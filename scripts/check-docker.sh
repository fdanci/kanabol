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
docker() {
  MSYS_NO_PATHCONV=1 command docker "$@"
}

failures=0
scratch=$(mktemp -d)
run_id="kanabol-check-$$"
network="$run_id-net"
api_image="kanabol-check-api:$$"
web_image="kanabol-check-web:$$"
secret=check-secret-with-at-least-32-characters-in-it

compose_projects=
web_url=
seq_url=
http_port=
https_port=

cleanup() {
  for project in $compose_projects; do
    docker compose -p "$project" --env-file "$(native "$scratch/check.env")" down -v --rmi local --remove-orphans >/dev/null 2>&1
  done
  containers=$(docker ps -aq --filter "label=$run_id")
  [ -z "$containers" ] || docker rm -f -v $containers >/dev/null 2>&1
  docker network rm "$network" >/dev/null 2>&1
  images=$(docker images -q --filter "reference=kanabol-check-*:$$")
  [ -z "$images" ] || docker rmi -f $images >/dev/null 2>&1
  images=$(docker images -q --filter "reference=kanabol-*:check-$"; docker images -q --filter "reference=$run_id*")
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

# A stand-in for the API, under the name nginx looks for. It reports what reached it, and accepts
# WebSocket upgrades.
start_api_stub() {
  on_network || return 1
  docker ps -q --filter "name=^$run_id-stub$" | grep -q . && return 0
  docker run -d --label "$run_id" --name "$run_id-stub" --network "$network" --network-alias api node:22-alpine node -e '
    const server = require("http").createServer((request, response) => {
      let size = 0;
      request.on("data", (chunk) => (size += chunk.length));
      request.on("end", () => {
        response.setHeader("Content-Type", "application/json");
        response.end(JSON.stringify({ url: request.url, headers: request.headers, size }));
      });
    });
    server.on("upgrade", (request, socket) => {
      socket.end("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n\r\n");
    });
    server.listen(8080);
  ' >/dev/null
}

# The frontend as it's staged, with what a developer's folder has and an image must not get.
frontend_copy() { # frontend_copy <folder>
  mkdir -p "$1"
  git archive "$(git write-tree)" frontend | tar -x -C "$1"
  for folder in node_modules dist .angular coverage; do
    mkdir -p "$1/frontend/$folder"
    echo leak >"$1/frontend/$folder/leak.js"
  done
}

web_context_has_nothing_extra() {
  copy="$scratch/web-context"
  frontend_copy "$copy"
  context_listing "$copy/frontend" >"$scratch/web-context.txt" || return 1
  grep -q 'src/main.ts' "$scratch/web-context.txt" || {
    echo "the context has no source"
    return 1
  }
  if grep -F 'leak.js' "$scratch/web-context.txt"; then
    return 1
  fi
}

web_context_control() {
  copy="$scratch/web-context-control"
  frontend_copy "$copy"
  rm -f "$copy/frontend/.dockerignore"
  context_listing "$copy/frontend" | grep -q 'leak.js'
}

build_web_image() {
  docker build -t "$web_image" frontend >"$scratch/web-build.log" 2>&1 || {
    tail -n 25 "$scratch/web-build.log"
    return 1
  }
}

# Starts nginx on a free port, with the image's configuration or the one given. Sets web_url.
start_web() { # start_web <name> [configuration file]
  start_api_stub || return 1
  if [ -n "${2:-}" ]; then
    docker run -d --label "$run_id" --name "$run_id-$1" --network "$network" -p 127.0.0.1::80 \
      -v "$(native "$2"):/etc/nginx/kanabol-app.conf:ro" "$web_image" >/dev/null || return 1
  else
    docker run -d --label "$run_id" --name "$run_id-$1" --network "$network" -p 127.0.0.1::80 \
      "$web_image" >/dev/null || return 1
  fi
  sleep 2
  web_url="http://$(docker port "$run_id-$1" 80/tcp | head -n 1)"
  curl -sS -o /dev/null --retry 10 --retry-connrefused --retry-delay 1 "$web_url/"
}

web_serves_the_app() { # web_serves_the_app <path>
  body=$(curl -sS -w '%{http_code}' "$web_url$1")
  case "$body" in
    *'<kb-root'*200) ;;
    *)
      echo "$1 didn't serve the app: $(printf '%s' "$body" | tail -c 200)"
      return 1
      ;;
  esac
}

