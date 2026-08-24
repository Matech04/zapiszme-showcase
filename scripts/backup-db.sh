#!/usr/bin/env bash
# Pre-migrate database snapshot, wywoływany z deploy.yml PRZED migrate-stepem.
#
# Argument: SHA aktualnie zdeployowanego kodu (czyli POPRZEDNI SHA, ten który zaraz
# zostanie zastąpiony przez nowy). Plik wynikowy: `/opt/zapiszme/backups/<sha>.sql.gz`.
#
# OFF-SITE: jeśli skonfigurowany backup R2 (.backup.env), zaszyfrowany snapshot jest dodatkowo
# wgrywany do `${RCLONE_REMOTE}/predeploy/` (retencja OFFSITE_KEEP_PREDEPLOY_DAYS, def. 30 dni) —
# off-site stan bazy DOKŁADNIE sprzed każdej migracji. Lokalny snapshot ginie z boxem; ten przeżywa.
#
# Sematyka: `<sha>.sql.gz` zawiera NAJŚWIEŻSZY stan bazy w momencie kiedy ten SHA był
# aktualnie aktywny — czyli stan tuż przed tym jak go zastąpiliśmy. Rollback do tego
# SHA z `--with-db=true` minimalizuje utratę świeżych danych: tracimy tylko delty
# zrobione w czasie BIEŻĄCEGO deployu (sekundy-godziny), nie cały okres aktywności
# tego SHA (godziny-dni).
#
# Założenia (sprawdzane przez deploy.yml przed wywołaniem):
#   • cwd = /opt/zapiszme
#   • kontener db jest healthy (`pg_isready`)
#   • migrate-step NIE został jeszcze uruchomiony (czyli baza ma stary schema)
#   • backups/ jest zapisywalne dla deploy usera (chmod 700)
#
# Rotacja: trzymamy 15 ostatnich snapshotów (~50MB każdy na bazie ~10-20 tenantów),
# starsze są kasowane. 15 = sensowny bufor żeby rollback dał się zrobić nawet po
# kilku zlinkowanych deployach.
#
# SZYFROWANIE at-rest (RODO art. 32): jeśli skonfigurowany off-site backup (.backup.env z
# AGE_RECIPIENT) i `age` jest dostępne, dump jest szyfrowany → `<sha>.sql.gz.age`. Klucz
# PRYWATNY jest OFFLINE (jak przy off-site), więc `rollback.sh --with-db` wymaga podania go
# operatorowi. Bez AGE_RECIPIENT (backup off-site niewłączony) fallback na plain gzip, żeby
# deploy nigdy nie padł — z głośnym ostrzeżeniem (PII w cleartext na dysku).

set -euo pipefail

SHA="${1:?usage: backup-db.sh <sha>}"
BACKUP_DIR="/opt/zapiszme/backups"
KEEP_LAST=15

mkdir -p "${BACKUP_DIR}"
chmod 700 "${BACKUP_DIR}"

# Recipient czytamy punktowo z .backup.env (bez `source`, żeby nie wciągać RCLONE_* itp.).
AGE_RECIPIENT=""
[ -f /opt/zapiszme/.backup.env ] && \
  AGE_RECIPIENT="$(sed -n 's/^AGE_RECIPIENT=//p' /opt/zapiszme/.backup.env | head -1)"

# Plain SQL (gzippowany) zamiast pg_dump -Fc — łatwiejszy do `zcat | grep` przy
# debug i niezależny od wersji pg_restore. --no-owner / --no-acl pomija ROLE/USER
# ownership, czystszy restore.
dump_db() {
  docker compose -f docker-compose.prod.yml -f docker-compose.monitoring.yml \
    exec -T db pg_dump -U admin -d saas_db --no-owner --no-acl
}

if [ -n "${AGE_RECIPIENT}" ] && command -v age >/dev/null; then
  OUT="${BACKUP_DIR}/${SHA}.sql.gz.age"
  echo "→ Backing up DB (encrypted) to ${OUT}"
  # set -o pipefail jest aktywne (set -e -o pipefail) → padnięty pg_dump zatrzyma pipeline.
  dump_db | gzip | age -r "${AGE_RECIPIENT}" > "${OUT}"
else
  OUT="${BACKUP_DIR}/${SHA}.sql.gz"
  echo "::warning::brak AGE_RECIPIENT/age — lokalny pre-migrate dump NIE jest szyfrowany at-rest (PII w cleartext na dysku VPS). Włącz off-site backup (.backup.env), by szyfrować."
  echo "→ Backing up DB to ${OUT}"
  dump_db | gzip > "${OUT}"
fi

SIZE=$(du -h "${OUT}" | cut -f1)
echo "✓ Backup ${SHA} = ${SIZE}"

