#!/bin/sh
# Acceptance checks for Docker (group 4 in the project's Tasks.md).
# Usage: scripts/check-docker.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Needs Docker running. Everything runs in its own throwaway
# containers, networks and volumes, so it leaves the real database and logs alone.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

# Shells opened before Docker Desktop was installed don't have it on the PATH.
command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
# Git Bash would otherwise rewrite container paths like /data/attachments into Windows paths.
export MSYS_NO_PATHCONV=1

failures=0
scratch=$(mktemp -d)
run_id="kanabol-check-$$"
network="$run_id-net"
api_image="kanabol-check-api:$$"
secret=check-secret-with-at-least-32-characters-in-it

cleanup() {
  containers=$(docker ps -aq --filter "label=$run_id")
  [ -z "$containers" ] || docker rm -f $containers >/dev/null 2>&1
  docker network rm "$network" >/dev/null 2>&1
  images=$(docker images -q --filter "reference=kanabol-check-*:$$")
  [ -z "$images" ] || docker rmi -f $images >/dev/null 2>&1
  rm -rf "$scratch"
}
trap cleanup EXIT

check() { # check <description> <command...>
  description=$1
  shift
  if "$@" >"$scratch/last-check.log" 2>&1; then
    printf '  ok    %s\n' "$description"
  else
    printf '  FAIL  %s\n' "$description"
    sed 's/^/        /' "$scratch/last-check.log" | tail -n 15
    failures=$((failures + 1))
  fi
}

native() { # A path Docker understands, on Windows too.
  cygpath -m "$1" 2>/dev/null || printf '%s' "$1"
}

tracked() {
  [ -f "$1" ] && [ -n "$(git ls-files -- "$1")" ]
}

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
}

# Waits for a container's health to reach a state. Prints its logs when it doesn't.
health_becomes() { # health_becomes <container> <state> <seconds>
  waited=0
  while [ "$waited" -lt "$3" ]; do
    state=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$1" 2>/dev/null)
    [ "$state" = "$2" ] && return 0
    sleep 2
    waited=$((waited + 2))
  done
  echo "$1 is '$state' after $3 seconds, expected '$2'"
  docker logs --tail 20 "$1" 2>&1
  return 1
}

docker_is_running() {
  docker info --format '{{.ServerVersion}}'
}

on_network() {
  docker network inspect "$network" >/dev/null 2>&1 || docker network create "$network" >/dev/null
}

start_postgres() { # A database on the check's network, under the name compose gives it.
  on_network || return 1
  docker ps -q --filter "name=^$run_id-postgres$" | grep -q . && return 0
  docker run -d --label "$run_id" --name "$run_id-postgres" --network "$network" --network-alias postgres \
    -e POSTGRES_DB=kanabol -e POSTGRES_USER=kanabol -e POSTGRES_PASSWORD=check \
    --health-cmd 'pg_isready -U kanabol -d kanabol' --health-interval 2s --health-retries 30 \
    postgres:17-alpine >/dev/null || return 1
  health_becomes "$run_id-postgres" healthy 60
}

# The backend as it's staged, with what a developer's folder has and an image must not get.
backend_copy() { # backend_copy <folder>
  mkdir -p "$1"
  git archive "$(git write-tree)" backend | tar -x -C "$1"
  git ls-files -z --others --exclude-standard -- backend | tar -c --null -T - 2>/dev/null | tar -x -C "$1"
  mkdir -p "$1/backend/src/Kanabol.Api/bin/Debug" "$1/backend/src/Kanabol.Api/obj/Debug"
  echo leak >"$1/backend/src/Kanabol.Api/bin/Debug/leak.dll"
  echo leak >"$1/backend/src/Kanabol.Api/obj/Debug/leak.dll"
  echo leak >"$1/backend/src/Kanabol.Api/appsettings.Development.local.json"
}

context_listing() { # context_listing <context folder>: every file docker build gets from it
  tag="kanabol-check-context:$$"
  printf 'FROM busybox\nCOPY . /context\n' >"$scratch/ContextDockerfile"
  docker build -q -t "$tag" -f "$(native "$scratch/ContextDockerfile")" "$(native "$1")" >/dev/null || return 1
  docker run --rm "$tag" find /context -type f
}

api_context_has_nothing_extra() {
  copy="$scratch/api-context"
  backend_copy "$copy"
  context_listing "$copy/backend" >"$scratch/api-context.txt" || return 1
  grep -q 'Kanabol.Api/Program.cs' "$scratch/api-context.txt" || {
    echo "the context has no source"
    return 1
  }
  if grep -E 'leak|/tests/|\.local\.json' "$scratch/api-context.txt"; then
    return 1
  fi
}

