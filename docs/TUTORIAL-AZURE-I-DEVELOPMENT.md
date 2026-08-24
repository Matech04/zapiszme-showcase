# Tutorial: konfiguracja po refaktoryzacji (dev lokalnie + wdrożenie na Azure)

Ten dokument opisuje, **co uzupełnić** i **w jakiej kolejności**, żeby aplikacja działała u Ciebie w **Development** oraz żeby można było ją **wypuścić na Azure (Production)**.

---

## Część 1: Development na Twoim komputerze

### 1.1. Baza PostgreSQL (Docker)

Z katalogu głównego repozytorium:

```bash
docker compose up -d postgres
```

Domyślnie: `localhost:5432`, użytkownik/hasło `postgres`/`postgres`, baza `App_db` (zgodnie z `backend/src/App.Api/appsettings.Development.json`).

Jeśli zmienisz port lub hasło, zaktualizuj **`ConnectionStrings:DefaultConnection`** w tym samym pliku.

### 1.2. Migracje EF (API)

```bash
cd backend/src/App.Api
dotnet ef database update --project ../App.Infrastructure/App.Infrastructure.csproj
```

(Upewnij się, że `ASPNETCORE_ENVIRONMENT` jest `Development` albo użyj `--connection` wskazując na ten sam connection string co w `appsettings.Development.json`.)

### 1.3. Microsoft Entra (External ID / CIAM) — środowisko **dev**

W pliku **`backend/src/App.Api/appsettings.Development.json`** jest sekcja **`AzureAd`**. Dla **osobnego tenanta dev**:

- Zarejestruj w Entra **aplikację API** (backend) i **SPA** (dashboard), jak w dokumentacji Microsoft Identity.
- Wpisz w `appsettings.Development.json`: `Instance`, `TenantId`, `ClientId` (API), `Audience` (zwykle `api://{ClientId-API}`), `Domain` jeśli używasz.

Dashboard (`dashboard/src/environments/environment.ts`) musi mieć **te same wartości logicznie** co front SPA: `clientId` (SPA), `tenantId`, `authority`, `knownAuthorities`, `apiScope` (np. `api://{API-app-id}/access_as_user`).

### 1.4. Azure Communication Services (e-mail OTP)

Connection string **nie** jest commitowany w repo (pole jest puste w `appsettings.Development.json`).

Użyj **user secrets** (projekt API ma już `UserSecretsId`):

```bash
cd backend/src/App.Api
dotnet user-secrets set "AzureCommunication:Email:ConnectionString" "endpoint=...;accesskey=..."
dotnet user-secrets set "AzureCommunication:Email:SenderAddress" "donotreply@twojadomena.pl"
```

Listę kluczy możesz podejrzeć: `dotnet user-secrets list`.

### 1.5. Uruchomienie API

```bash
cd backend/src/App.Api
dotnet run
```

Profil z `Properties/launchSettings.json` ustawia **`ASPNETCORE_ENVIRONMENT=Development`**, więc ładowane są: `appsettings.json` + `appsettings.Development.json` + user secrets.

### 1.6. Dashboard (Angular)

- **`dashboard/src/environments/environment.ts`** — `apiBaseUrl` (np. `http://localhost:5140`) oraz blok **`msal`** zgodny z rejestracją **dev SPA**.
- Uruchomienie: `cd dashboard && npm start` (domyślnie build `development`).

### 1.7. Witryna rezerwacji (Astro)

W katalogu **`web/`**:

```bash
cp .env.example .env
```

Edytuj **`.env`**: ustaw `PUBLIC_API_BASE_URL` na URL API (np. `http://localhost:5140`, **bez** końcowego slasha).

```bash
cd web && npm run dev
```

### 1.8. Typowe problemy (dev)

| Objaw | Co sprawdzić |
|--------|----------------|
| „Brak ConnectionStrings:DefaultConnection” | Czy `ASPNETCORE_ENVIRONMENT=Development` i czy istnieje `appsettings.Development.json` z connection stringiem. |
| Błąd JWT / logowanie MSAL | Zgodność `ClientId`, `authority`, `audience` / `apiScope` między Entra, API i `environment.ts`. |
| OTP e-mail nie wychodzi | `dotnet user-secrets list` — czy są `AzureCommunication:Email:*`. |
| CORS z przeglądarki | W Development API pozwala origin localhost na dowolnym porcie; sprawdź URL frontu. |

---

## Część 2: Production na Azure

Założenie: **`ASPNETCORE_ENVIRONMENT=Production`** na App Service (lub równoważnie), API opublikowane jako aplikacja .NET na **Azure App Service** (lub kontener z tą samą konfiguracją).

### 2.1. Azure Database for PostgreSQL

- Utwórz serwer i bazę.
- Connection string w formacie Npgsql ustawiasz jako zmienną środowiskową lub w konfiguracji hosta (patrz [SECRETS-AND-ENVIRONMENTS.md](./SECRETS-AND-ENVIRONMENTS.md)).

