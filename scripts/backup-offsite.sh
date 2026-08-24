#!/usr/bin/env bash
# OFF-SITE backup bazy → Cloudflare R2 — niezależny od deploya (uruchamiany z crona).
#
# Czym różni się od backup-db.sh:
#   • backup-db.sh   → snapshot LOKALNY (ten sam dysk), tylko PRZED migracją przy deployu.
#   • backup-offsite → kopia POZA serwerem (R2), często, szyfrowana. Przeżywa śmierć boxa.
#
# Przepływ: pg_dump | gzip | age (szyfrowanie — baza ma PII klientów, RODO) | rclone → R2.
# Heartbeat (dead-man's-switch): po SUKCESIE pinguje URL; brak pinga → alert.
# Bez tego cichy fail = myślisz, że masz backup, a nie masz.
#
# DWA POZIOMY (tier — argument $1; brak = flat, wstecznie):
#   • frequent → ${RCLONE_REMOTE}/frequent/ , retencja OFFSITE_KEEP_FREQUENT_DAYS (def. 3)
#                — cron co 15 min; chroni przed "ups, skasowane 10 min temu" (małe RPO).
#   • daily    → ${RCLONE_REMOTE}/daily/    , retencja OFFSITE_KEEP_DAILY_DAYS   (def. 30)
#                — cron raz na dobę; długie okno na "serwer umarł, potrzebuję sprzed 3 tyg.".
#
# Wymaga /opt/zapiszme/.backup.env (chmod 600) — generowany przez deploy.yml z GH Secrets.
# rclone celuje w R2 przez zmienne RCLONE_CONFIG_R2_* (remote zdefiniowany env-em, BEZ
# `rclone config` na hoście; klucze niewidoczne w `ps`). Przykładowa zawartość:
#   RCLONE_CONFIG_R2_TYPE=s3
#   RCLONE_CONFIG_R2_PROVIDER=Cloudflare
#   RCLONE_CONFIG_R2_ACCESS_KEY_ID=...          # token R2 scoped do bucketa backupów
#   RCLONE_CONFIG_R2_SECRET_ACCESS_KEY=...
#   RCLONE_CONFIG_R2_ENDPOINT=https://<account-id>.eu.r2.cloudflarestorage.com  # bucket w jurysdykcji EU (RODO)
#   RCLONE_CONFIG_R2_ACL=private
#   RCLONE_REMOTE=r2:zapiszme-backups/db        # remote (r2:) + bucket/ścieżka bazowa
#   AGE_RECIPIENT=age1...                        # PUBLICZNY klucz age (szyfrujemy do niego)
#   OFFSITE_KEEP_FREQUENT_DAYS=3                 # retencja tier frequent
#   OFFSITE_KEEP_DAILY_DAYS=30                   # retencja tier daily
#   HEARTBEAT_URL=https://hc-ping.com/<uuid>     # opcjonalnie (healthchecks.io / UptimeRobot)
#
# RESTORE (na czystej maszynie z age-identity + rclone, .backup.env w środowisku):
#   rclone copy r2:zapiszme-backups/db/daily/<plik>.sql.gz.age .
#   age -d -i backup-key.txt <plik>.sql.gz.age | gunzip | \
#     docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
#       exec -T db psql -U admin -d saas_db
set -euo pipefail
# Cron ma minimalny PATH (/usr/bin:/bin) i nie widzi /usr/local/bin, gdzie domyślnie
# ląduje rclone — bez tego `command -v rclone` padnie tylko spod crona, nie z shella.
export PATH="/usr/local/bin:/usr/bin:/bin:${PATH:-}"
cd /opt/zapiszme

# Mutex: cron odpala frequent co 15 min + daily o 03:30 na TYM SAMYM boxie co API/Postgres.
# Pojedynczy lock (współdzielony przez oba tiery) gwarantuje, że nigdy nie biegną dwa pg_dumpy
# naraz — gdy poprzedni cykl trwa dłużej niż interwał (rosnąca baza), kolejny po prostu pomija.
# fd 9 trzymany otwarty do końca skryptu = lock żyje przez cały backup. flock z util-linux.
if command -v flock >/dev/null; then
  exec 9>/tmp/zapiszme-backup-offsite.lock
  flock -n 9 || { echo "⏭ poprzedni off-site backup wciąż trwa — pomijam ten cykl"; exit 0; }
fi

