#!/bin/sh
# Backs up the production stack: the database as a pg_dump in custom format, and the attachments volume as a
# tar, in backups/kanabol-YYYY-MM-DD/ (docs/SPEC.md section 13.5). Keeps 14 days, and writes one line per run
# in backups/backup.log. It runs on the server from the clone's root, from cron at 02:00: see docs/backup.md.
#
# Usage: scripts/backup.sh
# The stack is the one docs/deploy.md starts, from compose.yaml and compose.prod.yaml. COMPOSE_FILE and
# COMPOSE_PROJECT_NAME, when set, pick another one, as scripts/check-release.sh does.
#
# No password goes through here: pg_dump runs as the postgres user inside the container, whose local
# connections PostgreSQL trusts, so the command line and the log never hold one.

set -u
cd "$(dirname "$0")/.." || exit 2

# Shells opened before Docker Desktop was installed don't have it on the PATH (Windows only).
command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
# Git Bash would otherwise turn /data/attachments into a Windows path.
export MSYS_NO_PATHCONV=1

compose() {
  if [ -n "${COMPOSE_FILE:-}" ]; then
    docker compose "$@"
  else
    docker compose -f compose.yaml -f compose.prod.yaml "$@"
  fi
}

# compose.yaml's database and user, and the folder the api service keeps its files in.
database=kanabol
attachments=/data/attachments
helper_image=alpine:3.21
keep_days=14

today=$(date +%Y-%m-%d)
folder="backups/kanabol-$today"
partial="backups/.kanabol-$today.partial"
log=backups/backup.log
errors="backups/.backup-errors"

mkdir -p backups || exit 1

stamp() {
  date '+%Y-%m-%d %H:%M:%S'
}

fail() { # fail <what went wrong>: the log gets the reason, and the backups already there stay as they were
  reason=$1
  last=$(tail -n 1 "$errors" 2>/dev/null | tr -d '\r')
  [ -n "$last" ] && reason="$reason: $last"
  printf '%s FAIL %s\n' "$(stamp)" "$reason" >>"$log"
  echo "Backup failed: $reason" >&2
  rm -rf "$partial" "$errors"
  exit 1
}

bytes() {
  wc -c <"$1" | tr -d ' '
}

: >"$errors"
[ -n "$(compose ps --status running -q postgres 2>>"$errors")" ] || fail "postgres isn't running"
api=$(compose ps -a -q api 2>>"$errors" | head -n 1)
[ -n "$api" ] || fail "there is no api container, whose volume holds the attachments"

rm -rf "$partial"
mkdir -p "$partial" || fail "can't write $partial"

echo "Dumping the database..."
compose exec -T -u postgres postgres pg_dump -Fc -U "$database" -d "$database" \
  >"$partial/db.dump" 2>"$errors" || fail "pg_dump failed"
[ "$(head -c 5 "$partial/db.dump")" = PGDMP ] || fail "pg_dump wrote no custom-format dump"

echo "Packing the attachments..."
docker run --rm --volumes-from "$api" "$helper_image" tar czf - -C "$attachments" . \
  >"$partial/attachments.tar.gz" 2>"$errors" || fail "packing the attachments failed"

# Today's backup is replaced only by a whole one: a run that fails above leaves it as it was.
rm -rf "$folder" && mv "$partial" "$folder" || fail "can't move the backup to $folder"

# Folders named after a day more than 14 days back go. Anything else in backups/ stays.
cutoff=$(date -d "@$(($(date +%s) - keep_days * 86400))" +%Y%m%d)
removed=0
for old in backups/kanabol-*; do
  day=${old#backups/kanabol-}
  case "$day" in
    [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]) ;;
    *) continue ;;
  esac
  [ -d "$old" ] || continue
  if [ "$(printf '%s' "$day" | tr -d -)" -lt "$cutoff" ]; then
    rm -rf "$old" && removed=$((removed + 1))
  fi
done

rm -f "$errors"
line="ok $folder (db.dump $(bytes "$folder/db.dump") bytes, attachments.tar.gz $(bytes "$folder/attachments.tar.gz") bytes; removed $removed older than $keep_days days)"
printf '%s %s\n' "$(stamp)" "$line" >>"$log"
echo "Backed up to $folder, and removed $removed older than $keep_days days."
