---
description: Bramka bezpieczeństwa przed deployem — warstwa deterministyczna + siatka subagentów audytowych, zwraca werdykt GO/NO-GO
argument-hint: (bez argumentów)
allowed-tools: Bash(.claude/scripts/preflight-check.sh:*), Bash(mkdir:*), Read, Write, Agent
---

Jesteś **dyrygentem** bramki bezpieczeństwa przed-deployowej. Celem jest jeden zrankowany raport z werdyktem **GO / NO-GO**. Bramka ma dwie warstwy: deterministyczną (skrypt) i agentową (specjaliści osądu). Wykonaj DOKŁADNIE poniższe kroki.

## Krok 1 — warstwa deterministyczna

Uruchom: `.claude/scripts/preflight-check.sh`

Zapamiętaj exit code i przechwyć linie w formacie `SEVERITY|id|message`. Exit 1 = są blokery (CRITICAL/HIGH) już na tej warstwie. (Szybki przebieg bez testów: `PREFLIGHT_SKIP_TESTS=1 .claude/scripts/preflight-check.sh` — używaj tylko gdy użytkownik prosi o szybkie sprawdzenie.)

## Krok 2 — siatka subagentów (równolegle)

W JEDNEJ wiadomości odpal WSZYSTKICH 10 subagentów audytowych równolegle przez narzędzie Agent (są read-only, nie zmienią kodu):
- `preflight-sms-cost` — nadużycia kosztowe SMS (smsapi.pl = realne pieniądze)
- `preflight-tenant-isolation` — izolacja wielodostępności (query filters, TenantViolation, DbSet bez filtra)
- `preflight-authz` — autoryzacja (endpoint→polityka, dostęp anonimowy, eskalacja Employee, impersonacja)
- `preflight-booking-abuse` — nadużycia publicznego bookingu (brute-force OTP, slot hoarding, enumeracja)
- `preflight-secrets-config` — sekrety/konfiguracja (CORS/cookies, wyciek błędów, maskowanie logów, fail-fast prod)
- `preflight-deps` — łańcuch dostaw (podatności osiągalne, porzucone pakiety, lockfile, typosquat)
- `preflight-migration-safety` — migracje EF (destrukcyjne operacje, Cascade, indeks TenantId, utrata danych)
- `preflight-privacy-rodo` — dane osobowe/RODO (prawo do usunięcia vs soft-delete, PII w SMS/e-mail/logach, dokumenty)
- `preflight-resilience` — niezawodność/DoS nie-SMS (zapytania bez limitu, timeouty, izolacja awarii jobów)
- `preflight-input-injection` — wstrzyknięcia/XSS (dane klienta → dashboard/SMS/e-mail, surowy SQL, overposting)

Każdy zwraca raport z blokami `### [SEVERITY] …` (severity, lokalizacja, scenariusz, wpływ, naprawa, test).

Jeśli któryś typ subagenta jest „not found" (świeżo dodany plik, sesja nie przeładowana), odpal zamiennie `general-purpose` z poleceniem: „przeczytaj `.claude/agents/<nazwa>.md`, wciel się w tę rolę i zwróć raport w zdefiniowanym tam formacie".

## Krok 3 — synteza

Połącz znaleziska deterministyczne i agentowe:
- Deduplikuj nakładające się znaleziska (np. skrypt i agent o tym samym capie).
- Uszereguj wg `severity`, w obrębie severity wg realnego wpływu (koszt/blast radius).
- Werdykt: **NO-GO**, jeśli jest choć jedno `CRITICAL` lub `HIGH` (z dowolnej warstwy). W przeciwnym razie **GO** (z listą `MEDIUM/LOW` do zaplanowania).

## Krok 4 — raport

Zapisz raport do `test-results/preflight-<RRRR-MM-DD>.md` (utwórz katalog, jeśli trzeba) wg szablonu z `.claude/preflight/release-gate.md`. Następnie wypisz w czacie ZWIĘŹLE:
1. Werdykt **GO / NO-GO** + liczby (CRITICAL/HIGH/MEDIUM/LOW).
2. Listę blokerów (CRITICAL/HIGH) z `plik:linia` i jednozdaniową naprawą.
3. Ścieżkę do pełnego raportu.

Nie proponuj samodzielnie poprawek kodu w tym przebiegu — bramka tylko diagnozuje. Jeśli użytkownik zechce, naprawy robimy osobno.
