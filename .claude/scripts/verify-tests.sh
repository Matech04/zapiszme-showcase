#!/usr/bin/env bash
# Bramka testowa na żądanie (/verify-tests) — warstwa deterministyczna.
# "Smart wg zmian": wykrywa, które obszary repo się zmieniły i uruchamia TYLKO
# powiązane zestawy testów. Wynik w stałym formacie:
#   STATUS|id|message
# STATUS: RUN | PASS | FAIL | SKIP | WARN | INFO
# Exit 1, jeśli choć jeden zestaw testów był CZERWONY (FAIL).
#
# Czyta to komenda /verify-tests (dyrygent) i na tej podstawie naprawia/dopisuje testy.
# Można też uruchomić samodzielnie: .claude/scripts/verify-tests.sh
#
# Zmienne środowiskowe:
#   VERIFY_SKIP_INTEG=1  — pomiń wolne testy integracyjne (Testcontainers/Docker)
#   VERIFY_ALL=1         — wymuś WSZYSTKIE zestawy niezależnie od zmian (pełny przebieg)
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

FAIL=0
RAN=0
SKIP_INTEG="${VERIFY_SKIP_INTEG:-0}"
ALL="${VERIFY_ALL:-0}"

emit() { printf '%s|%s|%s\n' "$1" "$2" "${*:3}"; }
has()  { command -v "$1" >/dev/null 2>&1; }

DOMAIN_PROJ="backend/tests/App.Domain.UnitTests/App.Domain.UnitTests.csproj"
APP_PROJ="backend/tests/App.Application.UnitTests/App.Application.UnitTests.csproj"
INTEG_PROJ="backend/tests/App.Api.IntegrationTests/App.Api.IntegrationTests.csproj"

echo "===== VERIFY-TESTS: warstwa deterministyczna ====="
echo "repo: $REPO_ROOT"

# ---------------------------------------------------------------------------
# 1. Zbiór zmienionych plików: working tree (staged+unstaged) ∪ untracked ∪ commity gałęzi vs origin/main
# ---------------------------------------------------------------------------
TRACKED="$(git diff --name-only HEAD 2>/dev/null || true)"
UNTRACKED="$(git ls-files --others --exclude-standard 2>/dev/null || true)"
BRANCH="$(git diff --name-only origin/main...HEAD 2>/dev/null || true)"
CHANGED="$(printf '%s\n%s\n%s\n' "$TRACKED" "$UNTRACKED" "$BRANCH" | grep -v '^$' | sort -u)"

n_changed=$(printf '%s\n' "$CHANGED" | grep -c . || true)
echo "zmienionych plików (working tree + gałąź vs origin/main): $n_changed"
echo

match() { printf '%s\n' "$CHANGED" | grep -qE "$1"; }

# ---------------------------------------------------------------------------
# 2. Mapowanie obszar → zestaw testów
# ---------------------------------------------------------------------------
RUN_DOMAIN=0; RUN_APP=0; RUN_INTEG=0; RUN_DASH=0; RUN_WEB=0

if [ "$ALL" = "1" ]; then
  RUN_DOMAIN=1; RUN_APP=1; RUN_INTEG=1; RUN_DASH=1; RUN_WEB=1
  emit INFO scope "VERIFY_ALL=1 — wymuszono wszystkie zestawy testów"
else
  # Domena podpiera aplikację → zmiana w domenie odpala też testy aplikacji.
  match '^backend/src/App\.Domain/'                    && { RUN_DOMAIN=1; RUN_APP=1; }
  match '^backend/src/App\.Application/'                && RUN_APP=1
  # Api/Infrastructure spinają handlery → integracyjne + aplikacyjne.
  match '^backend/src/(App\.Api|App\.Infrastructure)/'  && { RUN_INTEG=1; RUN_APP=1; }
  # Zmiana w samym projekcie testowym → odpal ten projekt.
  match '^backend/tests/App\.Domain\.UnitTests/'        && RUN_DOMAIN=1
  match '^backend/tests/App\.Application\.UnitTests/'    && RUN_APP=1
  match '^backend/tests/App\.Api\.IntegrationTests/'     && RUN_INTEG=1
  match '^dashboard/'                                    && RUN_DASH=1
  match '^web/'                                          && RUN_WEB=1
fi

# ---------------------------------------------------------------------------
# 3. Nudge "testy na bieżąco": kod zmieniony bez odpowiadających testów
# ---------------------------------------------------------------------------
echo "--- pokrycie testami (nudge) ---"
# Backend: src zmieniony, ale tests nietknięte.
if printf '%s\n' "$CHANGED" | grep -qE '^backend/src/' \
   && ! printf '%s\n' "$CHANGED" | grep -qE '^backend/tests/'; then
  emit WARN cover-backend "Zmieniono backend/src/** bez zmian w backend/tests/** — czy nowy kod ma testy?"
