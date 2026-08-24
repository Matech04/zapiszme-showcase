#!/usr/bin/env bash
# Skan DAST (OWASP ZAP) lokalnie uruchomionej aplikacji.
# Uzupełnia analizę STATYCZNĄ z /preflight-security o warstwę "co dzieje się na żywym HTTP":
# brakujące nagłówki bezpieczeństwa, słabe ciasteczka, wycieki w odpowiedziach, XSS/injection.
#
# Dwie warstwy ZAP:
#   - BASELINE (pasywny, tylko spider+obserwacja)  -> frontend web + dashboard. Bezpieczny.
#   - API SCAN (AKTYWNY, wstrzykuje payloady)       -> opcjonalny, ZA jawną bramką.
#
# !!! UWAGA KOSZTOWA !!!
# Skan API uderza w publiczny flow rezerwacji (POST .../request-otp) => REALNE SMS-y (smsapi.pl).
# Dlatego skan API jest domyślnie WYŁĄCZONY. Włączasz go świadomie (ZAP_API_SCAN=1) i TYLKO
# z Sms:TestMode=true / atrapą providera, na izolowanej bazie. Nigdy na produkcji.
#
# Wynik: raporty HTML+JSON w test-results/zap/. Severity w formacie zgodnym z preflight-check.sh.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

# --- Konfiguracja (wszystko nadpisywalne env-em) ---
# Domyślne porty = worktree "main" (zob. .claude/worktree-ports.json).
ZAP_HOST="${ZAP_HOST:-localhost}"
ZAP_WEB_URL="${ZAP_WEB_URL:-http://${ZAP_HOST}:4321}"           # publiczny booking (Astro/Svelte)
ZAP_DASHBOARD_URL="${ZAP_DASHBOARD_URL:-http://${ZAP_HOST}:4201}" # panel staff (Angular)
ZAP_API_SPEC_URL="${ZAP_API_SPEC_URL:-http://${ZAP_HOST}:5141/swagger/v1-booking/swagger.json}"
ZAP_API_SCAN="${ZAP_API_SCAN:-0}"                               # 1 = włącz AKTYWNY skan API (patrz ostrzeżenie)
ZAP_IMAGE="${ZAP_IMAGE:-zaproxy/zap-stable}"
OUT_DIR="${ZAP_OUT_DIR:-$REPO_ROOT/test-results/zap}"

CRIT=0; HIGH=0; MED=0; LOW=0
emit() { local sev="$1" id="$2"; shift 2; printf '%s|%s|%s\n' "$sev" "$id" "$*"
  case "$sev" in CRITICAL) CRIT=$((CRIT+1));; HIGH) HIGH=$((HIGH+1));; MEDIUM) MED=$((MED+1));; LOW) LOW=$((LOW+1));; esac; }
has() { command -v "$1" >/dev/null 2>&1; }

echo "===== ZAP DAST scan ====="
echo "repo:    $REPO_ROOT"
echo "out:     $OUT_DIR"
echo "web:     $ZAP_WEB_URL"
echo "dash:    $ZAP_DASHBOARD_URL"
echo "apispec: $ZAP_API_SPEC_URL   (API scan: $([ "$ZAP_API_SCAN" = 1 ] && echo ON || echo off))"
echo

if ! has docker; then
  emit CRITICAL docker "docker niedostępny — ZAP działa jako kontener. Zainstaluj Docker i uruchom ponownie."
  echo; echo "WERDYKT: nie uruchomiono (brak docker)"; exit 1
fi

mkdir -p "$OUT_DIR"
chmod 777 "$OUT_DIR" 2>/dev/null || true   # ZAP w kontenerze działa jako uid 1000

# Czy cel odpowiada (curl z hosta). Brak odpowiedzi -> najpewniej nie odpalono `make prod-local-up` / wt-dev.
reachable() { # reachable <url>
  local url="$1"
  if has curl; then curl -fsS -o /dev/null --max-time 5 "$url" 2>/dev/null; return $?; fi
  return 0  # brak curl -> nie blokuj, ZAP sam zgłosi
}