### 2.2. Sekrety (produkcja i dev)

**Azure Key Vault nie jest używany w kodzie API.** Sekrety podajesz przez **zmienne środowiskowe** (App Service → Configuration, Docker, systemd na VPS) albo lokalnie przez **`dotnet user-secrets`** — pełna tabela kluczy i checklista: **[SECRETS-AND-ENVIRONMENTS.md](./SECRETS-AND-ENVIRONMENTS.md)**.

Na Azure App Service możesz nadal użyć **Key Vault references** w ustawieniach aplikacji — to warstwa platformy, nie osobnego kodu w repozytorium.

### 2.3. CORS (produkcja)

W **`appsettings.Production.json`** (lub wyłącznie w ustawieniach App Service) ustaw tablicę **`Cors:AllowedOrigins`** na **dokładne** adresy HTTPS dashboardu i strony rezerwacji (bez końcowego slasha), np.:

`https://app-twojadomena.pl`, `https://rezerwacje-twojadomena.pl`

### 2.4. Microsoft Entra — środowisko **prod**

Osobna rejestracja aplikacji (inne `TenantId` / `ClientId` niż dev). Wartości w zmiennych środowiskowych / Application settings (patrz [SECRETS-AND-ENVIRONMENTS.md](./SECRETS-AND-ENVIRONMENTS.md)).

### 2.5. Publikacja API

- Ustaw zmienne środowiskowe / connection stringi jak w [SECRETS-AND-ENVIRONMENTS.md](./SECRETS-AND-ENVIRONMENTS.md).
- Upewnij się, że na hoście jest **`ASPNETCORE_ENVIRONMENT=Production`**.
- Po wdrożeniu uruchom migracje na produkcyjnej bazie (np. `dotnet ef database update` z pipeline z sekretem connection stringa, albo skrypt inicjalizacyjny).

### 2.6. Dashboard (build produkcyjny)

1. Uzupełnij **`dashboard/src/environments/environment.production.ts`** (URL API prod, MSAL prod) **albo** generuj ten plik w CI (np. z zmiennych).
2. Build:

   ```bash
   cd dashboard && npm ci && npx ng build --configuration production
   ```

   (`angular.json` podmienia `environment.ts` na `environment.production.ts` dla tej konfiguracji.)

3. Hostuj wynik z `dist/` (np. **Azure Static Web Apps**, Storage + CDN, App Service static site).

**Uwaga:** obecnie budżet rozmiaru bundla w `angular.json` może zgłaszać błąd przy `production`; to osobny temat optymalizacji lub podniesienia limitów.

### 2.7. Astro (witryna publiczna)

- Ustaw **`PUBLIC_API_BASE_URL`** w środowisku buildu / hostingu (np. z `.env.production` na CI — wzorzec: `web/.env.production.example`).
- Build: `cd web && npm ci && npm run build`, deploy artefaktu (np. Node adapter na App Service lub Static Web Apps zgodnie z `astro.config.mjs`).

### 2.8. Checklista przed „go live”

- [ ] PostgreSQL prod + migracje
- [ ] Entra prod: API + SPA, role aplikacji (`Admin`, `Owner`, `Manager`, `Employee`) zgodne z politykami w `Program.cs`
- [ ] Zmienne środowiskowe / App Settings z connection stringiem i ACS (lub Key Vault reference w App Service)
- [ ] CORS: dokładne origin frontów
- [ ] `AllowedHosts` / TLS na App Service
- [ ] Fronty wskazują na właściwy URL API (`environment.production.ts`, `PUBLIC_API_BASE_URL`)

---

## Szybkie odniesienie do plików

| Cel | Plik / miejsce |
|-----|-----------------|
| Dev: DB, Entra w pliku | `backend/src/App.Api/appsettings.Development.json` |
| Dev: ACS (sekret) | `dotnet user-secrets` w `backend/src/App.Api` |
| Wspólne, bez sekretów | `backend/src/App.Api/appsettings.json` |
| Prod: szablon CORS (sekrety tylko z env) | `backend/src/App.Api/appsettings.Production.json` |
| Sekrety: dev + prod + mapowanie env | [SECRETS-AND-ENVIRONMENTS.md](./SECRETS-AND-ENVIRONMENTS.md) |
| Dev: MSAL + URL API | `dashboard/src/environments/environment.ts` |
| Prod: MSAL + URL API | `dashboard/src/environments/environment.production.ts` |
| Dev: URL API (Astro) | `web/.env` (z `.env.example`) |
| Prod: URL API (Astro) | `web/.env.production` / zmienne CI (wzorzec `.env.production.example`) |
| Postgres lokalnie | `docker-compose.yml` |

Jeśli coś z tej listy zmienisz nazwą lub ścieżką w kodzie, zaktualizuj ten plik razem z refaktoryzacją.