# Obniż priorytet CPU/IO całego skryptu (gzip|age|rclone na hoście dziedziczą), żeby spike
# co 15 min nie konkurował z API o rdzenie. pg_dump biegnie w kontenerze db (osobny cgroup),
# więc go to nie dotyczy — ale to host-owe gzip/age/rclone są tu realnym obciążeniem.
command -v ionice >/dev/null && ionice -c2 -n7 -p $$ >/dev/null 2>&1 || true
renice -n 10 -p $$ >/dev/null 2>&1 || true

CONF="/opt/zapiszme/.backup.env"
[ -f "$CONF" ] || { echo "::error::brak ${CONF} — skonfiguruj off-site backup (patrz nagłówek skryptu)"; exit 1; }
# shellcheck disable=SC1090
set -a; . "$CONF"; set +a
: "${AGE_RECIPIENT:?brak AGE_RECIPIENT w .backup.env}"
: "${RCLONE_REMOTE:?brak RCLONE_REMOTE w .backup.env}"

# Tier (argument $1): wybiera podkatalog na remote + retencję. Brak argumentu = tryb flat
# (wstecznie kompatybilny: cała ścieżka RCLONE_REMOTE, retencja OFFSITE_KEEP_DAYS).
TIER="${1:-flat}"
case "$TIER" in
  frequent) REMOTE_PATH="${RCLONE_REMOTE%/}/frequent"; KEEP="${OFFSITE_KEEP_FREQUENT_DAYS:-3}" ;;
  daily)    REMOTE_PATH="${RCLONE_REMOTE%/}/daily";    KEEP="${OFFSITE_KEEP_DAILY_DAYS:-30}" ;;
  flat)     REMOTE_PATH="${RCLONE_REMOTE%/}";          KEEP="${OFFSITE_KEEP_DAYS:-30}" ;;
  *) echo "::error::nieznany tier '${TIER}' (dozwolone: frequent | daily | brak)"; exit 1 ;;
esac

command -v age   >/dev/null || { echo "::error::brak 'age' na hoście (apt install age)"; exit 1; }
command -v rclone >/dev/null || { echo "::error::brak 'rclone' na hoście (https://rclone.org/install)"; exit 1; }

STAMP="$(date -u +%Y-%m-%dT%H%M%SZ)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
OUT="${TMP}/saas_db-${STAMP}.sql.gz.age"

echo "→ pg_dump | gzip | age → ${OUT##*/}"
# set -o pipefail: jeśli pg_dump padnie w środku pipe'a, cały pipeline zwraca błąd i
# skrypt wychodzi PRZED uploadem/heartbeatem — nie wgramy ani nie zaraportujemy pustego backupu.
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
  exec -T db pg_dump -U admin -d saas_db --no-owner --no-acl \
  | gzip \
  | age -r "$AGE_RECIPIENT" > "$OUT"

# Sanity guard: zaszyfrowany dump zdrowej bazy to dziesiątki+ KB. <1KB = coś poszło nie tak.
SIZE=$(stat -c%s "$OUT")
if [ "$SIZE" -lt 1024 ]; then
  echo "::error::backup podejrzanie mały (${SIZE}B) — przerywam, NIE pinguję heartbeat"
  exit 1
fi
echo "✓ zaszyfrowany dump = $(du -h "$OUT" | cut -f1)"

echo "→ upload [${TIER}] do ${REMOTE_PATH}/"
# --s3-no-check-bucket: token R2 jest scoped do obiektów w buckecie (least-privilege) i NIE ma
# uprawnienia CreateBucket. Bez tej flagi rclone próbuje sprawdzić/utworzyć bucket przed uploadem
# → R2 zwraca 403 AccessDenied i backup się nie wgrywa. Bucket tworzymy raz, ręcznie, w panelu R2.
rclone copy "$OUT" "${REMOTE_PATH}/" --no-traverse --s3-no-check-bucket

echo "→ retencja [${TIER}]: kasuję starsze niż ${KEEP}d"
rclone delete "${REMOTE_PATH}/" --min-age "${KEEP}d" --s3-no-check-bucket || true

if [ -n "${HEARTBEAT_URL:-}" ]; then
  if curl -fsS -m 10 "$HEARTBEAT_URL" >/dev/null 2>&1; then
    echo "✓ heartbeat ping wysłany"
  else
    echo "⚠ heartbeat ping nieudany (backup OK, ale monitoring nie dostał sygnału)"
  fi
fi

echo "✓ off-site backup OK [${TIER}] (${STAMP})"
