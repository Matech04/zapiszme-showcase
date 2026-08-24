#!/usr/bin/env bash
# Odświeża listę zakresów IP Cloudflare w `caddyfile` (blok między znacznikami CF-RANGES).
#
# PO CO: `trusted_proxies` decyduje, komu Caddy wierzy, gdy ten podaje IP klienta w nagłówku
# CF-Connecting-IP. Lista, która się zestarzeje, nie powoduje głośnej awarii — powoduje CICHĄ:
# ruch z nowego zakresu CF przestaje być zaufany, {client_ip} spada na adres edge'a Cloudflare
# i wszyscy ci użytkownicy lądują we wspólnym kubełku rate-limitera oraz wspólnym capie
# MaxConcurrentHoldsPerIp. Objaw: losowe 429 przy rezerwacji. Dlatego: cron raz w miesiącu.
#
# Użycie:
#   scripts/cf-ip-ranges.sh          # aktualizuje caddyfile w miejscu
#   scripts/cf-ip-ranges.sh --check  # nic nie zapisuje; exit 1 gdy lista jest nieaktualna (CI/cron)

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CADDYFILE="${REPO_ROOT}/caddyfile"
CHECK_ONLY=0
[ "${1:-}" = "--check" ] && CHECK_ONLY=1

command -v curl >/dev/null || { echo "::error::brak curl"; exit 1; }

echo "→ Pobieram zakresy Cloudflare"
V4="$(curl -fsS --max-time 20 https://www.cloudflare.com/ips-v4)"
V6="$(curl -fsS --max-time 20 https://www.cloudflare.com/ips-v6)"

# Sanity: pusta lub absurdalnie krótka odpowiedź (błąd sieci / strona logowania hotspotu)
# NIE MOŻE trafić do caddyfile — pusty trusted_proxies wyłącza zaufanie do CF-Connecting-IP.
RANGES="$(printf '%s\n%s\n' "$V4" "$V6" | grep -E '^[0-9a-fA-F:.]+/[0-9]+$' || true)"
COUNT="$(printf '%s\n' "$RANGES" | grep -c . || true)"
if [ "${COUNT:-0}" -lt 15 ]; then
  echo "::error::pobrano tylko ${COUNT} zakresów — to nie wygląda na prawdziwą listę CF. Przerywam."
  exit 1
fi
echo "  ${COUNT} zakresów"

# JEDNA długa linia, bo `caddy fmt` i tak skleja kontynuacje `\` w jedną — gdyby generator
# emitował je wieloliniowo, każdy przebieg widziałby rozjazd z sformatowanym plikiem i `--check`
# zgłaszałby fałszywy alarm. Emitujemy od razu postać kanoniczną, więc skrypt jest idempotentny.
BLOCK="$(
  printf '\t\t# BEGIN CF-RANGES (generowane — nie edytuj ręcznie)\n'
  printf '\t\ttrusted_proxies static %s\n' "$(printf '%s\n' "$RANGES" | paste -sd' ' -)"
  printf '\t\t# END CF-RANGES\n'
)"

TMP="$(mktemp)"
trap 'rm -f "$TMP"' EXIT
awk -v block="$BLOCK" '
  /# BEGIN CF-RANGES/ { print block; skip = 1; next }
  /# END CF-RANGES/   { skip = 0; next }
  !skip               { print }
' "$CADDYFILE" > "$TMP"

if cmp -s "$CADDYFILE" "$TMP"; then
  echo "✓ Lista aktualna — bez zmian"
  exit 0
fi

if [ "$CHECK_ONLY" = "1" ]; then
  echo "::error::zakresy CF w caddyfile są nieaktualne — odpal scripts/cf-ip-ranges.sh"
  diff -u "$CADDYFILE" "$TMP" || true
  exit 1
fi

cp "$TMP" "$CADDYFILE"
echo "✓ caddyfile zaktualizowany"

# Konfiguracja, która się nie waliduje, ubija Caddy'ego przy starcie (pełna niedostępność),
# więc sprawdzamy ją TU, a nie dopiero na produkcji.
if command -v docker >/dev/null; then
  echo "→ caddy fmt + validate"
  docker run --rm --security-opt label=disable -v "${REPO_ROOT}:/w" -w /w caddy:2-alpine \
    caddy fmt --overwrite caddyfile
  docker run --rm --security-opt label=disable -v "${CADDYFILE}:/etc/caddy/Caddyfile:ro" caddy:2-alpine \
    caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile 2>&1 | grep -E 'Valid|Error'
else
  echo "::warning::brak docker — pomijam caddy fmt/validate. Zwaliduj przed deployem!"
fi
