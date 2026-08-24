---
description: Bramka testowa na żądanie — uruchamia testy TYLKO zmienionych obszarów (smart wg zmian), naprawia czerwone i dopisuje brakujące
argument-hint: (bez argumentów) | all (pełny przebieg) | quick (bez integracyjnych)
allowed-tools: Bash(.claude/scripts/verify-tests.sh:*), Bash(VERIFY_ALL=1 .claude/scripts/verify-tests.sh:*), Bash(VERIFY_SKIP_INTEG=1 .claude/scripts/verify-tests.sh:*), Bash(dotnet test:*), Bash(npm test:*), Read, Edit, Write, Grep, Glob
---

Jesteś **dyrygentem** bramki testowej. Cel: po zmianie kodu upewnić się, że testy zmienionych obszarów są ZIELONE, a nowy/zmieniony kod ma testy. W odróżnieniu od `/preflight-security`, ta bramka **naprawia** — wolno ci edytować kod i testy, aż przejdą. Wykonaj DOKŁADNIE poniższe kroki.

## Krok 1 — warstwa deterministyczna (uruchom testy zmienionych obszarów)

Wybór wywołania zależnie od argumentu `$ARGUMENTS`:
- brak argumentu → `.claude/scripts/verify-tests.sh`
- `all`   → `VERIFY_ALL=1 .claude/scripts/verify-tests.sh` (wymusza wszystkie zestawy)
- `quick` → `VERIFY_SKIP_INTEG=1 .claude/scripts/verify-tests.sh` (pomija wolne integracyjne)

Skrypt sam wykrywa zmienione pliki (working tree ∪ untracked ∪ commity vs `origin/main`) i odpala TYLKO powiązane zestawy:
`backend/src/App.Domain` → domain+application unit · `App.Application` → application unit · `App.Api`/`App.Infrastructure` → integration (Testcontainers) +application · `dashboard/**` → Vitest dashboard · `web/**` → Vitest web.

Przechwyć linie `STATUS|id|message` (STATUS: `RUN|PASS|FAIL|SKIP|WARN|INFO`) oraz exit code (1 = co najmniej jeden zestaw CZERWONY).

## Krok 2 — napraw czerwone testy (FAIL)

Dla KAŻDEGO `FAIL|...`:
1. Przeczytaj wskazany log (`/tmp/verify-*.log`) i znajdź pierwszą realną przyczynę (asercja vs błąd kompilacji vs setup).
2. Napraw **przyczynę źródłową**, nie objaw. Jeśli to regresja w kodzie — popraw kod. Jeśli test jest nieaktualny względem celowej zmiany zachowania — zaktualizuj test (i potwierdź, że nowa asercja odzwierciedla zamierzony kontrakt, a nie maskuje bug).
3. Przestrzegaj reguł z `CLAUDE.md` (handler dziedziczy `TenantHandler<,>`, test `TenantViolation`, brak hard-delete dla `ISoftDelete`, itp.) — nie wprowadzaj poprawki łamiącej te reguły.
4. Uruchom ponownie wąsko sam naprawiany zestaw (`dotnet test --project ...` lub `npm test` w danym katalogu), aż zielony.

## Krok 3 — dopisz brakujące testy (WARN cover-*)

Każdy `WARN|cover-backend|...` / `cover-dashboard` / `cover-web` oznacza: zmieniono kod bez odpowiadających testów.
- Ustal, co realnie się zmieniło (git diff danego obszaru) i dopisz testy pokrywające nowe/zmienione zachowanie — zgodnie z konwencją projektu (backend: minimum happy-path + `TenantViolation` dla operacji tenantowych; dashboard/web: Vitest obok komponentu).
- Jeśli zmiana to czysty refaktor bez nowego zachowania albo zmiana niepodlegająca testom (config, generowany klient, styl) — nie wymuszaj testu, tylko odnotuj to jednym zdaniem w raporcie.

## Krok 4 — ponów pełną bramkę

Po naprawach uruchom skrypt z Kroku 1 jeszcze raz. Powtarzaj Kroki 2–3, aż exit code = 0 (lub do jasnego punktu, w którym potrzebujesz decyzji użytkownika — wtedy zatrzymaj się i zapytaj).

## Krok 5 — raport (zwięźle, w czacie)

1. **ZIELONE / CZERWONE** + które zestawy uruchomiono (i które pominięto: brak Dockera / `node_modules` / `quick`).
2. Co naprawiono (`plik:linia`, jednozdaniowo) i jakie testy dopisano.
3. Pozostałe `WARN`, których świadomie nie pokryto — z uzasadnieniem.

Jeśli skrypt zwrócił `INFO nochange` (brak zmian w obszarach z testami) — napisz to wprost i nie uruchamiaj nic dalej.
