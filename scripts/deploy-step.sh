#!/usr/bin/env bash
# Pull obrazów, backup, REHEARSAL migracji na klonie prod-danych, migracja prod, force-recreate.
# Odpalane przez deploy.yml po wgraniu .env / compose / scripts na serwer.
# Można też odpalić ręcznie: `cd /opt/zapiszme && bash scripts/deploy-step.sh`
# (źródłem prawdy dla wersji obrazów jest IMAGE_TAG w .env).
set -euo pipefail
cd /opt/zapiszme

# Czytamy IMAGE_TAG z .env zamiast brać z env workflow — to czyni skrypt
# samowystarczalnym (działa po odpaleniu ręcznym po np. ssh na hot-fix).
IMAGE_TAG="$(grep '^IMAGE_TAG=' .env | cut -d= -f2)"
: "${IMAGE_TAG:?IMAGE_TAG missing from /opt/zapiszme/.env}"
API_IMAGE="ghcr.io/matech04/zapiszme-api:${IMAGE_TAG}"

# POPRZEDNI IMAGE_TAG odczytujemy z .env.previous (zapisanego przez deploy.yml
# przed nadpisaniem nowego .env). Używamy go jako nazwa pre-migrate backupu —
# tak żeby plik `<previous>.sql.gz` zawierał NAJŚWIEŻSZY stan bazy w momencie
# gdy poprzedni SHA był aktualny (= stan tuż przed naszą migracją).
#
# Rollback do <previous> z --with-db restore'uje ten plik = minimalna utrata
# świeżych danych (tylko od momentu uruchomienia obecnego deploya, nie od
# czasu kiedy <previous> był wgrywany).
PREVIOUS_IMAGE_TAG="$(grep '^IMAGE_TAG=' .env.previous 2>/dev/null | cut -d= -f2 || echo "initial")"

# NIE używamy `source .env` — special chars (spacja, `;`, `=`) w sekretach
# wywalają shell ("command not found"). Compose i `--env-file` parsują literalnie.

docker network inspect saas-network >/dev/null 2>&1 || docker network create saas-network
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml pull

# Migrate-step (docker run --rm poza compose) nie ma `depends_on:`, więc bez
# warmupu próbuje uderzyć do `db:5432` zanim postgres wystartuje.
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml up -d db
echo "Waiting for postgres to become healthy..."
for i in $(seq 1 60); do
  # `</dev/null` historycznie krytyczne gdy ten kod był w heredoc-SSH (compose exec zżerał reszte skryptu); w pliku zbędne ale zostawione defensywnie.
  if docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml exec -T db pg_isready -U admin -d saas_db </dev/null >/dev/null 2>&1; then
    echo "DB healthy after ${i}x2s"
    break
  fi
  sleep 2
  if [ "$i" = "60" ]; then
    echo "::error::Postgres did not become healthy in 120s"
    docker compose -f docker-compose.prod.yml logs db | tail -50
    exit 1
  fi
done

# Pre-migrate snapshot → backups/<previous>.sql.gz. Robione PRZED migracją żeby
# zawierał NAJŚWIEŻSZY stan bazy z czasów poprzedniego SHA. Rollback do
# <previous> z --with-db restore'uje dokładnie ten stan = traci się tylko
# zmiany zrobione od chwili rozpoczęcia obecnego deployu.
./scripts/backup-db.sh "${PREVIOUS_IMAGE_TAG}"

# --- REHEARSAL: próba migracji na KLONIE prod-danych PRZED migracją prod ---
# Migracje sprawdzamy najpierw na świeżej kopii RZECZYWISTYCH danych prod (nie na pustej bazie,
# jak CI/Testcontainers — tamto nie łapie dryfu/naruszeń ograniczeń na istniejących wierszach).
# Klon żyje w osobnej bazie tego samego kontenera `db`; dane lecą `pg_dump | psql` w locie, więc
# NIE schodzą na dysk (zero problemu z offline'owym kluczem szyfrującym backupy). Jeśli migracja
# padnie na realnym stanie danych — przerywamy deploy ZANIM dotkniemy prod-bazy: zero zmian, zero
# downtime. Klon kasowany zawsze (trap).
MIGRATION_TEST_DB="saas_db_migration_check"
cleanup_migration_test_db() {
  docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
    exec -T db psql -U admin -d postgres -c "DROP DATABASE IF EXISTS ${MIGRATION_TEST_DB} WITH (FORCE);" >/dev/null 2>&1 || true
}
trap cleanup_migration_test_db EXIT

