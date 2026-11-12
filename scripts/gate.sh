#!/bin/sh
# The full gate (docs/SPEC.md §14.7): every scripts/check-*.sh on a fresh clone, after scripts/setup.sh, with
# the work they share done once by scripts/gate-prepare.sh and reused through KANABOL_GATE_CACHE.
#
# Usage:
#   scripts/gate.sh --fresh <branch> <clone folder> <log folder>
#       clones <branch> of this repo into <clone folder> (which must not exist), runs scripts/setup.sh there,
#       then the clone's own scripts/gate.sh on every script
#   scripts/gate.sh <log folder> [script...]
#       in this checkout, which has to be clean: the preparation, then the scripts named (check-tests, or
#       check-docker:1,2,3 for some of a script's tasks), or every one: each check-*.sh but check-docker, then
#       check-docker 1 2 3 4 6, then check-docker 5 with the local PostgreSQL stopped, since it needs port 5432
#
# <log folder> gets a log per script, the preparation's in cache/, and summary.txt: ok or FAIL for each, its
# seconds, how many answers came from the cache, and the total minutes. The exit code is 1 if any failed.
#
# Environment:
#   KANABOL_GATE_CACHE    a cache scripts/gate-prepare.sh already made for this tree (CI does it in a job of its
#                         own): no preparation here
#   KANABOL_GATE_NO_REUSE=1  the scripts run as they do on their own, without the cache: the old gate
#   KANABOL_PGBIN, KANABOL_PGDATA  a local PostgreSQL the gate may start and stop with pg_ctl; on Windows,
#                         without them, the portable one in %LOCALAPPDATA%\Kanabol (pgsql\bin, pgdata) if it's there
#
# Needs what the scripts need (docs/SPEC.md §14.7): the packages scripts/setup.sh installs, Docker running, and a
# PostgreSQL whose user may create databases, in KANABOL_CHECK_DB or the API's user secrets. On Windows it
# starts Docker Desktop when it has stopped, which it does after a few idle minutes.

set -u

if [ "${1:-}" = --fresh ]; then
  [ "$#" -eq 4 ] || {
    echo "usage: scripts/gate.sh --fresh <branch> <clone folder> <log folder>"
    exit 2
  }
  repo=$(git rev-parse --show-toplevel) || exit 2
  [ -e "$3" ] && {
    echo "$3 exists: use a new folder"
    exit 2
  }
  mkdir -p "$4" || exit 2
  logs=$(cd "$4" && pwd -P)
  started=$(date +%s)
  git clone -q --branch "$2" "$repo" "$3" || exit 1
  cd "$3" || exit 1
  if ! sh scripts/setup.sh >"$logs/setup.log" 2>&1; then
    echo "scripts/setup.sh failed: $logs/setup.log"
    exit 1
  fi
  echo "setup: $(($(date +%s) - started)) s" >"$logs/setup.time"
  exec sh scripts/gate.sh "$logs"
fi

cd "$(git rev-parse --show-toplevel)" || exit 2
logs=${1:?usage: scripts/gate.sh <log folder> [script...]}
shift
mkdir -p "$logs" || exit 2
logs=$(cd "$logs" && pwd -P)
summary="$logs/summary.txt"
: >"$summary"
note() { printf '%-5s %s\n' "$1" "$2" >>"$summary"; }
started=$(date +%s)
[ -f "$logs/setup.time" ] && note ok "$(cat "$logs/setup.time")"

docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
docker_up() {
  "$docker_cli" info >/dev/null 2>&1 && return 0
  "$docker_cli" desktop start >/dev/null 2>&1 || return 1
  waited=0
  until "$docker_cli" info >/dev/null 2>&1; do
    waited=$((waited + 1))
    [ "$waited" -gt 60 ] && return 1
    sleep 5
  done
}

# Docker Desktop stops itself after a few idle minutes, which a long script without Docker can leave it.
if "$docker_cli" desktop version >/dev/null 2>&1; then
  (while :; do "$docker_cli" info >/dev/null 2>&1 </dev/null; sleep 60; done) &
  keepalive=$!
  trap 'kill $keepalive 2>/dev/null' EXIT
