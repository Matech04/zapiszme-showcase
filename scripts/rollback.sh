#!/usr/bin/env bash
# Rollback do poprzedniego SHA. Wywoływany przez .github/workflows/rollback.yml.
#
# Argumenty:
#   $1 = target SHA (musi istnieć jako tag w GHCR)
#   $2 = "true" gdy restore też bazę z `pre-<sha>.sql.gz`; default "false"
#
# Co robi:
#   1. Sprawdza że wszystkie 3 obrazy (api/dashboard/web) z tym SHA są w GHCR
#   2. Aktualizuje IMAGE_TAG w /opt/zapiszme/.env
#   3. (Opcjonalnie) restore bazy z backupu pre-<sha>.sql.gz
#   4. Pull + force-recreate kontenery z nowym tagiem
#   5. Smoke check przez /health/smoke — WEWNĘTRZNIE (http://api:8080 w sieci docker),
#      bo publicznie Caddy zwraca na tej ścieżce 404
#
# IMPORTANT: w trybie --with-db api zostaje zatrzymane na ~30-60s (downtime),
# a wszystkie zmiany w bazie OD MOMENTU BACKUPU SHA są tracone bezpowrotnie.
#
# SZYFROWANIE: pre-migrate backupy są szyfrowane `age` (`<sha>.sql.gz.age`), gdy włączony
# off-site backup. --with-db wymaga wtedy klucza PRYWATNEGO age (offline z założenia) —
# podaj go przez BACKUP_AGE_IDENTITY=/ścieżka lub wgraj do /opt/zapiszme/secrets/backup-identity.txt
# na czas rollbacku, a potem usuń. Legacy plain `<sha>.sql.gz` nadal działa bez klucza.

set -euo pipefail

SHA="${1:?usage: rollback.sh <target_sha> [with_db=true|false]}"
WITH_DB="${2:-false}"
GHCR_PREFIX="ghcr.io/matech04"
DEPLOY_DIR="/opt/zapiszme"
cd "${DEPLOY_DIR}"

COMPOSE="docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml"

# Recipient (publiczny) do szyfrowania snapshotu przed-rollbackowego — punktowo z .backup.env.
AGE_RECIPIENT=""
[ -f "${DEPLOY_DIR}/.backup.env" ] && \
  AGE_RECIPIENT="$(sed -n 's/^AGE_RECIPIENT=//p' "${DEPLOY_DIR}/.backup.env" | head -1)"
# Identity (PRYWATNY klucz) do odszyfrowania backupu przy --with-db. Domyślnie OFFLINE (nie ma
# go na serwerze) — operator musi go podać: BACKUP_AGE_IDENTITY=/ścieżka albo wgrać do
# /opt/zapiszme/secrets/backup-identity.txt na czas rollbacku, a potem usunąć.
AGE_IDENTITY="${BACKUP_AGE_IDENTITY:-${DEPLOY_DIR}/secrets/backup-identity.txt}"

echo "=========================================="
echo " ROLLBACK"
echo " target  = ${SHA}"
echo " with_db = ${WITH_DB}"
echo "=========================================="

# --- 1) Sanity: obrazy istnieją w GHCR ---
for IMG in zapiszme-api zapiszme-dashboard zapiszme-web; do
  FULL="${GHCR_PREFIX}/${IMG}:${SHA}"
  if ! docker manifest inspect "${FULL}" >/dev/null 2>&1; then
    echo "::error::Image ${FULL} not found in GHCR — aborting rollback"
    exit 1
  fi
  echo "✓ ${FULL}"
done

# --- 2) Backup AKTUALNEGO stanu przed rollbackiem ---
# To safety net: jak rollback się nie powiedzie, mamy snapshot do którego można wrócić.
CURRENT_SHA=$(grep -E '^IMAGE_TAG=' .env | cut -d= -f2 || echo "unknown")
echo "→ Snapshot bazy przed rollbackiem (current SHA: ${CURRENT_SHA})"
mkdir -p backups
if [ -n "${AGE_RECIPIENT}" ] && command -v age >/dev/null; then
  SNAP="backups/rollback-from-${CURRENT_SHA}-$(date +%s).sql.gz.age"
  $COMPOSE exec -T db pg_dump -U admin -d saas_db --no-owner --no-acl \
    | gzip | age -r "${AGE_RECIPIENT}" > "${SNAP}"
else
  SNAP="backups/rollback-from-${CURRENT_SHA}-$(date +%s).sql.gz"
  echo "::warning::brak AGE_RECIPIENT/age — snapshot przed-rollbackowy NIE jest szyfrowany at-rest (PII w cleartext)."
  $COMPOSE exec -T db pg_dump -U admin -d saas_db --no-owner --no-acl \
    | gzip > "${SNAP}"
fi
echo "✓ Snapshot: ${SNAP}"