web_asset_is() { # web_asset_is <path> <content type>
  is "200 $2" "$(curl -sS -o /dev/null -w '%{http_code} %{content_type}' "$web_url$1")"
}

web_compresses_scripts() {
  script=$(curl -sS "$web_url/" | sed -n 's/.*src="\(main-[^"]*\.js\)".*/\1/p' | head -n 1)
  [ -n "$script" ] || {
    echo "no main script in index.html"
    return 1
  }
  curl -sSI -H 'Accept-Encoding: gzip' "$web_url/$script" | tr -d '\r' | grep -qi '^content-encoding: gzip$'
}

web_sends_header() { # web_sends_header <header line>
  curl -sSI "$web_url/" | tr -d '\r' | grep -qi "^$1\$"
}

api_stub_saw() { # api_stub_saw <text>: looks in what reached the stub for /api/orders?page=1
  curl -sS -H 'Host: kanabol.example' "$web_url/api/orders?page=1" | grep -qF -- "$1"
}

upgrade_status() {
  curl -sS -o /dev/null -w '%{http_code}' --max-time 5 --http1.1 \
    -H 'Connection: Upgrade' -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' \
    -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' "$web_url/hubs/board"
}

web_upgrades_hubs() {
  is 101 "$(upgrade_status)"
}

# Without the upgrade lines the same request has to fail, or the check above proves nothing.
upgrade_control() {
  sed -e '/Upgrade $http_upgrade/d' -e '/Connection "upgrade"/d' frontend/nginx-app.conf >"$scratch/no-upgrade.conf"
  grep -q 'proxy_pass' "$scratch/no-upgrade.conf" || return 1
  start_web no-upgrade "$scratch/no-upgrade.conf" || return 1
  [ "$(upgrade_status)" != 101 ]
}

upload_status_is() { # upload_status_is <megabytes> <status>
  dd if=/dev/zero of="$scratch/body.bin" bs=1048576 count="$1" 2>/dev/null
  is "$2" "$(curl -sS -o /dev/null -w '%{http_code}' --data-binary @"$(native "$scratch/body.bin")" \
    -H 'Content-Type: application/octet-stream' -H 'Expect:' "$web_url/api/attachments")"
}

task_2() {
  echo "4.2 frontend/Dockerfile and nginx.conf"
  web_url=
  check "the Dockerfile is tracked" tracked frontend/Dockerfile
  check "nginx.conf is tracked" tracked frontend/nginx.conf
  check "its shared part is tracked" tracked frontend/nginx-app.conf
  check ".dockerignore is tracked" tracked frontend/.dockerignore
  check "the build context has no node_modules, dist or caches" web_context_has_nothing_extra
  check "control: without .dockerignore they do get in" web_context_control
  check "the image builds" build_web_image
  start_web web >"$scratch/start-web.log" 2>&1 || cat "$scratch/start-web.log"
  check "nginx serves the app at /" web_serves_the_app /
  check "a deep link falls back to the app" web_serves_the_app /projects/KAN/board
  check "it serves the brand files" web_asset_is /brand/favicon-32.png image/png
  check "it serves the manifest with its own type" web_asset_is /manifest.webmanifest application/manifest+json
  check "it compresses scripts" web_compresses_scripts
  check "it sends X-Content-Type-Options" web_sends_header 'X-Content-Type-Options: nosniff'
  check "it sends X-Frame-Options" web_sends_header 'X-Frame-Options: DENY'
  check "/api goes to the API, path and query intact" api_stub_saw '"url":"/api/orders?page=1"'
  check "the API gets the client's address" api_stub_saw '"x-forwarded-for"'
  check "the API gets the host the client asked for" api_stub_saw '"host":"kanabol.example"'
  check "/hubs upgrades to a WebSocket" web_upgrades_hubs
  check "a 26 MB upload gets through, for the API to judge" upload_status_is 26 200
  check "a 31 MB upload stops at nginx" upload_status_is 31 413
  check "control: without the upgrade lines /hubs doesn't upgrade" upgrade_control
}

