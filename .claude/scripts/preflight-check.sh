#!/usr/bin/env bash
# Deterministyczna warstwa bramki przed-deployowej (/preflight-security).
# Szybkie, powtarzalne, tanie sprawdzenia — bez LLM. Wynik w stałym formacie:
#   SEVERITY|id|message
# Severity: PASS | INFO | LOW | MEDIUM | HIGH | CRITICAL | SKIP
# Exit 1, jeśli wystąpi choć jedno HIGH lub CRITICAL (blokery deployu).
#
# Czyta to dyrygent (komenda /preflight-security) i łączy z wynikami subagentów.
# Uruchamiane też samodzielnie: .claude/scripts/preflight-check.sh
set -uo pipefail

# --- lokalizacja repo (skrypt leży w .claude/scripts/) ---
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

CRIT=0; HIGH=0; MED=0; LOW=0
SKIP_TESTS="${PREFLIGHT_SKIP_TESTS:-0}"   # PREFLIGHT_SKIP_TESTS=1 pomija dotnet test (szybki przebieg)

emit() { # emit SEVERITY id message
  local sev="$1" id="$2"; shift 2
  printf '%s|%s|%s\n' "$sev" "$id" "$*"
  case "$sev" in
    CRITICAL) CRIT=$((CRIT+1)) ;;
    HIGH)     HIGH=$((HIGH+1)) ;;
    MEDIUM)   MED=$((MED+1)) ;;
    LOW)      LOW=$((LOW+1)) ;;
  esac
}

has() { command -v "$1" >/dev/null 2>&1; }

echo "===== PREFLIGHT: warstwa deterministyczna ====="
echo "repo: $REPO_ROOT"
echo

# ---------------------------------------------------------------------------
# A. Build + testy (regresja logiki nadużyć żyje w testach)
# ---------------------------------------------------------------------------
echo "--- A. backend build + testy ---"
if ! has dotnet; then
  emit SKIP build "dotnet niedostępny — pomijam build/testy (zainstaluj .NET SDK)"
elif [ "$SKIP_TESTS" = "1" ]; then
  emit SKIP tests "PREFLIGHT_SKIP_TESTS=1 — pominięto dotnet test"
else
  if dotnet test --solution backend/backend.slnx >/tmp/preflight-dotnet-test.log 2>&1; then
    emit PASS tests "dotnet test --solution backend/backend.slnx — zielone"
  else
    emit CRITICAL tests "dotnet test NIE przeszło — szczegóły: /tmp/preflight-dotnet-test.log"
  fi
fi
echo

# ---------------------------------------------------------------------------
# B. Podatne zależności
# ---------------------------------------------------------------------------
echo "--- B. zależności (NuGet + npm) ---"
if has dotnet; then
  if dotnet list backend/backend.slnx package --vulnerable --include-transitive \
       >/tmp/preflight-nuget-vuln.log 2>&1; then
    if grep -qiE '\b(Critical|High)\b' /tmp/preflight-nuget-vuln.log; then
      emit HIGH deps-nuget "NuGet: podatność High/Critical — zob. /tmp/preflight-nuget-vuln.log"
    elif grep -qiE 'has the following vulnerable' /tmp/preflight-nuget-vuln.log; then
      emit MEDIUM deps-nuget "NuGet: podatności Low/Moderate — zob. /tmp/preflight-nuget-vuln.log"
    else
      emit PASS deps-nuget "NuGet: brak znanych podatności"
    fi
  else
    emit SKIP deps-nuget "dotnet list package --vulnerable nie wykonało się (offline?)"
  fi
else
  emit SKIP deps-nuget "dotnet niedostępny"
fi

audit_npm() { # audit_npm <dir>
  local dir="$1"
  [ -d "$dir/node_modules" ] || { emit SKIP "deps-npm-$dir" "$dir: brak node_modules (npm install)"; return; }
  local json; json="$(cd "$dir" && npm audit --json 2>/dev/null)"
  if [ -z "$json" ]; then emit SKIP "deps-npm-$dir" "$dir: npm audit bez wyniku"; return; fi
  if has python3; then
    local out; out="$(printf '%s' "$json" | python3 -c '
import sys, json
try:
    d = json.load(sys.stdin)
except Exception:
    print("ERR"); sys.exit()
v = d.get("metadata", {}).get("vulnerabilities", {})
print(v.get("critical", 0), v.get("high", 0), v.get("moderate", 0), v.get("low", 0))
')"
    if [ "$out" = "ERR" ] || [ -z "$out" ]; then emit SKIP "deps-npm-$dir" "$dir: nie sparsowano npm audit"; return; fi
    read -r c h m l <<<"$out"
    if [ "${c:-0}" -gt 0 ]; then emit CRITICAL "deps-npm-$dir" "$dir: $c critical, $h high (npm audit)"
    elif [ "${h:-0}" -gt 0 ]; then emit HIGH "deps-npm-$dir" "$dir: $h high (npm audit)"
    elif [ "${m:-0}" -gt 0 ]; then emit MEDIUM "deps-npm-$dir" "$dir: $m moderate (npm audit)"
    elif [ "${l:-0}" -gt 0 ]; then emit LOW "deps-npm-$dir" "$dir: $l low (npm audit)"
    else emit PASS "deps-npm-$dir" "$dir: brak podatności npm"; fi
  else
    emit SKIP "deps-npm-$dir" "$dir: python3 niedostępny — pomijam parsowanie npm audit"
  fi
}
if has npm; then audit_npm dashboard; audit_npm web; else emit SKIP deps-npm "npm niedostępny"; fi
echo