# Uruchomienie skanu ZAP. --network host => kontener widzi localhost hosta (WSL2/Linux engine).
run_baseline() { # run_baseline <name> <url>
  local name="$1" url="$2"
  if ! reachable "$url"; then
    emit MEDIUM "reach-$name" "$name: $url nie odpowiada — pomijam (uruchom aplikację: make prod-local-up / wt-dev)"
    return
  fi
  echo "--- BASELINE (pasywny): $name -> $url ---"
  local html="zap-$name.html" json="zap-$name.json"
  set +e
  docker run --rm --network host -v "$OUT_DIR:/zap/wrk:rw" "$ZAP_IMAGE" \
    zap-baseline.py -t "$url" -r "$html" -J "$json" -I
  local rc=$?
  set -e 2>/dev/null
  # zap-baseline: 0=czysto, 1=FAIL(błąd skanu), 2=WARN. -I => nie zwraca 1 za same WARN-y.
  case "$rc" in
    0) emit PASS "$name" "$name: brak alertów ZAP (raport: test-results/zap/$html)";;
    2) emit LOW  "$name" "$name: ZAP zgłosił WARN — przejrzyj test-results/zap/$html";;
    *) emit MEDIUM "$name" "$name: ZAP zakończył kodem $rc — przejrzyj test-results/zap/$html";;
  esac
  echo
}

# --- 1. Frontendy: baseline pasywny (bezpieczny) ---
run_baseline web "$ZAP_WEB_URL"
run_baseline dashboard "$ZAP_DASHBOARD_URL"

# --- 2. API: AKTYWNY skan — tylko za jawną bramką ---
echo "--- API SCAN (aktywny) ---"
if [ "$ZAP_API_SCAN" != "1" ]; then
  emit SKIP api "Skan API wyłączony (domyślnie). Włącz: ZAP_API_SCAN=1 — ALE NAJPIERW ustaw Sms:TestMode=true (realne SMS!)."
else
  # Twarda bramka kosztowa: sprawdź, że TestMode jest włączony w configu, zanim wstrzykniemy payloady w request-otp.
  if grep -qE '"TestMode"[[:space:]]*:[[:space:]]*true' backend/src/App.Api/appsettings.Development.json 2>/dev/null \
     || [ "${ZAP_API_FORCE:-0}" = "1" ]; then
    if ! reachable "$ZAP_API_SPEC_URL"; then
      emit MEDIUM reach-api "Spec OpenAPI $ZAP_API_SPEC_URL nieosiągalny — Swagger jest tylko w Development (ASPNETCORE_ENVIRONMENT=Development)."
    else
      echo "AKTYWNY skan API wstrzykuje payloady do endpointów z $ZAP_API_SPEC_URL"
      set +e
      docker run --rm --network host -v "$OUT_DIR:/zap/wrk:rw" "$ZAP_IMAGE" \
        zap-api-scan.py -t "$ZAP_API_SPEC_URL" -f openapi -r zap-api.html -J zap-api.json -I
      local_rc=$?
      set -e 2>/dev/null
      case "$local_rc" in
        0) emit PASS api "API: brak alertów ZAP (raport: test-results/zap/zap-api.html)";;
        2) emit LOW  api "API: ZAP zgłosił WARN — test-results/zap/zap-api.html";;
        *) emit MEDIUM api "API: ZAP zakończył kodem $local_rc — test-results/zap/zap-api.html";;
      esac
    fi
  else
    emit HIGH api-testmode "ZAP_API_SCAN=1, ale Sms:TestMode != true w appsettings.Development.json — ODMOWA (drenaż SMS). Ustaw TestMode=true lub wymuś ZAP_API_FORCE=1 na własną odpowiedzialność."
  fi
fi
echo

echo "===== PODSUMOWANIE ZAP ====="
echo "CRITICAL=$CRIT  HIGH=$HIGH  MEDIUM=$MED  LOW=$LOW"
echo "Raporty HTML/JSON: $OUT_DIR"
if [ "$CRIT" -gt 0 ] || [ "$HIGH" -gt 0 ]; then
  echo "WERDYKT: NO-GO (blokery: $((CRIT+HIGH)))"; exit 1
fi
echo "WERDYKT: brak blokerów na warstwie DAST"
exit 0
