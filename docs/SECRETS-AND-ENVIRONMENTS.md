# Sekrety i środowiska (bez Azure Key Vault)

> **Uwaga:** to repozytorium jest snapshotem pokazowym. Workflowy deploymentu
> (`deploy.yml`, `rollback.yml`, `fetch-local-env.yml`) nie są jego częścią — poniższy
> opis dokumentuje, jak działa środowisko produkcyjne, ale samych plików tu nie znajdziesz.

Aplikacja opiera się na **wbudowanym łańcuchu konfiguracji ASP.NET Core**: `appsettings.json` → `appsettings.{Environment}.json` → **zmienne środowiskowe** (najwyższy priorytet) → opcjonalnie **dotnet user-secrets** (tylko lokalnie, profil `Development`).

Sekretów **nie commitujesz** do git (hasła, connection stringi z hasłem, klucze ACS). W repozytorium zostają wyłącznie **szablony** i wartości niepoufne (np. publiczne ClientId API po stronie walidacji JWT).

---

## Development (lokalnie)

**Opcja A — plik `appsettings.Development.json` (wygodne na start)**  
Skopiuj `appsettings.Development.example.json`, uzupełnij connection string i Entra. Plik jest często commitowany w małych zespołach z **słabymi hasłami tylko na Docker Postgres**; dla prawdziwych sekretów użyj B lub C.

**Opcja B — `dotnet user-secrets` (zalecane na wrażliwe wartości)**  
Projekt `App.Api` ma już `UserSecretsId`. Przykłady:

```bash
cd backend/src/App.Api

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;..."
dotnet user-secrets set "AzureCommunication:Email:ConnectionString" "endpoint=...;accesskey=..."
dotnet user-secrets set "AzureCommunication:Email:SenderAddress" "donotreply@twojadomena.pl"
```

User secrets **ładowane są tylko** gdy `ASPNETCORE_ENVIRONMENT=Development`. Nie trafiają do builda ani obrazu Dockera, jeśli ich nie kopiujesz.

**Opcja C — zmienne środowiskowe w IDE / terminalu**  
Ten sam efekt co na produkcji (patrz niżej), np. przed `dotnet run`:

`ConnectionStrings__DefaultConnection=...`

---

## Production (Hetzner VPS + Docker + GitHub Actions)

**Źródło prawdy:** **GitHub Secrets + Variables**. `.github/workflows/deploy.yml` generuje plik
`/opt/zapiszme/.env` na serwerze przy każdym deployu. Docker Compose czyta go automatycznie.

**NIE TRZYMAJ `.env` ręcznie na serwerze** — zostanie nadpisany. Dodaj wartość w GitHub, nie w SSH.

### Mapping GitHub → kontener

| GH klucz | Typ GH | Used by | Notes |
|----------|--------|---------|-------|
| `SSH_KEY` | Secret | deploy.yml | Private key do SSH na VPS |
| `HOST` | Secret | deploy.yml | Hostname/IP VPS-a (Secret, by nie wyciekało w logach PR) |
| `USER` | Secret | deploy.yml | Użytkownik SSH (Secret, surface area reduction) |
| `GHCR_USERNAME` | Secret | deploy.yml + server pull | GitHub username (Secret dla spójności) |
| `GHCR_TOKEN` | Secret | deploy.yml + server pull | PAT (`read:packages`) |
| `DB_PASSWORD` | Secret | compose `db`, `api`, `postgres-exporter`, `umami` | Hasło Postgres `admin` |
| `TURNSTILE_SITE_KEY` | Variable | compose `api` (Managed widget) | Site-key Managed widget (dashboard: login/register/forgot-password). Osadzony w HTML — publiczny. |
| `TURNSTILE_SECRET_KEY` | Secret | compose `api` (Managed widget) | Secret pairing z Managed site-key |
| `TURNSTILE_INVISIBLE_SITE_KEY` | Variable | compose `api` (Invisible widget) | Site-key Invisible widget (web: public booking-flow). Osadzony w HTML — publiczny. |
| `TURNSTILE_INVISIBLE_SECRET_KEY` | Secret | compose `api` (Invisible widget) | Secret pairing z Invisible site-key |
| `ACS_EMAIL_CONNECTION_STRING` | Secret | compose `api` | Azure Communication Services |
| `ACS_SENDER_ADDRESS` | Variable | compose `api` | np. `no-reply@zapisz.me` |
| `ACS_FEEDBACK_RECIPIENT` | Variable | compose `api` | inbox dla zgłoszeń z FeedbackController |
| `BOOKING_ADMIN_EMAIL` | Variable | compose `api` | Email pierwszego admina (bootstrap) |
| `BOOKING_ADMIN_PASSWORD` | Secret | compose `api` | Hasło pierwszego admina (jednorazowo używane) |
| `UMAMI_APP_SECRET` | Secret | compose `umami` (monitoring) | APP_SECRET Umami |
| `MEDIATR_LICENSE_KEY` | Secret | compose `api` | MediatR 14 (Lucky Penny Software) — komercyjna licencja JWT |
| `SMSAPI_OAUTH_TOKEN` | Secret | compose `api` (`Sms__OAuthToken`) | Token OAuth z panelu smsapi.pl. **Puste = kanał SMS wyłączony** (OTP SMS przy rejestracji padnie). |
| `SMS_SENDER_NAME` | Variable | compose `api` (`Sms__SenderName`) | Pole nadawcy, max 11 znaków, pre-zarejestrowane w smsapi.pl. Default `INFO`. |