echo "→ Rehearsal migracji na klonie prod-danych (${MIGRATION_TEST_DB})..."
cleanup_migration_test_db   # sprzątnij ewentualnego sierotę po poprzednim nieudanym biegu
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
  exec -T db psql -U admin -d postgres -c "CREATE DATABASE ${MIGRATION_TEST_DB} OWNER admin;" >/dev/null
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
  exec -T db pg_dump -U admin -d saas_db --no-owner --no-acl \
  | docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
      exec -T db psql -U admin -d "${MIGRATION_TEST_DB}" -v ON_ERROR_STOP=1 -q >/dev/null

# Connection string klona: bierzemy prod-owy z .env i podmieniamy TYLKO nazwę bazy (hasło zostaje
# nietknięte — bez rozbierania sekretu w shellu). Guard: jeśli podmiana nie zaszła, ABORT, żeby
# rehearsal NIGDY przypadkiem nie ruszył realnej bazy saas_db.
MIGRATION_TEST_CONN="$(grep '^ConnectionStrings__DefaultConnection=' .env | cut -d= -f2-)"
MIGRATION_TEST_CONN="${MIGRATION_TEST_CONN/Database=saas_db;/Database=${MIGRATION_TEST_DB};}"
case "${MIGRATION_TEST_CONN}" in
  *"Database=${MIGRATION_TEST_DB};"*) : ;;
  *) echo "::error::Nie udało się podmienić nazwy bazy w connection stringu — przerywam (nie ryzykuję migracji rehearsal na realnej bazie)."; exit 1 ;;
esac

if docker run --rm \
  --network saas-network \
  --env-file /opt/zapiszme/.env \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e RUN_MIGRATIONS_AND_EXIT=1 \
  -e ConnectionStrings__DefaultConnection="${MIGRATION_TEST_CONN}" \
  -e DataProtection__CertificatePath=/etc/dataprotection/cert.pfx \
  -v /opt/zapiszme/secrets/dataprotection.pfx:/etc/dataprotection/cert.pfx:ro \
  "${API_IMAGE}"; then
  echo "✓ Rehearsal OK — migracje przechodzą na rzeczywistym stanie danych prod."
else
  echo "::error::Migracje PADŁY na klonie prod-danych — przerywam deploy PRZED migracją prod (baza nietknięta, zero downtime). Zbadaj migrację, zanim wdrożysz."
  exit 1
fi
cleanup_migration_test_db
trap - EXIT

# Migracje + bootstrap admina. ASPNETCORE_ENVIRONMENT i RUN_MIGRATIONS_AND_EXIT
# nadpisujemy explicitly bo nie są w .env.
# DataProtection:CertificatePath + zamontowany PFX są wymagane bo ValidateProductionConfiguration
# (fail-fast H2) odpala się też w tym kroku — inaczej migracja crashuje na braku certu. Hasło
# (DataProtection__CertificatePassword) przychodzi z --env-file .env.
docker run --rm \
  --network saas-network \
  --env-file /opt/zapiszme/.env \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e RUN_MIGRATIONS_AND_EXIT=1 \
  -e DataProtection__CertificatePath=/etc/dataprotection/cert.pfx \
  -v /opt/zapiszme/secrets/dataprotection.pfx:/etc/dataprotection/cert.pfx:ro \
  "${API_IMAGE}"

# `--force-recreate` krytyczne: compose NIE wykrywa zmian w `env_file`, bez tej
# flagi stack zostaje na starym `.env` mimo że deploy.yml go zregenerował.
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml up -d --remove-orphans --force-recreate
docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml restart caddy-proxy

# Post-deploy smoke check — WEWNĘTRZNIE przez sieć docker (http://api:8080), NIE przez
# publiczny Caddy. /health/smoke dotyka bazy + sprawdza konfig, więc świadomie nie jest
# wystawiony na świat (404 w caddyfile). Weryfikujemy go tu, z wnętrza saas-network,
# jednorazowym kontenerem curl. Smoke = self + database + configuration.
echo "Post-deploy smoke check (internal http://api:8080/health/smoke)..."
SMOKE_CODE="000"
for i in $(seq 1 12); do
  # Podstawienie, NIE `|| echo 000` — przy odmowie połączenia curl sam wypisuje "000"
  # i kończy się kodem !=0, więc doklejone `echo` dałoby bezsensowne "000000" w komunikacie.
  SMOKE_CODE="$(docker run --rm --network saas-network curlimages/curl:latest \
      -s -o /dev/null -w '%{http_code}' http://api:8080/health/smoke 2>/dev/null)" || SMOKE_CODE="000"
  if [ "$SMOKE_CODE" = "200" ]; then
    echo "Smoke OK after ${i}x5s"
    break
  fi
  sleep 5
done
if [ "$SMOKE_CODE" != "200" ]; then
  echo "::error::Smoke check failed after 60s (last HTTP code: ${SMOKE_CODE})"
  docker compose -f docker-compose.prod.yml logs api | tail -50
  exit 1
fi

docker image prune -f