# ---------------------------------------------------------------------------
# C. Higiena sekretów (nic wrażliwego w appsettings/repo)
# ---------------------------------------------------------------------------
echo "--- C. sekrety w configu ---"
BASE_APPSETTINGS="backend/src/App.Api/appsettings.json"
DEV_APPSETTINGS="backend/src/App.Api/appsettings.Development.json"
secret_found=0
# OAuthToken SMS musi być pusty WSZĘDZIE (ładowany z env SMSAPI__OAUTH_TOKEN). Niepusty = wyciek/realny koszt.
for f in "$BASE_APPSETTINGS" "$DEV_APPSETTINGS"; do
  [ -f "$f" ] || continue
  if grep -nqE '"OAuthToken"[[:space:]]*:[[:space:]]*"[^"]+"' "$f"; then
    emit CRITICAL secret-sms "Sms:OAuthToken NIEPUSTY w $f — token SMS nie może być commitowany"
    secret_found=1
  fi
done
# Hasło w connection stringu — blokujące tylko w bazowym (deployowanym) appsettings.
if [ -f "$BASE_APPSETTINGS" ] && grep -niqE '(Password|Pwd)=[^";]+' "$BASE_APPSETTINGS"; then
  emit HIGH secret-connstr "Hasło w connection stringu w $BASE_APPSETTINGS (deployowany) — przenieś do env/secrets"
  secret_found=1
fi
# Development: lokalne hasło do kontenera jest oczekiwane → tylko informacyjnie (nie blokuje).
if [ -f "$DEV_APPSETTINGS" ] && grep -niqE '(Password|Pwd)=[^";]+' "$DEV_APPSETTINGS"; then
  emit LOW secret-connstr-dev "Hasło w connection stringu w $DEV_APPSETTINGS — OK jeśli to lokalny dev; nie używaj prod-credentiali"
fi
[ "$secret_found" = "0" ] && emit PASS secrets "appsettings bez zaszytych sekretów prod (OAuthToken pusty, brak hasła w bazowym)"
echo

# ---------------------------------------------------------------------------
# D. Strażnicy regresji kontrolek kosztu SMS (presence checks)
#    Jeśli ktoś usunie te zabezpieczenia, bramka to wychwyci.
# ---------------------------------------------------------------------------
echo "--- D. kontrolki kosztu SMS / rate-limit (regresja) ---"
OTP_GUARD="backend/src/App.Infrastructure/Booking/BookingOtpProtectionService.cs"
if [ -f "$OTP_GUARD" ]; then
  grep -q "MaxOtpPerPhonePerHour" "$OTP_GUARD" \
    && emit PASS cap-phone "cap per-telefon (MaxOtpPerPhonePerHour) obecny" \
    || emit HIGH cap-phone "USUNIĘTO cap per-telefon w BookingOtpProtectionService.cs"
  grep -q "MaxRequestOtpPerIpPerMinute" "$OTP_GUARD" \
    && emit PASS cap-ip "cap per-IP (MaxRequestOtpPerIpPerMinute) obecny" \
    || emit HIGH cap-ip "USUNIĘTO cap per-IP request-otp"
else
  emit HIGH otp-guard "Brak BookingOtpProtectionService.cs — zniknęła warstwa anty-abuse OTP"
fi

# Rate-limit policy w configu
grep -q '"PublicBookingWrite"' backend/src/App.Api/appsettings.json \
  && emit PASS rl-config "polityka PublicBookingWrite w appsettings.json" \
  || emit HIGH rl-config "Brak polityki PublicBookingWrite w appsettings.json"

# Polityka realnie nałożona na endpoint wysyłki OTP
OTP_CTRL="backend/src/App.Api/Controllers/Booking/PublicOtpController.cs"
if [ -f "$OTP_CTRL" ]; then
  grep -q 'EnableRateLimiting' "$OTP_CTRL" \
    && emit PASS rl-otp "PublicOtpController ma [EnableRateLimiting]" \
    || emit HIGH rl-otp "PublicOtpController BEZ [EnableRateLimiting] — request-otp bez limitu!"
else
  emit MEDIUM rl-otp "Nie znaleziono PublicOtpController.cs (zmiana ścieżki?)"
fi

# TestMode w bazowym appsettings (prod): true = SMS-y po cichu nie wychodzą
if grep -qE '"TestMode"[[:space:]]*:[[:space:]]*true' backend/src/App.Api/appsettings.json; then
  emit MEDIUM sms-testmode "Sms:TestMode=true w bazowym appsettings.json — w prod SMS nie będą wysyłane"
