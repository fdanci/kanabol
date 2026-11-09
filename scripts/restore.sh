#!/bin/sh
# Puts a backup of scripts/backup.sh back into the production stack: the database and the attachments
# volume both, as they were at that moment (docs/SPEC.md section 13.5, docs/backup.md). Everything saved
# since is lost, so it asks first, unless given --yes.
#
# Usage: scripts/restore.sh <backup folder> [--yes]
# Example: scripts/restore.sh backups/kanabol-2026-11-09
# The stack is the one docs/deploy.md starts, from compose.yaml and compose.prod.yaml. COMPOSE_FILE and
# COMPOSE_PROJECT_NAME, when set, pick another one, as scripts/check-release.sh does. It works on a new
# stack too, whose database is empty: it starts PostgreSQL first.
#
# It stops api and web, checks both files, unpacks the attachments next to the old ones, puts the database
# back in one transaction (pg_restore --clean --if-exists), and only then swaps the files and starts the
# stack. A step that fails before the swap leaves the data as it was and starts the stack again.

set -u
cd "$(dirname "$0")/.." || exit 2

command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
export MSYS_NO_PATHCONV=1

compose() {
  if [ -n "${COMPOSE_FILE:-}" ]; then
    docker compose "$@"
  else
    docker compose -f compose.yaml -f compose.prod.yaml "$@"
  fi
}

database=kanabol
attachments=/data/attachments
staging="$attachments/.restoring"
helper_image=alpine:3.21

usage() {
  echo "Usage: scripts/restore.sh <backup folder> [--yes]" >&2
  exit 2
}

folder=
yes=
for argument in "$@"; do
  case "$argument" in
    --yes) yes=1 ;;
    -*) usage ;;
    *) [ -z "$folder" ] && folder=${argument%/} || usage ;;
  esac
done
[ -n "$folder" ] || usage

if [ ! -f "$folder/db.dump" ] || [ ! -f "$folder/attachments.tar.gz" ]; then
  echo "$folder has no db.dump and attachments.tar.gz, which scripts/backup.sh writes." >&2
  exit 1
fi
if [ "$(head -c 5 "$folder/db.dump")" != PGDMP ]; then
  echo "$folder/db.dump isn't a pg_dump in custom format." >&2
  exit 1
fi

if [ -z "$yes" ]; then
  echo "This replaces the database and the attachments with the backup in $folder."
  echo "Everything saved since then is lost, and Kanabol is down until it ends."
  printf 'Type yes to go on: '
  answer=
  read -r answer || answer=
  if [ "$answer" != yes ]; then
    echo "Nothing changed."
    exit 1
  fi
fi

stopped=
restart() { # After a step that changed nothing: the stack goes back up as it was.
  [ -n "$stopped" ] && compose up -d >/dev/null 2>&1
}

fail() {
  echo "Restore failed: $1" >&2
  if [ -n "${api:-}" ]; then
    docker run --rm --volumes-from "$api" "$helper_image" rm -rf "$staging" >/dev/null 2>&1
  fi
  if [ -n "${2:-}" ]; then
    echo "The database is back as in $folder, but the attachments are only half swapped: run this again." >&2
    exit 1
  fi
  restart
  echo "The database and the attachments are as they were before." >&2
  exit 1
}

echo "Starting PostgreSQL, if it isn't running..."
compose up -d --wait postgres || fail "PostgreSQL didn't start"

echo "Checking the backup..."
compose exec -T -u postgres postgres pg_restore -l <"$folder/db.dump" >/dev/null ||
  fail "pg_restore can't read $folder/db.dump"
docker run --rm -i "$helper_image" tar tzf - <"$folder/attachments.tar.gz" >/dev/null ||
  fail "$folder/attachments.tar.gz isn't a tar.gz"

echo "Stopping api and web..."
compose stop api web || fail "api and web didn't stop"
stopped=1

# The api container holds the attachments volume. A new stack has none yet, so it's created, not started.
api=$(compose ps -a -q api | head -n 1)
if [ -z "$api" ]; then
  compose create api >/dev/null || fail "can't create the api container"
  api=$(compose ps -a -q api | head -n 1)
fi
[ -n "$api" ] || fail "there is no api container, whose volume holds the attachments"

echo "Unpacking the attachments..."
docker run --rm -i --volumes-from "$api" "$helper_image" \
  sh -c "rm -rf '$staging' && mkdir '$staging' && tar xzf - -C '$staging'" <"$folder/attachments.tar.gz" ||
  fail "unpacking the attachments failed"

echo "Putting the database back..."
compose exec -T -u postgres postgres pg_restore --clean --if-exists --single-transaction --exit-on-error \
  -U "$database" -d "$database" <"$folder/db.dump" || fail "pg_restore failed"

echo "Swapping the attachments..."
# Everything in the volume but the unpacked copy goes, then the copy takes its place, its owner included.
docker run --rm --volumes-from "$api" "$helper_image" sh -c "
  find '$attachments' -mindepth 1 -maxdepth 1 ! -name .restoring -exec rm -rf {} + &&
  find '$staging' -mindepth 1 -maxdepth 1 -exec mv {} '$attachments/' \; &&
  chown \$(stat -c %u:%g '$staging') '$attachments' && chmod \$(stat -c %a '$staging') '$attachments' &&
  rmdir '$staging'" || fail "swapping the attachments failed" half

echo "Starting the stack..."
compose up -d --wait --wait-timeout 180 || {
  echo "The data is back, but the stack didn't come up: see docker compose logs api." >&2
  exit 1
}
echo "Restored $folder."