api_context_control() {
  copy="$scratch/api-context-control"
  backend_copy "$copy"
  rm -f "$copy/backend/.dockerignore"
  context_listing "$copy/backend" | grep -q 'leak.dll'
}

build_api_image() {
  docker build -t "$api_image" backend >"$scratch/api-build.log" 2>&1 || {
    tail -n 25 "$scratch/api-build.log"
    return 1
  }
}

# A change in the source has to reuse the restore layer and rebuild only what follows it.
source_change_keeps_the_restore_layer() {
  copy="$scratch/api-cache"
  backend_copy "$copy"
  tag="kanabol-check-cache:$$"
  docker build -q -t "$tag" "$(native "$copy/backend")" >/dev/null || return 1
  printf '\n// a change in the source\n' >>"$copy/backend/src/Kanabol.Api/Program.cs"
  docker build --progress=plain -t "$tag" "$(native "$copy/backend")" >"$scratch/api-cache.log" 2>&1 || {
    tail -n 25 "$scratch/api-cache.log"
    return 1
  }
  grep -A1 -E 'RUN dotnet restore' "$scratch/api-cache.log" | grep -q 'CACHED' || {
    echo "restore ran again"
    return 1
  }
  ! grep -A1 -E 'RUN dotnet publish' "$scratch/api-cache.log" | grep -q 'CACHED'
}

api_config_is() { # api_config_is <format> <expected>
  is "$2" "$(docker inspect --format "$1" "$api_image")"
}

api_env_has() {
  docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$api_image" | tr -d '\r' | grep -qxF -- "$1"
}

api_runs() { # api_runs <shell command, run in the image as its own user>
  docker run --rm --entrypoint sh "$api_image" -c "$1"
}

api_answers_healthy() {
  start_postgres || return 1
  docker run -d --label "$run_id" --name "$run_id-api" --network "$network" \
    --health-interval 2s --health-start-period 0s --health-retries 30 \
    -e 'ConnectionStrings__Default=Host=postgres;Database=kanabol;Username=kanabol;Password=check' \
    -e "Jwt__Secret=$secret" -e 'Admin__InitialPassword=check-password' \
    "$api_image" >/dev/null || return 1
  health_becomes "$run_id-api" healthy 90
}

healthcheck_notices_a_dead_api() {
  docker run -d --label "$run_id" --name "$run_id-dead" --entrypoint sleep \
    --health-interval 1s --health-start-period 0s --health-retries 1 \
    "$api_image" 300 >/dev/null || return 1
  health_becomes "$run_id-dead" unhealthy 30
}

task_1() {
  echo "4.1 backend/Dockerfile"
  check "Docker is running" docker_is_running
  check "the Dockerfile is tracked" tracked backend/Dockerfile
  check ".dockerignore is tracked" tracked backend/.dockerignore
  check "the build context has no bin, obj, tests or local settings" api_context_has_nothing_extra
  check "control: without .dockerignore they do get in" api_context_control
  check "the image builds" build_api_image
  check "a source change keeps the restore layer" source_change_keeps_the_restore_layer
  check "it runs as the app user" api_config_is '{{.Config.User}}' app
  check "it listens on 8080" api_config_is '{{range $port, $_ := .Config.ExposedPorts}}{{$port}}{{end}}' 8080/tcp
  check "ASPNETCORE_URLS is set" api_env_has 'ASPNETCORE_URLS=http://+:8080'
  check "diagnostics are off" api_env_has 'DOTNET_EnableDiagnostics=0'
  check "it runs the API" api_config_is '{{join .Config.Entrypoint " "}}' 'dotnet Kanabol.Api.dll'
  check "the app user can write to /data/attachments" api_runs 'test "$(id -un)" = app && touch /data/attachments/probe'
  check "it holds the runtime, not the SDK" api_runs 'test -z "$(dotnet --list-sdks)"'
  check "it holds no source code" api_runs 'test -f /app/Kanabol.Api.dll && test -z "$(find /app -name "*.cs" -o -name "*.csproj")"'
  check "the healthcheck goes healthy on a real database" api_answers_healthy
  check "the healthcheck goes unhealthy when nothing answers" healthcheck_notices_a_dead_api
}

if [ "$#" -eq 0 ]; then
  set -- 1
fi

for task in "$@"; do
  "task_$task"
done

echo
if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