# --- OFF-SITE: kopia pre-migrate snapshotu → R2 (DR poza serwerem) ---
# Lokalny snapshot leży na TYM SAMYM dysku co baza — ginie razem z boxem. Dodatkowo
# wypychamy go do R2 (osobny podkatalog predeploy/, obok frequent/ i daily/ z backup-offsite.sh),
# żeby mieć off-site stan bazy DOKŁADNIE sprzed każdej migracji — najmocniejszy punkt rollbacku
# przy nieudanym/destrukcyjnym deployu, nawet gdy serwer przestanie istnieć.
#
# Twarde zasady:
#   • TYLKO zaszyfrowany (.age) — cleartext PII NIGDY nie opuszcza serwera (RODO art. 32).
#   • Brak .backup.env / rclone / RCLONE_REMOTE = cichy skip (R2 niewłączony) — to opcjonalne.
#   • Krok NIGDY nie wywala deployu: całość pod `|| ::warning::` (lokalny snapshot już jest,
#     a wgrywanie kodu nie może paść tylko dlatego, że R2 chwilowo niedostępne).
upload_offsite_predeploy() {
  case "${OUT}" in
    *.age) ;;
    *) echo "::warning::pre-migrate snapshot nieszyfrowany — pomijam upload off-site (nie wysyłam PII cleartext do R2)"; return 0 ;;
  esac
  [ -f /opt/zapiszme/.backup.env ] || { echo "ℹ brak .backup.env — pomijam off-site pre-migrate (R2 niewłączony)"; return 0; }
  command -v rclone >/dev/null || { echo "::warning::brak rclone na hoście — pomijam off-site pre-migrate snapshot"; return 0; }
  # .backup.env sourcujemy TYLKO w tym subshellu, żeby RCLONE_CONFIG_R2_* trafiły do rclone,
  # a reszta backup-db.sh pozostała czysta (bez wciągania RCLONE_*/AGE_* do globalnego env).
  (
    set -a; . /opt/zapiszme/.backup.env; set +a
    : "${RCLONE_REMOTE:?brak RCLONE_REMOTE w .backup.env}"
    # Cron ma minimalny PATH; rclone domyślnie ląduje w /usr/local/bin — prepend jak w backup-offsite.sh.
    export PATH="/usr/local/bin:/usr/bin:/bin:${PATH:-}"
    REMOTE_PATH="${RCLONE_REMOTE%/}/predeploy"
    KEEP="${OFFSITE_KEEP_PREDEPLOY_DAYS:-30}"
    echo "→ off-site pre-migrate → ${REMOTE_PATH}/"
    # --s3-no-check-bucket: token R2 jest scoped do obiektów (bez CreateBucket) — bez tej flagi 403.
    rclone copy "${OUT}" "${REMOTE_PATH}/" --no-traverse --s3-no-check-bucket
    rclone delete "${REMOTE_PATH}/" --min-age "${KEEP}d" --s3-no-check-bucket || true
    echo "✓ off-site pre-migrate OK (${OUT##*/})"
  )
}
upload_offsite_predeploy || echo "::warning::off-site pre-migrate snapshot nieudany — lokalny snapshot OK, deploy kontynuowany"

# Zbiera istniejące snapshoty (oba warianty: .sql.gz i .sql.gz.age).
#
# `nullglob` jest kluczowe: przy `set -euo pipefail` niedopasowany glob trafiał do `ls` DOSŁOWNIE,
# `ls` kończył się kodem 2, a `pipefail` przenosił go na cały potok → deploy padał. Dokładnie to
# się stało, gdy rotacja skasowała ostatni stary plik bez `.age` i zostały same zaszyfrowane.
list_backups() {
  shopt -s nullglob
  local files=("${BACKUP_DIR}"/*.sql.gz "${BACKUP_DIR}"/*.sql.gz.age)
  shopt -u nullglob
  ((${#files[@]})) && printf '%s\n' "${files[@]}"
  return 0
}

# Rotacja: lista po mtime DESC, kasuj od (KEEP_LAST+1).
echo "→ Rotating backups (keep last ${KEEP_LAST})"
mapfile -t all_backups < <(list_backups)
if ((${#all_backups[@]} > KEEP_LAST)); then
  ls -1t "${all_backups[@]}" | tail -n +$((KEEP_LAST + 1)) | xargs -r rm -v
fi

echo "→ Current backups:"
mapfile -t kept_backups < <(list_backups)
if ((${#kept_backups[@]})); then
  # `|| true` — `head` zamyka potok, więc `ls` może dostać SIGPIPE (141), co przy `pipefail`
  # znów wywróciłoby deploy. Ta linia jest ostatnia w skrypcie: jej kod = kod skryptu.
  ls -lht "${kept_backups[@]}" | head || true
else
  echo "(brak snapshotów)"
fi