# Values compose would otherwise read from .env, so the real .env and its volumes stay out of this.
write_check_env() {
  [ -f "$scratch/check.env" ] && return 0
  seq_password=check-seq-password
  seq_hash=$(echo "$seq_password" | docker run --rm -i datalust/seq config hash | tr -d '\r')
  [ -n "$seq_hash" ] || return 1
  cat >"$scratch/check.env" <<EOF
POSTGRES_PASSWORD=check-database-password
JWT_SECRET=$secret
SEQ_ADMIN_HASH=$seq_hash
ADMIN_INITIAL_PASSWORD=check-admin-password
EOF
}

compose_as() { # compose_as <project> <arguments...>
  project=$1
  shift
  case " $compose_projects " in
    *" $project "*) ;;
    *) compose_projects="$compose_projects $project" ;;
  esac
  docker compose -p "$project" --env-file "$(native "$scratch/check.env")" "$@"
}

# Judges what compose really makes of the files. "services" is what the expression gets to look at.
config_satisfies() { # config_satisfies <-f arguments, or nothing for the default files> <JS expression>
  # shellcheck disable=SC2086 # the file arguments are meant to split
  compose_as "$run_id" $1 config --format json >"$scratch/config.json" || return 1
  node -e "
    const services = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).services;
    const published = (name) => (services[name].ports ?? []).map((p) => (p.host_ip ?? '') + ':' + p.published + ':' + p.target).sort().join(' ');
    const env = (name) => services[name].environment ?? {};
    if (!($2)) {
      console.log(JSON.stringify(services, null, 1).slice(0, 1800));
      process.exit(1);
    }
  " "$(native "$scratch/config.json")"
}

base='-f compose.yaml'
prod='-f compose.yaml -f compose.prod.yaml'

# The whole stack as compose.yaml describes it. Only the ports are the check's own, to stay out of
# the way of whatever already runs here.
write_stack_ports() {
  cat >"$scratch/stack-ports.yaml" <<'EOF'
services:
  web:
    ports: ["127.0.0.1::80"]
  seq:
    ports: ["127.0.0.1::80"]
EOF
}

stack() {
  compose_as "$run_id" -f compose.yaml -f "$(native "$scratch/stack-ports.yaml")" "$@"
}

stack_comes_up() {
  write_stack_ports
  stack up -d --build --wait --wait-timeout 240 >"$scratch/stack.log" 2>&1 || {
    tail -n 20 "$scratch/stack.log"
    stack logs --tail 20 api 2>&1
    return 1
  }
  web_url="http://$(stack port web 80 | tr -d '\r')"
  seq_url="http://$(stack port seq 80 | tr -d '\r')"
}

api_through_web_is_real() { # 404 as the API's ProblemDetails, not nginx's page
  is '404 application/problem+json' "$(curl -sS -o /dev/null -w '%{http_code} %{content_type}' "$web_url/api/no-such-thing" | sed 's/;.*//')"
}

stack_sql_gives() { # stack_sql_gives <expected> <sql>
  is "$1" "$(stack exec -T postgres psql -U kanabol -d kanabol -tAc "$2" | tr -d '\r')"
}

volume_survives_a_new_api_container() {
  stack exec -T api sh -c 'touch /data/attachments/still-here' || return 1
  stack rm -sf api >/dev/null 2>&1 || return 1
  (
    export ADMIN_INITIAL_PASSWORD=
    stack up -d --no-build --wait --wait-timeout 120 api
  ) >"$scratch/stack.log" 2>&1 || {
    tail -n 10 "$scratch/stack.log"
    return 1
  }
  stack exec -T api test -f /data/attachments/still-here
}

# Seq's API takes a session cookie, and a token on top of it for changes.
seq_login() { # seq_login <url> <password>
  seq_cookies="$scratch/seq-cookies-$$"
  seq_session=$(curl -sS -c "$(native "$seq_cookies")" -H 'Content-Type: application/json' \
    -d "{\"Username\":\"admin\",\"Password\":\"$2\"}" "$1/api/users/login")
  case "$seq_session" in
    *'"CsrfToken"'*) ;;
    *)
      echo "Seq refused the login: $seq_session"
      return 1
      ;;
  esac
}

