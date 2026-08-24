# Release Gate — bramka bezpieczeństwa przed deployem

Uruchamiana komendą `/preflight-security` przed każdym deployem na produkcję.
Ten plik = żywy spis tego, co sprawdzamy + rubryka severity + szablon raportu.
Nie jest raportem z konkretnego przebiegu — te lądują w `test-results/preflight-<data>.md`.

## Architektura

Dwie warstwy, jeden werdykt:

1. **Deterministyczna** — `.claude/scripts/preflight-check.sh`. Szybka, tania, powtarzalna, bez LLM. Build+testy, podatne zależności, sekrety w configu, regresja kontrolek kosztu SMS i rate-limit. Exit 1 = blokery.
2. **Agentowa** — subagenci read-only w `.claude/agents/` (osąd o kodzie tam, gdzie deterministyka nie wystarcza). Dyrygentem jest komenda `/preflight-security`, która odpala ich równolegle i składa wyniki.

## Rubryka severity (wspólna dla obu warstw)

| Severity | Znaczenie | Wpływ na werdykt |
|---|---|---|
| CRITICAL | Aktywnie eksploatowalne z zewnątrz, duży koszt/wyciek/utrata izolacji tenantów | **blokuje deploy** |
| HIGH | Realne nadużycie albo poważna luka wymagająca minimalnego wysiłku | **blokuje deploy** |
| MEDIUM | Ograniczony wpływ lub wymaga warunków; hardening | raport, nie blokuje |
| LOW | Drobne / defense-in-depth | raport, nie blokuje |

**Werdykt:** NO-GO przy ≥1 CRITICAL/HIGH; w przeciwnym razie GO.

## Pokrycie (warstwy → obszary)

### Deterministyczne (skrypt)
- [x] Backend build + `dotnet test backend/backend.slnx` (regresja testów nadużyć)
- [x] Podatne zależności: NuGet (`dotnet list package --vulnerable`) + npm audit (dashboard, web)
- [x] Sekrety w configu: `Sms:OAuthToken` pusty, brak haseł w connection stringach
- [x] Regresja kontrolek kosztu SMS: cap per-telefon/per-IP w `BookingOtpProtectionService`
- [x] Rate-limit: polityka `PublicBookingWrite` w configu + `[EnableRateLimiting]` na `PublicOtpController`
- [x] `Sms:TestMode` nie jest `true` w bazowym appsettings (prod realnie wysyła)
- [x] Canary migracji: DropColumn/DropTable/Cascade w nowych migracjach → do przeglądu
- [x] Canary frontu: brak sekretów w `environment.production.ts`, `web/.env*` nieśledzony
- [x] Operability: obecność health endpointów (`MapHealthChecks`)

### Agentowe (subagenci) — 10
- [x] `preflight-sms-cost` — drenaż kredytów SMS, obejścia capów, numery zagraniczne/premium
- [x] `preflight-tenant-isolation` — query filters, `TenantViolation`, handlery bez `TenantHandler`, `DbSet` bez filtra
- [x] `preflight-authz` — mapa endpoint→polityka, dostęp anonimowy, eskalacja roli Employee
- [x] `preflight-booking-abuse` — brute-force OTP, slot hoarding/holdy, enumeracja telefon/e-mail/slug
- [x] `preflight-secrets-config` — CORS/cookies (`SameSite`/`LOCAL_PROD`), wyciek stacktrace, maskowanie logów, historia git
- [x] `preflight-deps` — głębszy przegląd supply-chain ponad surowy audit
- [x] `preflight-migration-safety` — destrukcyjne migracje, Cascade wymiatający historię, indeks TenantId, odwracalność
- [x] `preflight-privacy-rodo` — prawo do usunięcia vs soft-delete, retencja, PII w SMS/e-mail/logach, dokumenty prawne
- [x] `preflight-resilience` — zapytania bez limitu, timeouty/CB, izolacja awarii jobów, pojedynczy punkt awarii
- [x] `preflight-input-injection` — stored XSS (dashboard/e-mail), wstrzyknięcie do SMS, surowy SQL, overposting

## Szablon raportu (`test-results/preflight-<data>.md`)

```markdown
# Preflight Security — <RRRR-MM-DD>

## Werdykt: <GO | NO-GO>
CRITICAL=<n>  HIGH=<n>  MEDIUM=<n>  LOW=<n>

## Blokery (CRITICAL/HIGH)
1. [SEVERITY] <tytuł> — <plik:linia> — naprawa: <jedno zdanie>
...

## Pozostałe (MEDIUM/LOW)
- [SEVERITY] <tytuł> — <plik:linia>
...

## Warstwa deterministyczna (surowe wyniki)
<wklejone linie SEVERITY|id|message + exit code>

## Raporty subagentów
### preflight-sms-cost
<pełny raport>
```

## Konwencje
- Subagent = `.claude/agents/<nazwa>.md` (frontmatter `name/description/tools/model`, body = system prompt + ścisły format wyniku). Read-only (`Read, Grep, Glob, Bash`).
- Nowy obszar ryzyka → nowy subagent + odhaczenie na liście wyżej. Twardych, tanich, deterministycznych sprawdzeń NIE rób agentem — dopisuj do skryptu.