fi
# Dashboard: kod (poza generowanym klientem i specami) zmieniony, ale brak zmienionych *.spec/*.test.
dash_src="$(printf '%s\n' "$CHANGED" | grep -E '^dashboard/src/' | grep -vE '\.(spec|test)\.ts$' | grep -v 'core/api/api-client.ts' || true)"
dash_spec="$(printf '%s\n' "$CHANGED" | grep -E '^dashboard/.*\.(spec|test)\.ts$' || true)"
[ -n "$dash_src" ] && [ -z "$dash_spec" ] && emit WARN cover-dashboard "Zmieniono dashboard/src/** bez zmian w *.spec.ts — dopisz/zaktualizuj testy"
# Web: analogicznie (z pominięciem generowanego klienta bookingu).
web_src="$(printf '%s\n' "$CHANGED" | grep -E '^web/src/' | grep -vE '\.(spec|test)\.ts$' | grep -v 'lib/booking-openapi-client.ts' || true)"
web_spec="$(printf '%s\n' "$CHANGED" | grep -E '^web/.*\.(spec|test)\.ts$' || true)"
[ -n "$web_src" ] && [ -z "$web_spec" ] && emit WARN cover-web "Zmieniono web/src/** bez zmian w *.spec.ts — dopisz/zaktualizuj testy"
echo

# ---------------------------------------------------------------------------
# 4. Uruchomienie zestawów
# ---------------------------------------------------------------------------
run_dotnet() { # run_dotnet <label> <project> <log>
  local label="$1" proj="$2" log="$3"
  if [ ! -f "$proj" ]; then emit SKIP "$label" "brak projektu $proj (zmiana ścieżki?)"; return; fi
  emit RUN "$label" "dotnet test --project $proj"
  if dotnet test --project "$proj" >"$log" 2>&1; then
    emit PASS "$label" "zielone — $proj"
  else
    emit FAIL "$label" "CZERWONE — log: $log"
    FAIL=1
  fi
  RAN=1
}

run_npm() { # run_npm <label> <dir> <log> <cmd...>
  local label="$1" dir="$2" log="$3"; shift 3
  if [ ! -d "$dir/node_modules" ]; then
    emit SKIP "$label" "$dir: brak node_modules — uruchom 'npm install' (pomijam)"; return
  fi
  emit RUN "$label" "$dir: $*"
  if (cd "$dir" && "$@") >"$log" 2>&1; then
    emit PASS "$label" "zielone — $dir"
  else
    emit FAIL "$label" "CZERWONE — log: $log"
    FAIL=1
  fi
  RAN=1
}

echo "--- backend ---"
if ! has dotnet; then
  [ "$((RUN_DOMAIN+RUN_APP+RUN_INTEG))" -gt 0 ] && emit SKIP backend "dotnet niedostępny — pomijam testy backendu"
else
  [ "$RUN_DOMAIN" = 1 ] && run_dotnet domain      "$DOMAIN_PROJ" /tmp/verify-domain.log
  [ "$RUN_APP"    = 1 ] && run_dotnet application "$APP_PROJ"    /tmp/verify-application.log
  if [ "$RUN_INTEG" = 1 ]; then
    if [ "$SKIP_INTEG" = "1" ]; then
      emit SKIP integration "VERIFY_SKIP_INTEG=1 — pominięto testy integracyjne (Testcontainers)"
    elif ! has docker || ! docker info >/dev/null 2>&1; then
      emit WARN integration "Docker niedostępny — testy integracyjne (Testcontainers) pominięte; uruchom Dockera lub VERIFY_SKIP_INTEG=1"
    else
      run_dotnet integration "$INTEG_PROJ" /tmp/verify-integration.log
    fi
  fi
fi
echo

echo "--- frontend ---"
if ! has npm; then
  [ "$((RUN_DASH+RUN_WEB))" -gt 0 ] && emit SKIP frontend "npm niedostępny — pomijam testy frontu"
else
  [ "$RUN_DASH" = 1 ] && run_npm vitest-dashboard dashboard /tmp/verify-dashboard.log npm test -- --watch=false
  [ "$RUN_WEB"  = 1 ] && run_npm vitest-web       web       /tmp/verify-web.log       npm test
fi
echo

# ---------------------------------------------------------------------------
# 5. Podsumowanie + exit code
# ---------------------------------------------------------------------------
echo "===== PODSUMOWANIE ====="
if [ "$RAN" = "0" ]; then
  emit INFO nochange "Brak zmian w obszarach z testami (backend/dashboard/web) — nic nie uruchomiono"
  echo "WERDYKT: brak testów do uruchomienia"
  exit 0
fi
if [ "$FAIL" = "1" ]; then
  echo "WERDYKT: TESTY CZERWONE"
  exit 1
fi
echo "WERDYKT: wszystkie uruchomione testy zielone"
exit 0