seq_has_event() { # seq_has_event <url> <password> <text in the message> <seconds to wait>
  seq_login "$1" "$2" || return 1
  waited=0
  while [ "$waited" -lt "$4" ]; do
    curl -sS -b "$(native "$seq_cookies")" -G --data-urlencode "filter=@Message like '%$3%'" \
      "$1/api/events?count=1&render=true" | grep -qF -- "$3" && return 0
    sleep 2
    waited=$((waited + 2))
  done
  echo "no event with '$3' in Seq after $4 seconds"
  return 1
}

logs_reach_seq() {
  curl -sS -o /dev/null "$web_url/api/seen-by-seq"
  seq_has_event "$seq_url" check-seq-password /api/seen-by-seq 30
}

new_database_needs_the_admin_password() {
  project="$run_id-noadmin"
  compose_projects="$compose_projects $project"
  (
    export ADMIN_INITIAL_PASSWORD=
    docker tag "$(stack images -q api | head -n 1)" "$project-api"
    compose_as "$project" -f compose.yaml up -d --no-build postgres api
  ) >"$scratch/noadmin.log" 2>&1
  waited=0
  while [ "$waited" -lt 90 ]; do
    state=$(compose_as "$project" -f compose.yaml ps -a --format '{{.State}}' api | tr -d '\r')
    [ "$state" = exited ] && break
    sleep 2
    waited=$((waited + 2))
  done
  is exited "$state" || return 1
  compose_as "$project" -f compose.yaml logs api 2>&1 | grep -q 'Admin:InitialPassword'
}

# The production stack, on the check's ports and with the certificates given.
prod_stack() {
  KANABOL_VERSION="check-$$" compose_as "$run_id-prod" -f compose.yaml -f compose.prod.yaml \
    -f "$(native "$scratch/prod-ports.yaml")" "$@"
}

prod_stack_comes_up() { # prod_stack_comes_up <certificates folder>
  cat >"$scratch/prod-ports.yaml" <<EOF
services:
  web:
    ports: !override
      - "127.0.0.1::80"
      - "127.0.0.1::443"
    volumes: !override
      - "$(native "$1"):/etc/nginx/certs:ro"
      - "./frontend/nginx.prod.conf:/etc/nginx/conf.d/default.conf:ro"
EOF
  docker image inspect "kanabol-api:check-$$" >/dev/null 2>&1 || docker tag "$(stack images -q api | head -n 1)" "kanabol-api:check-$$" || return 1
  docker image inspect "kanabol-web:check-$$" >/dev/null 2>&1 || docker tag "$(stack images -q web | head -n 1)" "kanabol-web:check-$$" || return 1
  prod_stack up -d --no-build --wait --wait-timeout 180 >"$scratch/prod.log" 2>&1 || {
    tail -n 15 "$scratch/prod.log"
    prod_stack logs --tail 15 web api 2>&1
    return 1
  }
  http_port=$(prod_stack port web 80 | tr -d '\r' | sed 's/.*://')
  https_port=$(prod_stack port web 443 | tr -d '\r' | sed 's/.*://')
}

throwaway_certificate() {
  mkdir -p "$scratch/throwaway-certs"
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=kanabol.example' \
    -keyout "$(native "$scratch/throwaway-certs/server.key")" -out "$(native "$scratch/throwaway-certs/server.crt")"
}

http_redirects_to_https() {
  is '301 https://kanabol.example/projects?page=2' \
    "$(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}' -H 'Host: kanabol.example' "http://127.0.0.1:$http_port/projects?page=2")"
}

https_serves_the_app() {
  web_url="https://127.0.0.1:$https_port"
  curl -sSk "$web_url/projects/KAN/board" | grep -q '<kb-root'
}

https_reaches_the_api() {
  is '404 application/problem+json' "$(curl -sSk -o /dev/null -w '%{http_code} %{content_type}' "https://127.0.0.1:$https_port/api/no-such-thing" | sed 's/;.*//')"
}