> **SMS-first:** `Sms__OAuthToken` jest generowany do `.env` przez `deploy.yml` (z GH Secret `SMSAPI_OAUTH_TOKEN`) i wymagany — bez niego deploy padnie na walidacji. Szczegóły: sekcja [SMS (smsapi.pl)](#sms-smsapipl) niżej.

### Ustawienie przez `gh` CLI

```bash
# Secrets (sensitive — masked in logs)
gh secret set SSH_KEY < ~/.ssh/zapiszme_deploy   # już ustawione
gh secret set HOST              # już ustawione (zostaje)
gh secret set USER              # już ustawione (zostaje)
gh secret set GHCR_USERNAME     # już ustawione (zostaje)
gh secret set GHCR_TOKEN        # już ustawione (zostaje)
gh secret set DB_PASSWORD
gh secret set TURNSTILE_SECRET_KEY              # Managed widget secret
gh secret set TURNSTILE_INVISIBLE_SECRET_KEY    # Invisible widget secret (osobny w CF)
gh secret set ACS_EMAIL_CONNECTION_STRING
gh secret set BOOKING_ADMIN_PASSWORD
gh secret set UMAMI_APP_SECRET
gh secret set MEDIATR_LICENSE_KEY    # JWT z https://luckypennysoftware.com
gh secret set SMSAPI_OAUTH_TOKEN     # token OAuth z panelu smsapi.pl (wymagane dla SMS-first)

# Variables (visible — env-specific config widoczne w logach)
gh variable set TURNSTILE_SITE_KEY            --body "0x4AAA..."   # Managed (dashboard)
gh variable set TURNSTILE_INVISIBLE_SITE_KEY  --body "0x4AAA..."   # Invisible (web)
gh variable set ACS_SENDER_ADDRESS     --body "no-reply@zapisz.me"
gh variable set ACS_FEEDBACK_RECIPIENT --body "feedback@zapisz.me"
gh variable set BOOKING_ADMIN_EMAIL    --body "admin@zapisz.me"
gh variable set SMS_SENDER_NAME        --body "Zapisz.me"   # max 11 znaków, zatwierdzone w smsapi.pl
```

### Rzeczy do USUNIĘCIA przy migracji

Jeśli wcześniej trzymałeś:
- `POSTGRES_CONNECTION_STRING` w GH Secret → **usuń**, zastąpione przez `DB_PASSWORD` + inline build w deploy.
- Ręczny `/opt/zapiszme/.env` na serwerze → **usuń**, deploy nadpisze.

Stare `HOST`, `USER`, `GHCR_USERNAME` jako Secrets **zostają bez zmian** — surface area reduction
(jako Secrets nie wyciekają w logach PR-ów / forki). Pełna zasada: jeśli wartość nie musi być
widoczna w logach żeby ułatwić debug, zostaje Secretem.

### Co dzieje się na deployu

1. CI Gate (tests) musi zielony.
2. Build + push 3 obrazów do GHCR.
3. SSH do VPS → przygotowanie `/opt/zapiszme`.
4. Runner generuje `.env` z GH Secrets+Variables → `scp` na serwer (chmod 600).
5. SSH login do GHCR z serwera.
6. `docker run --rm` z `RUN_MIGRATIONS_AND_EXIT=1` → migracje + bootstrap admina (idempotentne).
7. `docker compose up -d` → restart stacka.
8. Smoke check: `curl https://api.zapisz.me/health/smoke` musi zwrócić 200.

### Mapping `appsettings:Production.json` vs ENV

`appsettings.Production.json` w repo trzyma **niepoufne defaulty** (CORS allowed origins,
Dashboard:BaseUrl, ReverseProxy:TrustedProxies, rate-limit values). **Sekrety zawsze
nadpisuje ENV** (klucze `Section__SubSection` w pliku `.env`).

| Konfiguracja (JSON) | Zmienna środowiskowa |
|---------------------|----------------------|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `AzureCommunication:Email:ConnectionString` | `AzureCommunication__Email__ConnectionString` |
| `Turnstile:SecretKey` | `Turnstile__SecretKey` |
| `Sms:OAuthToken` | `Sms__OAuthToken` |
| `Sms:SenderName` | `Sms__SenderName` |
| `Cors:AllowedOrigins:0` | `Cors__AllowedOrigins__0` *(lista zwykle w appsettings)* |

---

## SMS (smsapi.pl)

Sekcja `Sms` w `appsettings.json` (klasa `SmsApiOptions`). smsapi.pl to **realne pieniądze** —
każdy SMS to kredyt. Klucze:

| Klucz (JSON) | ENV | Wymagane na prod? | Default | Notes |
|--------------|-----|-------------------|---------|-------|
| `Sms:OAuthToken` | `Sms__OAuthToken` | ✅ (SMS-first) | `""` | **Puste = kanał SMS wyłączony.** OTP SMS przy rejestracji rzuci wyjątek. GH Secret `SMSAPI_OAUTH_TOKEN`, **wymagany** w `deploy.yml`. |
| `Sms:SenderName` | `Sms__SenderName` | ⚠️ zalecane | `INFO` | Pole nadawcy, max 11 znaków, **pre-zarejestrowane** w panelu smsapi.pl. GH Variable `SMS_SENDER_NAME`, opcjonalny (fallback `INFO`). |
| `Sms:BaseUrl` | `Sms__BaseUrl` | ❌ | `https://api.smsapi.pl/` | Endpoint API. |
| `Sms:TestMode` | `Sms__TestMode` | ❌ | `false` | `true` → `test=1`, smsapi.pl symuluje wysyłkę bez naliczania kredytów. **Na prawdziwym prod = `false`.** |
| `Sms:TimeoutSeconds` | `Sms__TimeoutSeconds` | ❌ | `10` | Timeout HTTP. |

> **Status:** wpięte w `deploy.yml` — `Sms__OAuthToken=${SMSAPI_OAUTH_TOKEN}` (Secret, wymagany)
> + `Sms__SenderName=${SMS_SENDER_NAME:-INFO}` (Variable, opcjonalny). Pozostaje tylko ustawić
> `gh secret set SMSAPI_OAUTH_TOKEN` przed pierwszym deployem, inaczej walidacja w workflow przerwie deploy.

---

## Backupy bazy (off-site → Cloudflare R2)

Produkcyjny Postgres ma dwie warstwy kopii:

| Warstwa | Skrypt | Kiedy | Gdzie | Retencja |
|---------|--------|-------|-------|----------|
| Pre-migrate (lokalna) | `scripts/backup-db.sh` | przy każdym deployu, PRZED migracją | `/opt/zapiszme/backups/<sha>.sql.gz.age` (ten sam dysk, szyfrowane `age` jeśli włączony off-site) | 15 ostatnich |
| Pre-migrate (off-site) | `scripts/backup-db.sh` | przy każdym deployu, PRZED migracją | R2 `…/db/predeploy/` (TYLKO gdy szyfrowane `age` — inaczej skip) | `OFFSITE_KEEP_PREDEPLOY_DAYS` (30 dni) |
| Off-site `frequent` | `scripts/backup-offsite.sh frequent` | cron co 15 min | R2 `…/db/frequent/` (szyfrowane `age`) | `OFFSITE_KEEP_FREQUENT_DAYS` (3 dni) |
| Off-site `daily` | `scripts/backup-offsite.sh daily` | cron 03:30 | R2 `…/db/daily/` (szyfrowane `age`) | `OFFSITE_KEEP_DAILY_DAYS` (30 dni) |

Off-site to `pg_dump | gzip | age (PII/RODO) | rclone → R2`. `deploy.yml` generuje
`/opt/zapiszme/.backup.env` (chmod 600) i instaluje crontab deploy-usera (idempotentnie).
Cała warstwa jest **opcjonalna** — bez sekretów `R2_BACKUP_*` deploy działa jak dawniej
(tylko `::warning::`, brak crona).

> **Lokalne dumpy też szyfrowane:** gdy off-site jest włączony (`.backup.env` z `AGE_RECIPIENT`),
> pre-migrate dumpy są szyfrowane tym samym kluczem publicznym → `<sha>.sql.gz.age` (RODO art. 32,
> ochrona at-rest na dysku VPS). **Konsekwencja:** `rollback.sh --with-db` wymaga wtedy klucza
> PRYWATNEGO age (offline) — podaj `BACKUP_AGE_IDENTITY=/ścieżka` lub wgraj go tymczasowo do
> `/opt/zapiszme/secrets/backup-identity.txt`. Czysty rollback kodu (bez `--with-db`) klucza nie
> wymaga. Off-site cron `pg_dump` jest serializowany `flock`-iem i odpalany z `nice`/`ionice`.

> **Izolacja od mediów:** backupy mają OSOBNY, **prywatny** bucket (`R2_BACKUP_BUCKET`),
> OSOBNY token R2 scoped tylko do niego oraz OSOBNY endpoint **EU-jurisdiction**
> (`R2_BACKUP_ENDPOINT`, host `*.eu.r2.cloudflarestorage.com`). Mediowych `R2_*` (publiczny
> bucket, jurysdykcja default) **nie reużywamy** — backup bazy = całe PII, nie może trafić do
> publicznie-czytelnego bucketa ani opuścić EOG (RODO). Token backupu **nie trafia** do
> kontenera api (tylko host).
>
> **EOG/RODO:** bucket backupów MUSI być utworzony w **jurysdykcji EU** — wtedy dane fizycznie
> zostają w UE, zgodnie z polityką prywatności (`web/src/pages/polityka-prywatnosci.astro`,
> Cloudflare wpisany jako podprocesor). Jeśli `R2_BACKUP_ENDPOINT` nie jest ustawiony, deploy
> używa endpointu konta (default jurisdiction) i **głośno ostrzega** — wtedy PII może wyjść poza EOG.

### Sekrety / zmienne

| GH klucz | Typ GH | Used by | Notes |
|----------|--------|---------|-------|
| `R2_BACKUP_ACCESS_KEY_ID` | Secret | `.backup.env` (rclone) | Access Key ID tokena R2 scoped do bucketa backupów. **Gate** — puste = cała warstwa off-site pominięta. |
| `R2_BACKUP_SECRET_ACCESS_KEY` | Secret | `.backup.env` (rclone) | Secret pairing |
| `R2_BACKUP_BUCKET` | Variable | `.backup.env` (rclone) | Prywatny bucket **w jurysdykcji EU**, np. `zapiszme-backups` |
| `R2_BACKUP_ENDPOINT` | Variable | `.backup.env` (rclone) | Endpoint **EU-jurisdiction** `https://<account-id>.eu.r2.cloudflarestorage.com`. Pusty → fallback na `R2_ENDPOINT` z ostrzeżeniem (PII poza EOG). |
| `R2_ENDPOINT` | Variable | media + fallback backupu | Endpoint konta (default jurysdiction) `https://<account-id>.r2.cloudflarestorage.com` |
| `BACKUP_AGE_RECIPIENT` | Variable | `.backup.env` (`AGE_RECIPIENT`) | **Publiczny** klucz age (`age1…`). Prywatny trzymaj OFFLINE — NIE na serwerze. |
| `BACKUP_HEARTBEAT_URL` | Variable | `.backup.env` (`HEARTBEAT_URL`) | Opcjonalny dead-man's-switch (healthchecks.io). Puste = bez pinga. |

```bash
gh secret set R2_BACKUP_ACCESS_KEY_ID
gh secret set R2_BACKUP_SECRET_ACCESS_KEY
gh variable set R2_BACKUP_BUCKET     --body "zapiszme-backups"
gh variable set R2_BACKUP_ENDPOINT   --body "https://<account-id>.eu.r2.cloudflarestorage.com"  # EU jurisdiction
gh variable set BACKUP_AGE_RECIPIENT --body "age1..."          # PUBLICZNY klucz (age-keygen)
gh variable set BACKUP_HEARTBEAT_URL --body "https://hc-ping.com/<uuid>"   # opcjonalnie
```

### Prerekwizyty jednorazowe

1. **Cloudflare R2:** utwórz **prywatny** bucket (bez public access) **w jurysdykcji EU**
   (R2 → Create bucket → Location: *European Union (EU)*) + **R2 API token** z
   *Object Read & Write* **scoped tylko do tego bucketa**. Endpoint EU ma postać
   `https://<account-id>.eu.r2.cloudflarestorage.com` → ustaw go w `R2_BACKUP_ENDPOINT`.
2. **Klucz age:** `age-keygen -o backup-key.txt`. Prywatny klucz → menedżer haseł (OFFLINE).
   Pubkey → `BACKUP_AGE_RECIPIENT`.
3. **Host (Hetzner, raz):** `sudo apt-get install -y age` oraz rclone
   (`curl https://rclone.org/install.sh | sudo bash`). Skrypt faila czytelnie bez nich.

### RESTORE (na scratch — NIGDY wprost na prod bez analizy)

```bash
# .backup.env w środowisku (RCLONE_CONFIG_R2_* + RCLONE_REMOTE), backup-key.txt = klucz PRYWATNY
set -a; . /opt/zapiszme/.backup.env; set +a
rclone lsl r2:${R2_BACKUP_BUCKET}/db/daily/          # wybierz plik
rclone copy r2:${R2_BACKUP_BUCKET}/db/daily/saas_db-<stamp>.sql.gz.age .
age -d -i backup-key.txt saas_db-<stamp>.sql.gz.age | gunzip | \
  docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
    exec -T db psql -U admin -d <scratch_db>
```

> **PITR (point-in-time recovery)** przez WAL archiving (pgBackRest / WAL-G) NIE jest wdrożone —
> przy obecnej skali częsty `pg_dump` co 15 min wystarcza (RPO ≤ 15 min). PITR to osobny,
> większy projekt na później.

---

## Front (Angular / Astro)

- **Angular:** `environment.production.ts` budowany w pipeline — **nie wklejaj** tam sekretów typu client secret (w SPA i tak go nie używasz). ClientId Entra dla SPA jest publiczny; ewentualnie nadpisuj `apiBaseUrl` zmienną CI przed `ng build`.
- **Astro `web`:** `PUBLIC_*` — tylko to, co może być publiczne (URL API).

---

## Checklist przed pierwszym deployem na produkcję

- [ ] 13 GH Secrets ustawione (`gh secret list`): SSH_KEY, HOST, USER, GHCR_USERNAME, GHCR_TOKEN, DB_PASSWORD, TURNSTILE_SECRET_KEY, TURNSTILE_INVISIBLE_SECRET_KEY, ACS_EMAIL_CONNECTION_STRING, BOOKING_ADMIN_PASSWORD, UMAMI_APP_SECRET, MEDIATR_LICENSE_KEY, SMSAPI_OAUTH_TOKEN.
- [ ] 6 GH Variables ustawione (`gh variable list`): TURNSTILE_SITE_KEY, TURNSTILE_INVISIBLE_SITE_KEY, ACS_SENDER_ADDRESS, ACS_FEEDBACK_RECIPIENT, BOOKING_ADMIN_EMAIL, SMS_SENDER_NAME.
- [ ] `SMSAPI_OAUTH_TOKEN` ustawiony jako GH Secret (`deploy.yml` waliduje go jako required — bez tego deploy padnie).
- [ ] **2 widgety w Cloudflare Turnstile** (osobne site-keys + secret-keys): Managed (dashboard) + Invisible (web).
- [ ] Stary `POSTGRES_CONNECTION_STRING` Secret usunięty.
- [ ] Na VPS `/opt/zapiszme/` jest writable przez `DEPLOY_USER`.
- [ ] DNS dla `api.zapisz.me`, `admin.zapisz.me`, `zapisz.me` skierowane na VPS.
- [ ] Caddyfile w repo ma email Let's Encrypt + listę domen.
- [ ] Po pierwszym deployu: `curl https://api.zapisz.me/health/smoke` zwraca 200 z `"status":"Healthy"`.
- [ ] Po pierwszym deployu: login `BOOKING_ADMIN_EMAIL` + `BOOKING_ADMIN_PASSWORD` działa → zmień hasło przez UI.

---

## Dlaczego bez Key Vault

Na VPS (Hetzner) sekrety w **env / pliku tajnym na hoście** są prostsze w utrzymaniu niż połączenie do Azure z poświadczeniami aplikacji. Key Vault ma sens przy głębokiej integracji z Azure; ten projekt go **nie ładuje** — konfiguracja nadpisuje się standardowymi mechanizmami ASP.NET Core.