else
  emit PASS sms-testmode "Sms:TestMode=false w bazowym appsettings.json"
fi
echo

# ---------------------------------------------------------------------------
# E. Canary: migracje / sekrety frontu / operability
#    Tanie nudgi; głęboką analizę robią subagenci (migration-safety, secrets-config).
# ---------------------------------------------------------------------------
echo "--- E. canary: migracje / front / health ---"
MIG_DIR="backend/src/App.Infrastructure/Migrations"
if [ -d "$MIG_DIR" ]; then
  # Migracje nowe względem origin/main (fallback: najnowszy plik po nazwie).
  new_migs="$(git diff --diff-filter=AM --name-only origin/main...HEAD 2>/dev/null \
              | grep -E "$MIG_DIR/.*\.cs$" | grep -vE 'Designer|Snapshot')"
  if [ -z "$new_migs" ]; then
    new_migs="$(ls -t "$MIG_DIR"/*.cs 2>/dev/null | grep -vE 'Designer|Snapshot' | head -1)"
  fi
  if [ -n "$new_migs" ]; then
    mig_flag=0
    destructive_migs=""; cascade_migs=""
    # Skanujemy TYLKO blok Up() — DropColumn/Cascade w Down() to poprawny rollback, nie utrata danych.
    # (Pliki *.Designer.cs/Snapshot są już wykluczone z $new_migs powyżej — to model, nie operacje.)
    for mig in $new_migs; do
      [ -f "$mig" ] || continue
      up_block="$(awk '/void[[:space:]]+Up\(/{f=1} /void[[:space:]]+Down\(/{f=0} f' "$mig")"
      printf '%s' "$up_block" | grep -qE 'DropColumn|DropTable|DropForeignKey' \
        && destructive_migs="$destructive_migs $(basename "$mig")"
      printf '%s' "$up_block" | grep -qE 'ReferentialAction\.Cascade' \
        && cascade_migs="$cascade_migs $(basename "$mig")"
    done
    if [ -n "$destructive_migs" ]; then
      emit MEDIUM mig-destructive "DropColumn/DropTable/DropForeignKey w Up() — wymaga przeglądu (utrata danych?):$destructive_migs"
      mig_flag=1
    fi
    if [ -n "$cascade_migs" ]; then
      emit MEDIUM mig-cascade "DeleteBehavior.Cascade w Up() — sprawdź czy nie wymiata historii wizyt (preferuj Restrict):$cascade_migs"
      mig_flag=1
    fi
    [ "$mig_flag" -eq 0 ] && emit PASS mig "Brak destrukcyjnych operacji w Up() nowych migracji (canary)"
  else
    emit PASS mig "Brak nowych migracji do sprawdzenia"
  fi
else
  emit SKIP mig "Nie znaleziono katalogu migracji"
fi

# Sekrety w śledzonym configu frontu prod (klucze publiczne typu siteKey są OK).
FE_PROD_ENV="dashboard/src/environments/environment.production.ts"
if [ -f "$FE_PROD_ENV" ] && grep -niqE '(secret|password|private[_-]?key|client[_-]?secret|api[_-]?key|access[_-]?token|bearer)[^a-zA-Z]*[:=]' "$FE_PROD_ENV"; then
  emit HIGH fe-secret "Możliwy sekret w $FE_PROD_ENV — front jest publiczny, nie umieszczaj sekretów w buildzie"
else
  emit PASS fe-secret "Brak sekretów w produkcyjnym env dashboardu (tylko wartości publiczne)"
fi
# Pliki env web nie powinny być śledzone w gicie.
if git ls-files web/.env web/.env.production 2>/dev/null | grep -q .; then
  emit MEDIUM fe-env-tracked "web/.env* śledzony w gicie — env z sekretami nie powinien być commitowany"
else
  emit PASS fe-env-tracked "web/.env* nie jest śledzony w gicie"
fi

# Health endpoint (operability — czy w ogóle widać, że API żyje).
if grep -q 'MapHealthChecks' backend/src/App.Api/Program.cs 2>/dev/null; then
  emit PASS health "Health endpointy obecne (MapHealthChecks)"
else
  emit MEDIUM health "Brak MapHealthChecks — brak sygnału liveness/readiness dla prod"
fi
echo

# ---------------------------------------------------------------------------
# Podsumowanie + exit code
# ---------------------------------------------------------------------------
echo "===== PODSUMOWANIE (deterministyczne) ====="
echo "CRITICAL=$CRIT  HIGH=$HIGH  MEDIUM=$MED  LOW=$LOW"
if [ "$CRIT" -gt 0 ] || [ "$HIGH" -gt 0 ]; then
  echo "WERDYKT-DETERMINISTYCZNY: NO-GO (blokery: $((CRIT+HIGH)))"
  exit 1
fi
echo "WERDYKT-DETERMINISTYCZNY: brak blokerów na tej warstwie"
exit 0