task_3() {
  echo "4.3 compose"
  check "compose.yaml is tracked" tracked compose.yaml
  check "compose.override.yaml is tracked" tracked compose.override.yaml
  check "compose.prod.yaml is tracked" tracked compose.prod.yaml
  check "nginx.prod.conf is tracked" tracked frontend/nginx.prod.conf
  check "the values for the checks are ready" write_check_env

  check "compose.yaml has the four services" config_satisfies "$base" \
    "Object.keys(services).sort().join() === 'api,postgres,seq,web'"
  check "compose.yaml alone publishes nothing" config_satisfies "$base" \
    "Object.values(services).every((s) => !s.ports)"
  check "it uses PostgreSQL 17 and Seq" config_satisfies "$base" \
    "services.postgres.image === 'postgres:17-alpine' && services.seq.image === 'datalust/seq:latest'"
  check "the API gets its settings" config_satisfies "$base" \
    "env('api').ConnectionStrings__Default === 'Host=postgres;Database=kanabol;Username=kanabol;Password=check-database-password'
     && env('api').Jwt__Secret === '$secret'
     && env('api').Serilog__WriteTo__1__Args__serverUrl === 'http://seq:5341'
     && env('api').Storage__Root === '/data/attachments'
     && env('api').Database__MigrateOnStartup === 'true'
     && env('api').ASPNETCORE_ENVIRONMENT === 'Production'"
  check "the API gets the first admin's password" config_satisfies "$base" \
    "env('api').Admin__InitialPassword === 'check-admin-password'"
  check "the API waits for a healthy database" config_satisfies "$base" \
    "services.api.depends_on.postgres.condition === 'service_healthy'"
  check "data lives in named volumes" config_satisfies "$base" \
    "['postgres', 'seq', 'api'].every((name) => services[name].volumes.some((v) => v.type === 'volume'))"

  check "dev publishes PostgreSQL on 5432" config_satisfies '' "published('postgres') === '127.0.0.1:5432:5432'"
  check "dev publishes Seq's UI and ingestion on 5341" config_satisfies '' "published('seq') === '127.0.0.1:5341:80'"
  check "dev publishes the API on 8080" config_satisfies '' "published('api') === '127.0.0.1:8080:8080'"
  check "dev publishes nginx on 4200" config_satisfies '' "published('web') === '127.0.0.1:4200:80'"

  check "prod builds nothing" config_satisfies "$prod" "!services.api.build && !services.web.build"
  check "prod uses the tagged images" config_satisfies "$prod" \
    "services.api.image === 'kanabol-api:v0.1.0' && services.web.image === 'kanabol-web:v0.1.0'"
  check "prod restarts everything" config_satisfies "$prod" \
    "Object.values(services).every((s) => s.restart === 'unless-stopped')"
  check "prod publishes only 80 and 443 on nginx" config_satisfies "$prod" \
    "!services.postgres.ports && !services.seq.ports && !services.api.ports && published('web') === ':443:443 :80:80'"
  check "prod mounts the certificates read-only" config_satisfies "$prod" \
    "services.web.volumes.some((v) => v.target === '/etc/nginx/certs' && v.read_only === true && v.source.endsWith('certs'))"

  check "the whole stack comes up, with a healthy API" stack_comes_up
  check "nginx serves the app" web_serves_the_app /
  check "nginx reaches the real API" api_through_web_is_real
  check "the API migrated and seeded the database" stack_sql_gives 1 "select count(*) from users where email = 'admin@kanabol.local'"
  check "attachments survive a new API container, started without that password" volume_survives_a_new_api_container
  check "the API's logs reach Seq" logs_reach_seq
  check "control: a new database without that password stops the API" new_database_needs_the_admin_password

  check "a throwaway certificate is ready" throwaway_certificate
  check "the production stack comes up" prod_stack_comes_up "$scratch/throwaway-certs"
  check "prod sends port 80 to HTTPS" http_redirects_to_https
  check "prod serves the app over HTTPS" https_serves_the_app
  check "prod reaches the API over HTTPS" https_reaches_the_api
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3
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