fi

pg_bin=${KANABOL_PGBIN:-}
pg_data=${KANABOL_PGDATA:-}
if [ -z "$pg_bin" ] && [ -n "${LOCALAPPDATA:-}" ] && [ -d "$LOCALAPPDATA/Kanabol/pgdata" ]; then
  pg_bin="$LOCALAPPDATA/Kanabol/pgsql/bin"
  pg_data="$LOCALAPPDATA/Kanabol/pgdata"
fi
pg_ctl() { "$pg_bin/pg_ctl" "$@" >/dev/null 2>&1 </dev/null; }
pg_up() {
  [ -n "$pg_bin" ] || return 0
  pg_ctl -D "$pg_data" status && return 0
  pg_ctl -D "$pg_data" -l "$(dirname "$pg_data")/postgres.log" -w -t 60 start
}
pg_on_5432() {
  [ -n "$pg_bin" ] && pg_ctl -D "$pg_data" status && "$pg_bin/pg_isready" -h 127.0.0.1 -p 5432 >/dev/null 2>&1
}

# The preparation, unless CI made it already or the old gate is asked for.
if [ -n "${KANABOL_GATE_NO_REUSE:-}" ]; then
  unset KANABOL_GATE_CACHE
elif [ -z "${KANABOL_GATE_CACHE:-}" ]; then
  docker_up || note WARN "docker did not start before the preparation"
  pg_up || note WARN "PostgreSQL did not start before the preparation"
  from=$(date +%s)
  if sh scripts/gate-prepare.sh "$logs/cache" >"$logs/prepare.log" 2>&1; then
    note ok "prepare ($(($(date +%s) - from)) s)"
    KANABOL_GATE_CACHE="$logs/cache"
    export KANABOL_GATE_CACHE
  else
    note WARN "prepare failed ($(($(date +%s) - from)) s): the scripts run without the cache, $logs/prepare.log"
  fi
fi

reused() { # reused <script>: how many of its commands the cache answered
  [ -n "${KANABOL_GATE_CACHE:-}" ] && [ -f "$KANABOL_GATE_CACHE/reused.log" ] || {
    echo 0
    return
  }
  grep -c "^$1 " "$KANABOL_GATE_CACHE/reused.log"
}

failed=0
run() { # run <script>[:<tasks>]
  name=${1%%:*}
  tasks=
  label=$name
  case "$1" in *:*)
    tasks=$(printf '%s' "${1#*:}" | tr ',' ' ')
    label="$name-$(printf '%s' "$tasks" | tr -d ' ')"
    ;;
  esac
  docker_up || note WARN "docker did not start before $label"
  pg_up || note WARN "PostgreSQL did not start before $label"
  from=$(date +%s)
  # shellcheck disable=SC2086 # the tasks are separate words
  if KANABOL_GATE_SCRIPT=$label sh "scripts/$name.sh" $tasks >"$logs/$label.log" 2>&1; then
    note ok "$label ($(($(date +%s) - from)) s, $(reused "$label") reused)"
  else
    note FAIL "$label ($(($(date +%s) - from)) s, $(reused "$label") reused)"
    failed=$((failed + 1))
  fi
}

if [ "$#" -eq 0 ]; then
  for script in scripts/check-*.sh; do
    name=$(basename "$script" .sh)
    [ "$name" = check-docker ] || set -- "$@" "$name"
  done
  set -- "$@" check-docker:1,2,3,4,6 check-docker:5
fi

for wanted in "$@"; do
  if [ "$wanted" = check-docker:5 ] && pg_on_5432; then
    pg_ctl -D "$pg_data" -m fast -w stop
    run "$wanted"
    pg_up || note WARN "PostgreSQL did not start again"
  else
    run "$wanted"
  fi
done

note done "$(($(($(date +%s) - started)) / 60)) minutes"
cat "$summary"
[ "$failed" -eq 0 ]