# --- 3) Update IMAGE_TAG w .env ---
echo "→ Setting IMAGE_TAG=${SHA} in .env"
sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=${SHA}/" .env

# --- 4) Opcjonalny restore bazy ---
if [ "${WITH_DB}" = "true" ]; then
  # Preferuj zaszyfrowany backup (.sql.gz.age), legacy plain (.sql.gz) jako fallback.
  ENC="backups/${SHA}.sql.gz.age"
  PLAIN="backups/${SHA}.sql.gz"
  if [ -f "${ENC}" ]; then
    BACKUP="${ENC}"; ENCRYPTED=1
  elif [ -f "${PLAIN}" ]; then
    BACKUP="${PLAIN}"; ENCRYPTED=0
  else
    echo "::error::Brak backupu ${ENC} ani ${PLAIN} — nie mogę przywrócić bazy"
    echo "Dostępne backupy:"
    ls -1t backups/*.sql.gz backups/*.sql.gz.age 2>/dev/null | head -20
    exit 1
  fi

  # Walidacja klucza PRZED zatrzymaniem api — żeby nie brać downtime, jeśli i tak nie odszyfrujemy.
  if [ "${ENCRYPTED}" = "1" ]; then
    command -v age >/dev/null || { echo "::error::backup zaszyfrowany, brak 'age' na hoście"; exit 1; }
    if [ ! -f "${AGE_IDENTITY}" ]; then
      echo "::error::Backup ${BACKUP} jest zaszyfrowany, a brak klucza prywatnego (${AGE_IDENTITY})."
      echo "         Klucz age jest OFFLINE z założenia. Aby zrobić rollback --with-db: wgraj go tymczasowo"
      echo "         (BACKUP_AGE_IDENTITY=/ścieżka albo ${DEPLOY_DIR}/secrets/backup-identity.txt) i ponów."
      echo "         Czysty rollback kodu (bez --with-db) NIE wymaga klucza."
      exit 1
    fi
  fi

  echo "→ Stopping api/dashboards/web (db zostawiamy żeby przyjąć restore)"
  $COMPOSE stop api admin-panel booking-app

  echo "→ Wiping schema public..."
  $COMPOSE exec -T db psql -U admin -d saas_db <<'SQL'
DROP SCHEMA public CASCADE;
CREATE SCHEMA public;
GRANT ALL ON SCHEMA public TO admin;
GRANT ALL ON SCHEMA public TO public;
SQL

  echo "→ Restoring from ${BACKUP}..."
  if [ "${ENCRYPTED}" = "1" ]; then
    age -d -i "${AGE_IDENTITY}" "${BACKUP}" | gunzip \
      | $COMPOSE exec -T db psql -U admin -d saas_db --quiet
  else
    gunzip -c "${BACKUP}" | $COMPOSE exec -T db psql -U admin -d saas_db --quiet
  fi
  echo "✓ DB restored from ${BACKUP}"
fi

# --- 5) Pull starych obrazów + force-recreate ---
echo "→ Pulling images at :${SHA}"
$COMPOSE pull api admin-panel booking-app

echo "→ Recreating containers"
$COMPOSE up -d --force-recreate api admin-panel booking-app
$COMPOSE restart caddy-proxy

# --- 6) Smoke ---
# WEWNĘTRZNIE przez sieć docker (http://api:8080), NIE przez publiczny Caddy — /health/smoke
# dotyka bazy + sprawdza konfig, więc świadomie zwraca 404 z internetu (caddyfile). Kod HTTP
# sprawdzamy wprost, bo `curl -sf` zlewa 404 i brak odpowiedzi w jeden nieodróżnialny błąd.
echo "→ Waiting for internal http://api:8080/health/smoke (up to 60s)"
SMOKE_CODE="000"
for i in $(seq 1 12); do
  # Podstawienie, NIE `|| echo 000` — przy odmowie połączenia curl sam wypisuje "000"
  # i kończy się kodem !=0, więc doklejone `echo` dałoby bezsensowne "000000" w komunikacie.
  SMOKE_CODE="$(docker run --rm --network saas-network curlimages/curl:latest \
      -s -o /dev/null -w '%{http_code}' http://api:8080/health/smoke 2>/dev/null)" || SMOKE_CODE="000"
  if [ "${SMOKE_CODE}" = "200" ]; then
    echo "✓ Smoke OK after ${i}x5s"
    docker run --rm --network saas-network curlimages/curl:latest \
      -s http://api:8080/health/smoke 2>/dev/null || true
    echo
    echo "✓✓ Rollback to ${SHA} complete"
    exit 0
  fi
  sleep 5
done

echo "::error::Smoke check failed after rollback (last HTTP code: ${SMOKE_CODE}) — INVESTIGATE MANUALLY"
$COMPOSE logs --tail=80 api
exit 1
