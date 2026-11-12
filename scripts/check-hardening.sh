#!/bin/sh
# Acceptance checks for the hardening (group 19 in the project's Tasks.md; docs/SPEC.md §4.5, §6.2, §8.1, §9,
# §13.2, §14.6, §14.7, §16; docs/UI_UX_SPEC.md §2, §7.8). The tests and specs hold the rules, so these checks
# run them, and add what they can't see: nginx as the image runs it, the real API process on a real
# PostgreSQL, the frontend's production build and the merge into main.
#
# Usage: scripts/check-hardening.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  nginx's headers: X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy and the
#      CSP of docs/SPEC.md §13.2, at server level in nginx-app.conf, on the image built from frontend/: the
#      app, a deep link, a script, the manifest, what the API answers and nginx's own 413, each once; HSTS
#      only in nginx.prod.conf, on 443, a year without preload, and not on port 80's redirect, which still
#      goes to HTTPS; the built index.html with nothing the CSP would block (no inline script or handler,
#      no stylesheet left waiting on media="print", nothing from another origin), with controls: a header
#      in a location, a header without always, the build's inline script. Docker.
#   2  the hub's limits and who stops hearing what: the tests (HubLimitsTests, RealtimeAccessTests,
#      BoardHubTests and the handlers'), then the real API in Production over a real WebSocket, with a small
#      SignalR client in Node: eight projects joined and the ninth refused with Hub.TooManyProjects, a place
#      freed by LeaveProject, four refusals keeping a connection and the fifth closing it with a warning in
#      the log, a member taken out of a project hearing nothing more of it while another still does, and a
#      change of role and a deactivation closing every connection of that person and nobody else's, the token
#      they still hold joining no project. Docker, PostgreSQL.
#   3  the hub's sends: the tests (BoardSenderTests, and every test listening to the hub, whose probes go
#      through the same queue), then the real API in Production: a board hears a task created, moved,
#      renamed, archived and restored in that order, and while a board that joined a project stops reading
#      its socket, 20 workers move tasks for 40 seconds and none of their requests takes 3 seconds; the
#      server closes that board meanwhile, which proves its writes blocked (the old notifier, which sent
#      inside the request, made one wait 9.3 s); then the redaction: its tests on a token in a list, an
#      object, a dictionary (keys too), a Uri, which goes as text, and deeper, values without one kept as
#      they are, and the API host's own Serilog pipeline, then the real API in Development, which logs a
#      board's negotiation and WebSocket with the whole address: access_token=[hidden], no token. Nothing
#      the real API takes in reaches its log as a structured value yet, so those are the tests'.
#      Docker, PostgreSQL.
#   4  the refresh tokens' cleanup: the tests (the command's one DELETE and its rows, the worker, its
#      settings, the API's tests with it off), then the real API in Production on a database holding rows
#      made straight in it: turned off, it keeps every one and says so; at start it deletes those expired or
#      revoked over 30 days ago, 3 of 6, with one log line giving the count, and the admin's live sessions
#      stay; Auth__RefreshTokenCleanupInterval=00:30:00 stops the start. Docker, PostgreSQL.
#   5  the first bundle: the specs (the four paginators in Romanian from their own pages, the slide toggle's
#      defaults in ThemeToggle, the navbar's switch after @defer), lint, formatting, the production build
#      with no warning, its budget included, the budget at what remained plus 5% (750 kB, the error at
#      1 MB), the Initial total under it and under 800 kB, printed; none of the paginator, mat-select, the
#      slide toggle, the forms they bring, chart.js or SignalR in the files index.html loads, each in some
#      lazy chunk; every component with a mat-paginator providing its labels; then the charts' colors:
#      --kb-chart-1 and --kb-chart-2 in the built CSS (#122d94 and #135bf7, and #91c4fb and #3d6fc9 in both
#      dark rules), and kb-chart reading them instead of brand-800 and sky-300. Only the packages.
#   6  the tests' common password hashed once per suite: the tests (the people AuthApiTest and
#      AuthHandlerTest make share one hash, which signs in), no helper hashing per person, then the rule
#      that replaced the merge into main under 210 s (docs/SPEC.md v1.33, §14.3, §14.6): the hooks run the
#      same fast checks on main as anywhere, with no pre-push hook, and the full suite is the workflow's
#      (check-pipeline.sh 3 to 6 run the hooks themselves). Docker.
#
# Docker runs nginx and a stand-in for the API, and the tests' own PostgreSQL; 5 needs only the packages that
# scripts/setup.sh installs. PostgreSQL means one whose user may create databases, with its connection string
# in KANABOL_CHECK_DB or else in the API's user secrets (ConnectionStrings:Default); each run works in its own
# database, kanabol_hardening_check_<pid>, and drops it. The real API listens on port 5284.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

# Shells opened before Docker Desktop was installed don't have it on the PATH.
command -v docker >/dev/null 2>&1 || PATH="$PATH:/c/Program Files/Docker/Docker/resources/bin"
# Git Bash would otherwise rewrite container paths like /etc/nginx into Windows paths.
docker() {
  MSYS_NO_PATHCONV=1 command docker "$@"
}

failures=0
scratch=$(mktemp -d)
run_id="kanabol-hardening-$$"
network="$run_id-net"
web_image="kanabol-hardening-web:$$"
web_url=

check_db="kanabol_hardening_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5284
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
api_environment=
admin_connection=

admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'
initial_password='o parola initiala pentru colegi'
member_password='parola noua aleasa de coleg'

# docs/SPEC.md §13.2, written here as the spec has them, not read from the configuration.
csp="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'"
hsts='max-age=31536000'

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

native() { # native <path>: as Windows programs (node, docker, dotnet) read it
  cygpath -m "$1" 2>/dev/null || echo "$1"
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

docker_is_running() {
  docker info --format '{{.ServerVersion}}'
}

# ---- Tests, a database of this run, and the real API on it (as check-reports.sh has them).

# A build copies the DLLs into the API's bin folder, which the API started by an earlier task holds open
# on Windows: the API stops first, and api_is_up starts it again, on the same database, when a check needs it.
solution_builds() {
  stop_api
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
}

formatting_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
}

tests_pass() { # tests_pass <project> <filter> <minimum passed>
  stop_api
  log="$scratch/tests-$1.log"
  dotnet test "backend/tests/$1/$1.csproj" --nologo --filter "$2" >"$log" 2>&1 || {
    tail -n 25 "$log"
    return 1
  }
  passed=$(sed -n 's/.*Passed: *\([0-9]*\),.*/\1/p' "$log" | tail -n 1)
  [ "${passed:-0}" -ge "$3" ] || {
    echo "only ${passed:-0} tests passed, expected at least $3"
    return 1
  }
}

test_exists() { # test_exists <project> <name>: the test is there, so its filter can't run nothing
  dotnet test "backend/tests/$1/$1.csproj" --nologo --no-build --list-tests 2>/dev/null | tr -d '\r' | grep -qF "$2" || {
    echo "no test $2 in $1"
    return 1
  }
}

named_tests_pass() { # named_tests_pass <project> <test name...>: each exists, and all of them pass
  project=$1
  shift
  stop_api
  dotnet build "backend/tests/$project/$project.csproj" --nologo -v q >/dev/null 2>&1 || return 1
  filter=
  for name in "$@"; do
    test_exists "$project" "$name" || return 1
    filter="${filter:+$filter|}FullyQualifiedName~$name"
  done
  tests_pass "$project" "$filter" "$#"
}


# Run a statement and print its rows, with columns separated by |. Built once, on first use.
sql() { # sql <connection> <statement>
  sql_dll="$scratch/sql/bin/Debug/net10.0/sql.dll"
  if [ ! -f "$sql_dll" ]; then
    mkdir -p "$scratch/sql"
    cat >"$scratch/sql/sql.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.*" />
  </ItemGroup>
</Project>
XML
    cat >"$scratch/sql/Program.cs" <<'CS'
Console.OutputEncoding = System.Text.Encoding.UTF8;
await using var connection = new Npgsql.NpgsqlConnection(args[0]);
await connection.OpenAsync();
await using var command = new Npgsql.NpgsqlCommand(args[1], connection);
await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)?.ToString());
    Console.WriteLine(string.Join('|', values));
}
CS
    dotnet build "$scratch/sql/sql.csproj" --nologo -v q >"$scratch/sql/build.log" 2>&1 || {
      cat "$scratch/sql/build.log" >&2
      return 1
    }
  fi
  dotnet "$sql_dll" "$1" "$2"
}

need_database() {
  [ -n "$admin_connection" ] && return 0
  base_connection=${KANABOL_CHECK_DB:-$(dotnet user-secrets list --id kanabol-api 2>/dev/null |
    tr -d '\r' | sed -n 's/^ConnectionStrings:Default = //p')}
  if [ -z "$base_connection" ]; then
    echo "No database: set KANABOL_CHECK_DB or the API's ConnectionStrings:Default user secret."
    return 1
  fi
  admin_connection=$(printf '%s' "$base_connection" | sed 's/Database=[^;]*/Database=postgres/')
  sql "$admin_connection" "CREATE DATABASE $check_db"
}

connection_to() { # connection_to <database>
  printf '%s' "$base_connection" | sed "s/Database=[^;]*/Database=$1/"
}

in_db() { # in_db <statement>: its rows, as sql prints them
  sql "$(connection_to "$check_db")" "$1" | tr -d '\r'
}

# Starts the API on this run's database, with any other settings given as NAME=value, its output in the
# log given, and leaves its process id in started_pid. It doesn't wait.
launch_api() { # launch_api <log> <environment> [NAME=value...]
  log=$1
  environment=$2
  shift 2
  (
    cd "$api_dir" || exit 1
    # Seq's address leads nowhere, so a developer's own Seq gets nothing of these checks.
    exec env ASPNETCORE_ENVIRONMENT="$environment" \
      ConnectionStrings__Default="$(connection_to "$check_db")" \
      Jwt__Secret=only-for-these-checks-and-long-enough-to-pass \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword=$admin_password \
      Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 \
      "$@" dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$log" 2>&1 &
  started_pid=$!
}

# The API in Production unless asked otherwise, with any other settings given as NAME=value. A start in
# another environment, or with settings, stops the one running first; the next plain start goes back.
start_api() { # start_api <environment> [NAME=value...]
  wanted="$*"
  [ -n "$api_pid" ] && [ "$api_environment" = "$wanted" ] && return 0
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  launch_api "$api_log" "$@"
  api_pid=$started_pid
  api_environment=$wanted

  attempts=0
  until curl -s -o /dev/null "$API_URL/health"; do
    attempts=$((attempts + 1))
    if [ "$attempts" -gt 120 ] || ! kill -0 "$api_pid" 2>/dev/null; then
      api_pid=
      cat "$api_log"
      return 1
    fi
    sleep 0.5
  done
}

# Without the overdue check in the background, which would add notifications of its own.
api_is_up() {
  start_api Production Alerts__OverdueCheckEnabled=false
}

stop_api() {
  [ -n "${api_pid:-}" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
}

# ---- Requests to the real API.

# An address no other request of this run has used, so the limit per address stays out of the way.
# The count is in a file, because every caller runs in a subshell, where a new value would be lost.
unused_address() {
  printf x >>"$scratch/used-addresses"
  used=$(wc -c <"$scratch/used-addresses")
  printf '10.85.%s.%s' "$((used / 256))" "$((used % 256))"
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  rm -f "$scratch/body"
  curl -sS --max-time 30 -o "$scratch/body" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

as() { # as <token> <method> <path> [JSON body]
  token=$1
  method=$2
  path=$3
  if [ "$#" -ge 4 ]; then
    # From a file: Windows hands curl its arguments in the ANSI code page, where ă turns into a.
    printf '%s' "$4" >"$scratch/request.json"
    send "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
      --data-binary "@$(native "$scratch/request.json")"
  else
    send "$method" "$path" -H "Authorization: Bearer $token"
  fi
}

login() { # login <email> <password>
  send POST /api/auth/login -H 'Content-Type: application/json' -H "X-Forwarded-For: $(unused_address)" \
    -d "{\"email\":\"$1\",\"password\":\"$2\"}"
}

body_field() { # body_field <name>: the value of the first text field with that name in the last answer
  sed -n "s/^[^\"]*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body" | head -n 1
}

json() { # json <JavaScript expression on b, the last answer>: prints its value
  node -e "const b = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/body")" "$1"
}

status_is() { # status_is <expected status> <actual status>: shows the answer when it differs
  is "$1" "$2" || {
    cat "$scratch/body" 2>/dev/null
    return 1
  }
}

code_is() { # code_is <expected status> <expected code> <actual status>
  status_is "$1" "$3" || return 1
  in_file "\"code\":\"$2\"" "$scratch/body" || {
    echo "no code $2 in the answer:"
    cat "$scratch/body"
    return 1
  }
}

field_is_refused() { # field_is_refused <field> <status of the request just sent>
  code_is 400 Validation "$2" || return 1
  is true "$(json "Object.keys(b.errors).includes('$1')")"
}

# The first admin past the forced password change. The API may have stopped since the token was taken:
# it comes back first, and the token, signed with the same secret, still holds. The Admin role has
# every permission, projects.view_all among them.
admin_is_ready() {
  [ -n "${api_pid:-}" ] || api_is_up || return 1
  [ -n "${admin_token:-}" ] && return 0
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
  status_is 200 "$(as "$first_token" POST /api/auth/change-password "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  [ -n "$admin_token" ] && [ -n "$admin_id" ]
}

# A colleague in the role given, or in one without permissions, past their forced password change: their
# id goes in <name>_id and their token in <name>_token.
colleague_is_ready() { # colleague_is_ready <name> <email> <display name> [role id]
  admin_is_ready || return 1
  eval "[ -n \"\${$1_token:-}\" ]" && return 0
  role=${4:-}
  if [ -z "$role" ]; then
    if [ -z "${plain_role:-}" ]; then
      status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Fara permisiuni","permissions":[]}')" || return 1
      plain_role=$(body_field id)
    fi
    role=$plain_role
  fi
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"$2\",\"displayName\":\"$3\",\"roleId\":\"$role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  eval "$1_id=$(body_field id)"
  status_is 200 "$(login "$2" "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$member_password\"}")" || return 1
  eval "$1_token=$(body_field accessToken)"
}


cleanup() {
  stop_api
  if [ -n "$admin_connection" ]; then
    sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null
  fi
  containers=$(docker ps -aq --filter "label=$run_id" 2>/dev/null)
  [ -z "$containers" ] || docker rm -f -v $containers >/dev/null 2>&1
  docker network rm "$network" >/dev/null 2>&1
  docker rmi -f "$web_image" >/dev/null 2>&1
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ---- 19.1: nginx's headers.

# Every add_header at server level, outside a location, so a location never loses the server's
# (docs/SPEC.md §13.2). nginx-app.conf is included inside a server, so its server level is the file's own.
headers_at_server_level() { # headers_at_server_level <configuration file> <depth of the server level>
  awk -v level="$2" '
    { line = $0; sub(/#.*/, "", line) }
    line ~ /add_header/ && depth != level { print FILENAME ":" NR ": " $0; bad = 1 }
    { depth += gsub(/\{/, "{", line) - gsub(/\}/, "}", line) }
    END { exit bad }
  ' "$1"
}

app_conf_has_the_headers() {
  for header in X-Content-Type-Options X-Frame-Options Referrer-Policy Permissions-Policy Content-Security-Policy; do
    grep -q "^add_header $header .* always;\$" frontend/nginx-app.conf || {
      echo "no add_header $header ... always; in nginx-app.conf"
      return 1
    }
  done
}

hsts_only_in_the_443_server() {
  if grep -n 'Strict-Transport-Security' frontend/nginx-app.conf frontend/nginx.conf; then
    return 1
  fi
  # The server block that holds HSTS listens on 443, and the one on 80 has none.
  awk '
    /^server/ { block++ }
    /listen 443/ { tls[block] = 1 }
    /Strict-Transport-Security/ { hsts[block] = 1 }
    END {
      for (b in hsts) if (!tls[b]) { print "HSTS in a server without 443"; exit 1 }
      for (b in tls) if (!hsts[b]) { print "the 443 server has no HSTS"; exit 1 }
    }
  ' frontend/nginx.prod.conf
}

on_network() {
  docker network inspect "$network" >/dev/null 2>&1 || docker network create --label "$run_id" "$network" >/dev/null
}

# A stand-in for the API, under the name nginx looks for: it answers JSON to anything.
start_api_stub() {
  on_network || return 1
  docker ps -q --filter "name=^$run_id-stub$" | grep -q . && return 0
  docker run -d --label "$run_id" --name "$run_id-stub" --network "$network" --network-alias api node:22-alpine node -e '
    require("http").createServer((request, response) => {
      request.resume();
      request.on("end", () => {
        response.setHeader("Content-Type", "application/json");
        response.end(JSON.stringify({ url: request.url }));
      });
    }).listen(8080);
  ' >/dev/null
}

build_web_image() {
  docker build -t "$web_image" frontend >"$scratch/web-build.log" 2>&1 || {
    tail -n 25 "$scratch/web-build.log"
    return 1
  }
}

# nginx with the image's own configuration, or with another shared part mounted over it. Sets web_url.
start_web() { # start_web <name> [nginx-app.conf to use]
  start_api_stub || return 1
  mount=
  [ -z "${2:-}" ] || mount="-v $(native "$2"):/etc/nginx/kanabol-app.conf:ro"
  # shellcheck disable=SC2086 # the mount is meant to split
  docker run -d --label "$run_id" --name "$run_id-$1" --network "$network" -p 127.0.0.1::80 $mount "$web_image" >/dev/null || return 1
  web_url="http://$(docker port "$run_id-$1" 80/tcp | head -n 1)"
  curl -sS -o /dev/null --retry 15 --retry-connrefused --retry-delay 1 "$web_url/" || {
    docker logs "$run_id-$1" 2>&1 | tail -n 10
    return 1
  }
}

# The production server: nginx.prod.conf over nginx.conf, with a throwaway certificate. Sets http_port and
# https_port.
start_prod_web() {
  start_api_stub || return 1
  mkdir -p "$scratch/certs"
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=kanabol.example' \
    -keyout "$(native "$scratch/certs/server.key")" -out "$(native "$scratch/certs/server.crt")" >/dev/null 2>&1 || return 1
  docker run -d --label "$run_id" --name "$run_id-prod" --network "$network" -p 127.0.0.1::80 -p 127.0.0.1::443 \
    -v "$(native "$PWD/frontend/nginx.prod.conf"):/etc/nginx/conf.d/default.conf:ro" \
    -v "$(native "$scratch/certs"):/etc/nginx/certs:ro" "$web_image" >/dev/null || return 1
  http_port=$(docker port "$run_id-prod" 80/tcp | head -n 1 | sed 's/.*://')
  https_port=$(docker port "$run_id-prod" 443/tcp | head -n 1 | sed 's/.*://')
  curl -sSk -o /dev/null --retry 15 --retry-connrefused --retry-delay 1 "https://127.0.0.1:$https_port/" || {
    docker logs "$run_id-prod" 2>&1 | tail -n 10
    return 1
  }
}

# What an answer's headers were, one per line, without the carriage returns, the names in lower case
# (Git Bash's grep aborts on some of these lines with -i and -x together).
headers_of() { # headers_of <url> [curl arguments...]
  url=$1
  shift
  curl -sSk -o /dev/null -D - "$@" "$url" | tr -d '\r' | sed 's/^\([^: ]*\):/\L\1:/'
}

# Each header once, with the spec's value: twice would mean a location added its own on top.
has_the_headers() { # has_the_headers <url> [curl arguments...]
  headers_of "$@" >"$scratch/headers.txt" || return 1
  missing=0
  rm -f "$scratch/missing.txt"
  for expected in 'x-content-type-options: nosniff' 'x-frame-options: DENY' \
    'referrer-policy: strict-origin-when-cross-origin' \
    'permissions-policy: camera=(), microphone=(), geolocation=()' \
    "content-security-policy: $csp"; do
    count=$(grep -cF -- "${expected%%:*}:" "$scratch/headers.txt")
    if [ "$count" != 1 ] || ! grep -qxF -- "$expected" "$scratch/headers.txt"; then
      echo "wanted once: $expected" >>"$scratch/missing.txt"
      missing=1
    fi
  done
  [ "$missing" = 0 ] || {
    grep -v "^content-security-policy" "$scratch/headers.txt" | tail -n 6
    cat "$scratch/missing.txt"
    return 1
  }
}

lacks_header() { # lacks_header <name> <url> [curl arguments...]
  name=$1
  shift
  headers_of "$@" >"$scratch/headers.txt" || return 1
  if grep "^$name:" "$scratch/headers.txt"; then
    return 1
  fi
}

main_script() {
  curl -sS "$web_url/" | sed -n 's/.*src="\(main-[^"]*\.js\)".*/\1/p' | head -n 1
}

a_script_has_the_headers() {
  script=$(main_script)
  [ -n "$script" ] || {
    echo "no main script in index.html"
    return 1
  }
  has_the_headers "$web_url/$script"
}

# 31 MB is over client_max_body_size: nginx's own 413, which only always gives the headers to.
big_upload() {
  [ -f "$scratch/body.bin" ] || dd if=/dev/zero of="$scratch/body.bin" bs=1048576 count=31 2>/dev/null
}

refused_upload_has_the_headers() {
  big_upload || return 1
  is 413 "$(curl -sS -o /dev/null -w '%{http_code}' --data-binary @"$(native "$scratch/body.bin")" -H 'Expect:' "$web_url/api/attachments")" || return 1
  has_the_headers "$web_url/api/attachments" --data-binary @"$(native "$scratch/body.bin")" -H 'Expect:'
}

prod_redirects_without_hsts() {
  is "301 https://kanabol.example/projects?page=2" \
    "$(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}' -H 'Host: kanabol.example' "http://127.0.0.1:$http_port/projects?page=2")" || return 1
  lacks_header strict-transport-security "http://127.0.0.1:$http_port/projects" -H 'Host: kanabol.example'
}

prod_sends_hsts() { # prod_sends_hsts <path>
  headers_of "https://127.0.0.1:$https_port$1" >"$scratch/headers.txt" || return 1
  is 1 "$(grep -c '^strict-transport-security:' "$scratch/headers.txt")" || return 1
  grep -qxF "strict-transport-security: $hsts" "$scratch/headers.txt" || {
    cat "$scratch/headers.txt"
    return 1
  }
}

# What the CSP would block in the page: an inline script, an inline handler, a stylesheet that only a
# script would take off media="print" (Angular's critical CSS inlining does that), or anything from
# another origin.
page_obeys_the_csp() { # page_obeys_the_csp <index.html>
  node -e '
    const html = require("fs").readFileSync(process.argv[1], "utf8");
    const problems = [];
    for (const tag of html.match(/<script\b[^>]*>/g) ?? []) if (!/\ssrc=/.test(tag)) problems.push("inline script: " + tag);
    const handler = html.match(/<[^>]*\son[a-z]+\s*=[^>]*>/i);
    if (handler) problems.push("inline handler: " + handler[0]);
    for (const tag of html.match(/<link\b[^>]*>/g) ?? []) {
      if (/rel="?stylesheet/.test(tag) && /media="?print/.test(tag)) problems.push("stylesheet waiting on a script: " + tag);
      if (/href="?(https?:)?\/\//.test(tag)) problems.push("another origin: " + tag);
    }
    for (const tag of html.match(/<script\b[^>]*>/g) ?? []) if (/src="?(https?:)?\/\//.test(tag)) problems.push("another origin: " + tag);
    if (!/<script[^>]*src="main-/.test(html)) problems.push("no main script");
    if (!/<link[^>]*rel="?stylesheet[^>]*href="?styles-/.test(html)) problems.push("no stylesheet");
    if (problems.length) { console.log(problems.join("\n")); process.exit(1); }
  ' "$(native "$1")"
}

built_page_obeys_the_csp() {
  curl -sS "$web_url/" >"$scratch/index.html" || return 1
  page_obeys_the_csp "$scratch/index.html"
}

styles_come_from_here() {
  docker run --rm --entrypoint sh "$web_image" -c 'cat /usr/share/nginx/html/*.css' >"$scratch/styles.css" || return 1
  grep -q 'woff2' "$scratch/styles.css" || {
    echo "no font in the stylesheet"
    return 1
  }
  if grep -oE 'url\((["'"'"']?)(https?:)?//[^)]*\)|@import[^;]*' "$scratch/styles.css"; then
    return 1
  fi
}

# Controls, each against a copy that does what the checks above must catch.
location_header_control() {
  sed 's|^location /api/ {|location /api/ {\n  add_header X-Probe 1;|' frontend/nginx-app.conf >"$scratch/location-header.conf"
  grep -q 'X-Probe' "$scratch/location-header.conf" || return 1
  ! headers_at_server_level "$scratch/location-header.conf" 0 >/dev/null || return 1
  start_web location-header "$scratch/location-header.conf" || return 1
  ! has_the_headers "$web_url/api/orders" >/dev/null
}

without_always_control() {
  sed 's/ always;$/;/' frontend/nginx-app.conf >"$scratch/without-always.conf"
  start_web without-always "$scratch/without-always.conf" || return 1
  has_the_headers "$web_url/" >/dev/null || return 1
  big_upload || return 1
  ! has_the_headers "$web_url/api/attachments" --data-binary @"$(native "$scratch/body.bin")" -H 'Expect:' >/dev/null
}

inline_script_control() {
  sed -e 's|<link rel="stylesheet" href="\(styles-[^"]*\)"[^>]*>|<link rel="stylesheet" href="\1" media="print" data-beasties-media="all">|' \
    -e "s|</body>|<script>document.querySelectorAll('link[data-beasties-media]').forEach(function(l){l.media='all'})</script></body>|" \
    "$scratch/index.html" >"$scratch/inline-script.html"
  ! page_obeys_the_csp "$scratch/inline-script.html" >/dev/null
}

task_1() {
  echo "19.1 nginx's headers, the CSP and HSTS"
  check "Docker is running" docker_is_running
  check "nginx-app.conf, nginx.conf and nginx.prod.conf are tracked" \
    sh -c 'for f in frontend/nginx-app.conf frontend/nginx.conf frontend/nginx.prod.conf; do git ls-files --error-unmatch "$f" >/dev/null || exit 1; done'
  check "nginx-app.conf sets the five headers, always" app_conf_has_the_headers
  check "every add_header in nginx-app.conf is at server level" headers_at_server_level frontend/nginx-app.conf 0
  check "every add_header in nginx.prod.conf is at server level" headers_at_server_level frontend/nginx.prod.conf 1
  check "HSTS is only in nginx.prod.conf, in the server on 443" hsts_only_in_the_443_server
  check "the image builds" build_web_image
  start_web web >"$scratch/start-web.log" 2>&1 || cat "$scratch/start-web.log"
  check "the app has the headers, once each" has_the_headers "$web_url/"
  check "a deep link has them" has_the_headers "$web_url/projects/KAN/board"
  check "a script has them" a_script_has_the_headers
  check "the manifest, in a location of its own, has them" has_the_headers "$web_url/manifest.webmanifest"
  check "what the API answers through nginx has them" has_the_headers "$web_url/api/orders?page=1"
  check "nginx's own 413 has them" refused_upload_has_the_headers
  check "the development server sends no HSTS" lacks_header strict-transport-security "$web_url/"
  check "the built page has nothing the CSP blocks" built_page_obeys_the_csp
  check "the styles and fonts come from this origin" styles_come_from_here
  start_prod_web >"$scratch/start-prod.log" 2>&1 || cat "$scratch/start-prod.log"
  check "prod sends port 80 to HTTPS, without HSTS" prod_redirects_without_hsts
  check "prod sends HSTS on 443, a year, without preload" prod_sends_hsts /
  check "and on what the API answers" prod_sends_hsts /api/orders
  check "prod has the five headers on 443" has_the_headers "https://127.0.0.1:$https_port/"
  check "and on what the API answers" has_the_headers "https://127.0.0.1:$https_port/api/orders"
  check "control: an add_header in a location takes the others off it" location_header_control
  check "control: without always, nginx's 413 has none" without_always_control
  check "control: the build's inline script would be blocked" inline_script_control
}


# ---- A SignalR client as a browser is one: the JSON protocol over a WebSocket, the token only in the
# query string, as check-realtime.sh's, which also joins, leaves and notices when the server closes it.
# Built once, on first use.

hub_client() {
  [ -f "$scratch/hub.mjs" ] && return 0
  cat >"$scratch/hub.mjs" <<'JS'
// node hub.mjs <API address> <steps file>: runs the steps and writes what happened as JSON. A step connects
// a name with a token; joins or leaves a project on it, giving the hub's answer ("null" for a join, or the
// refusal's code, or "closed" when the connection closed first); sends a request to the API; waits until a
// name heard so many messages of an event; waits to see whether the server closes a connection; or waits.
// What each connection heard comes last.
import { readFileSync } from 'node:fs';

const RS = '\x1e';
const [base, stepsFile] = process.argv.slice(2);
const steps = JSON.parse(readFileSync(stepsFile, 'utf8'));
const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function connect(token) {
  const query = `access_token=${encodeURIComponent(token)}`;
  const negotiated = await fetch(`${base}/hubs/board/negotiate?negotiateVersion=1&${query}`, { method: 'POST' });
  if (negotiated.status !== 200) throw new Error(`negotiate answered ${negotiated.status}`);
  const { connectionToken } = await negotiated.json();
  const socket = new WebSocket(`${base.replace(/^http/, 'ws')}/hubs/board?id=${encodeURIComponent(connectionToken)}&${query}`);
  const hub = { heard: [], closed: false, pending: new Map(), next: 1 };
  let shake;
  const shaken = new Promise((resolve, reject) => (shake = { resolve, reject }));
  socket.onmessage = (event) => {
    for (const frame of String(event.data).split(RS).filter(Boolean)) {
      const message = JSON.parse(frame);
      if (!('type' in message)) {
        if (message.error) shake.reject(new Error(message.error));
        else shake.resolve();
      } else if (message.type === 1) {
        hub.heard.push({ event: message.target, payload: message.arguments[0] });
      } else if (message.type === 3) {
        hub.pending.get(message.invocationId)?.(message.error ? `error: ${message.error}` : String(message.result ?? null));
        hub.pending.delete(message.invocationId);
      }
    }
  };
  socket.onclose = () => {
    hub.closed = true;
    for (const answer of hub.pending.values()) answer('closed');
    hub.pending.clear();
  };
  await new Promise((resolve, reject) => {
    socket.onopen = resolve;
    socket.onerror = () => reject(new Error('the WebSocket failed'));
  });
  socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS);
  await shaken;
  hub.invoke = (target, args) => {
    if (hub.closed) return Promise.resolve('closed');
    const id = String(hub.next++);
    const answered = new Promise((resolve) => hub.pending.set(id, resolve));
    socket.send(JSON.stringify({ type: 1, invocationId: id, target, arguments: args }) + RS);
    return Promise.race([answered, pause(10000).then(() => 'no answer')]);
  };
  hub.close = () => socket.close();
  // The server handles calls once it has put the connection in its user's group: one call that joins
  // nothing makes sure of it.
  await hub.invoke('LeaveProject', ['00000000-0000-0000-0000-000000000000']);
  return hub;
}

const hubs = {};
const result = { steps: [], heard: {} };
try {
  for (const step of steps) {
    if (step.connect) {
      try {
        hubs[step.connect] = await connect(step.token);
        result.steps.push('connected');
      } catch (error) {
        result.steps.push(`refused: ${error.message}`);
      }
    } else if (step.join) {
      result.steps.push(await hubs[step.join].invoke('JoinProject', [step.project]));
    } else if (step.leave) {
      const answer = await hubs[step.leave].invoke('LeaveProject', [step.project]);
      result.steps.push(answer === 'null' ? 'left' : answer);
    } else if (step.request) {
      const answer = await fetch(`${base}${step.request}`, {
        method: step.method,
        headers: { Authorization: `Bearer ${step.token}`, 'Content-Type': 'application/json' },
        body: step.body === undefined ? undefined : JSON.stringify(step.body),
      });
      result.steps.push(String(answer.status));
    } else if (step.until) {
      const started = Date.now();
      const count = () => hubs[step.until].heard.filter((message) => message.event === step.event).length;
      while (count() < step.count && Date.now() - started < step.timeout) await pause(100);
      result.steps.push(count() >= step.count ? 'heard' : `heard ${count()} of ${step.count}`);
    } else if (step.closed) {
      const started = Date.now();
      while (!hubs[step.closed].closed && Date.now() - started < step.timeout) await pause(50);
      result.steps.push(hubs[step.closed].closed ? 'closed' : 'open');
    } else if (step.wait) {
      await pause(step.wait);
    }
  }
} finally {
  for (const [name, hub] of Object.entries(hubs)) {
    result.heard[name] = hub.heard;
    hub.close();
  }
}
console.log(JSON.stringify(result));
JS
}

hub() { # hub <steps as JSON>: runs them, and leaves what happened in the scratch folder
  hub_client || return 1
  printf '%s' "$1" >"$scratch/hub-steps.json"
  rm -f "$scratch/hub-result.json"
  node "$(native "$scratch/hub.mjs")" "$API_URL" "$(native "$scratch/hub-steps.json")" >"$scratch/hub-result.json" 2>"$scratch/hub-error.log" || {
    cat "$scratch/hub-error.log"
    return 1
  }
}

heard() { # heard <JavaScript expression on r, the last hub run>: prints its value
  node -e "const r = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/hub-result.json")" "$1"
}

steps_were() { # steps_were <expected steps, separated by |>
  is "$1" "$(heard "r.steps.join('|')")" || {
    cat "$scratch/hub-result.json"
    return 1
  }
}

# The API's log in Production is compact JSON: an entry with this template, holding this text.
api_logged() { # api_logged <message template> <text in the same entry>
  grep -F "\"@mt\":\"$1\"" "$api_log" | grep -qF -- "$2" || {
    echo "no entry \"$1\" with $2 in the API's log"
    return 1
  }
}

# ---- 19.2: the hub's limits, and who stops hearing what.

hub_limit_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    HubLimitsTests.JoinProject_EightProjects_JoinsEach_AndTheNinthAnswersTooManyProjects \
    HubLimitsTests.JoinProject_PastTheLimit_AnswersBeforeReadingTheDatabase \
    HubLimitsTests.JoinProject_TheSameProjectAgain_TakesNoOtherPlace \
    HubLimitsTests.LeaveProject_FreesItsPlace_ForAnotherProject \
    HubLimitsTests.EachConnection_HasEightPlacesOfItsOwn \
    HubLimitsTests.JoinProject_FourRefusals_KeepTheConnection_AndTheFifthClosesIt \
    HubLimitsTests.JoinProject_RefusedForTooManyProjects_CountsTowardsClosing \
    HubLimitsTests.Connection_WhenItCloses_IsForgotten \
    RealtimeAccessTests.RemoveMember_TakesEveryConnectionOfTheirsOutOfTheProject_AndTheOthersKeepHearing \
    RealtimeAccessTests.RemoveMember_WhoSeesEveryProject_KeepsHearingIt \
    RealtimeAccessTests.ChangeUserRole_ClosesEveryConnectionOfTheirs_AndNoOneElses \
    RealtimeAccessTests.DeactivateUser_ClosesTheirConnections_AndTheTokenTheyStillHoldJoinsNoProject
}

board_hub_tests_pass() {
  tests_pass Kanabol.Api.Tests "FullyQualifiedName~Kanabol.Api.Tests.Realtime.BoardHubTests" 19
}

realtime_handler_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    ProjectMemberCommandHandlerTests.Remove_ByAnOwner_TakesTheirConnectionsOutOfTheProject \
    ProjectMemberCommandHandlerTests.Remove_OfSomeoneWhoSeesEveryProject_LeavesThemOnItsBoard \
    ProjectMemberCommandHandlerTests.Remove_WhenRefused_TakesNobodyOut \
    ChangeUserRoleCommandHandlerTests.Handle_WhenAllowed_ClosesOnlyThatUsersConnections \
    ChangeUserRoleCommandHandlerTests.Handle_WhenRefused_ClosesNoConnection \
    ChangeUserRoleCommandHandlerTests.Handle_WhenTheRoleStaysTheSame_KeepsTheSessions \
    DeactivateUserCommandHandlerTests.Handle_WhenAllowed_ClosesOnlyThatUsersConnections \
    DeactivateUserCommandHandlerTests.Handle_WhenRefused_ClosesNoConnection \
    DeactivateUserCommandHandlerTests.Handle_WhenAlreadyDeactivated_SucceedsAgain
}

# Nine projects of the admin's, made through the API, their ids in nine_projects.
nine_projects_are_ready() {
  admin_is_ready || return 1
  [ -n "${nine_projects:-}" ] && return 0
  for key in PA PB PC PD PE PF PG PH PI; do
    status_is 201 "$(as "$admin_token" POST /api/projects "{\"name\":\"Proiectul $key\",\"key\":\"$key\"}")" || return 1
    nine_projects="${nine_projects:-} $(body_field id)"
  done
}

joins_of() { # joins_of <connection name> <project ids...>: the steps that join each
  name=$1
  shift
  for id in "$@"; do
    printf ',{"join":"%s","project":"%s"}' "$name" "$id"
  done
}

ninth_project_is_refused() {
  nine_projects_are_ready || return 1
  # shellcheck disable=SC2086 # the ids are meant to split
  set -- $nine_projects
  hub "[{\"connect\":\"admin\",\"token\":\"$admin_token\"}$(joins_of admin "$@")]" || return 1
  steps_were 'connected|null|null|null|null|null|null|null|null|Hub.TooManyProjects'
}

leaving_frees_a_place() {
  nine_projects_are_ready || return 1
  # shellcheck disable=SC2086
  set -- $nine_projects
  first=$1
  ninth=$9
  hub "[{\"connect\":\"admin\",\"token\":\"$admin_token\"}$(joins_of admin "$1" "$2" "$3" "$4" "$5" "$6" "$7" "$8"),
    {\"join\":\"admin\",\"project\":\"$ninth\"},{\"leave\":\"admin\",\"project\":\"$first\"},{\"join\":\"admin\",\"project\":\"$ninth\"}]" || return 1
  steps_were 'connected|null|null|null|null|null|null|null|null|Hub.TooManyProjects|left|null'
}

# Eva sees none of the admin's projects: four refusals keep her connection, the fifth closes it.
fifth_refusal_closes_the_connection() {
  nine_projects_are_ready || return 1
  colleague_is_ready eva eva@example.com 'Eva Marin' || return 1
  # shellcheck disable=SC2086
  set -- $nine_projects
  hub "[{\"connect\":\"eva\",\"token\":\"$eva_token\"}$(joins_of eva "$1" "$2" "$3" "$4"),{\"closed\":\"eva\",\"timeout\":1000},
    {\"join\":\"eva\",\"project\":\"$5\"},{\"closed\":\"eva\",\"timeout\":5000}]" || return 1
  case "$(heard "r.steps.join('|')")" in
    'connected|Project.MemberRequired|Project.MemberRequired|Project.MemberRequired|Project.MemberRequired|open|'*'|closed') ;;
    *)
      cat "$scratch/hub-result.json"
      return 1
      ;;
  esac
  api_logged 'Closed {ConnectionId} of user {UserId} after {RefusalCount} refused joins' "\"RefusalCount\":5"
}

# PAN is the admin's, with Dan and Eva. Both hear a task created while members; once Dan is out, Eva hears
# the next one and Dan doesn't.
removed_member_hears_no_more() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready eva eva@example.com 'Eva Marin' || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Panou","key":"PAN"}')" || return 1
  pan=$(body_field id)
  for member in "$dan_id" "$eva_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$pan/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  task='{"title":"Un task nou","description":"","priority":"Medium"}'
  hub "[{\"connect\":\"dan\",\"token\":\"$dan_token\"},{\"connect\":\"eva\",\"token\":\"$eva_token\"},
    {\"join\":\"dan\",\"project\":\"$pan\"},{\"join\":\"eva\",\"project\":\"$pan\"},
    {\"request\":\"/api/projects/$pan/tasks\",\"method\":\"POST\",\"token\":\"$admin_token\",\"body\":$task},
    {\"until\":\"dan\",\"event\":\"TaskCreated\",\"count\":1,\"timeout\":10000},{\"until\":\"eva\",\"event\":\"TaskCreated\",\"count\":1,\"timeout\":10000},
    {\"request\":\"/api/projects/$pan/members/$dan_id\",\"method\":\"DELETE\",\"token\":\"$admin_token\"},
    {\"request\":\"/api/projects/$pan/tasks\",\"method\":\"POST\",\"token\":\"$admin_token\",\"body\":$task},
    {\"until\":\"eva\",\"event\":\"TaskCreated\",\"count\":2,\"timeout\":10000},{\"wait\":1500},{\"closed\":\"dan\",\"timeout\":10}]" || return 1
  steps_were 'connected|connected|null|null|201|heard|heard|204|201|heard|open' || return 1
  is 1 "$(heard "r.heard.dan.filter((m) => m.event === 'TaskCreated').length")" || return 1
  api_logged 'User {UserId} taken out of the board of project {ProjectId} on {ConnectionCount} connections' "\"ConnectionCount\":1"
}

role_change_closes_their_connections() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready eva eva@example.com 'Eva Marin' || return 1
  status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Analist","permissions":[]}')" || return 1
  analyst=$(body_field id)
  hub "[{\"connect\":\"dan\",\"token\":\"$dan_token\"},{\"connect\":\"dan2\",\"token\":\"$dan_token\"},{\"connect\":\"eva\",\"token\":\"$eva_token\"},
    {\"request\":\"/api/users/$dan_id/role\",\"method\":\"PUT\",\"token\":\"$admin_token\",\"body\":{\"roleId\":\"$analyst\"}},
    {\"closed\":\"dan\",\"timeout\":5000},{\"closed\":\"dan2\",\"timeout\":5000},{\"closed\":\"eva\",\"timeout\":1000}]" || return 1
  steps_were 'connected|connected|connected|200|closed|closed|open' || return 1
  api_logged 'Closed the {ConnectionCount} connections of user {UserId} to the board hub' "\"ConnectionCount\":2"
}

# Ion is a member of the first of the nine projects: deactivated, his connection closes, and the token he
# still holds connects again but joins nothing.
deactivation_closes_their_connections() {
  nine_projects_are_ready || return 1
  colleague_is_ready ion ion@example.com 'Ion Pop' || return 1
  # shellcheck disable=SC2086
  set -- $nine_projects
  status_is 201 "$(as "$admin_token" POST "/api/projects/$1/members" "{\"userId\":\"$ion_id\",\"role\":\"Member\"}")" || return 1
  hub "[{\"connect\":\"ion\",\"token\":\"$ion_token\"},{\"join\":\"ion\",\"project\":\"$1\"},
    {\"request\":\"/api/users/$ion_id/deactivate\",\"method\":\"POST\",\"token\":\"$admin_token\"},{\"closed\":\"ion\",\"timeout\":5000},
    {\"connect\":\"again\",\"token\":\"$ion_token\"},{\"join\":\"again\",\"project\":\"$1\"}]" || return 1
  steps_were 'connected|null|200|closed|connected|Project.MemberRequired'
}

task_2() {
  echo "19.2 the hub's limits, and who stops hearing what"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the hub's limits and the closed connections pass their tests" hub_limit_tests_pass
  check "the board hub's tests still pass" board_hub_tests_pass
  check "the handlers call IRealtimeAccess, and only when they change something" realtime_handler_tests_pass
  check "the real API starts in Production on a database of this run" api_is_up
  check "eight projects join on one connection, and the ninth answers Hub.TooManyProjects" ninth_project_is_refused
  check "leaving one frees its place for the ninth" leaving_frees_a_place
  check "four refusals keep a connection, the fifth closes it, with a warning in the log" fifth_refusal_closes_the_connection
  check "a member taken out of a project hears nothing more of it, and the others do" removed_member_hears_no_more
  check "a change of role closes that person's connections, and nobody else's" role_change_closes_their_connections
  check "a deactivation closes them, and the token still held joins no project" deactivation_closes_their_connections
}


# ---- 19.3: the hub's sends go through a channel, and the logs hide tokens in structured values.

# A board that joins its project and then stops reading its socket, so the hub's writes to it pile up
# and block, as a frozen tab's would. It reads again at the end, to see whether the server closed it.
stalled_client() {
  [ -f "$scratch/stalled.mjs" ] && return 0
  cat >"$scratch/stalled.mjs" <<'JS'
// node stalled.mjs <API address> <token> <project id> <seconds>: prints "joined" once it stops reading,
// then, after the seconds, "closed" if the server closed the connection meanwhile, or "open".
import net from 'node:net';
import { randomBytes } from 'node:crypto';

const [base, token, projectId, seconds] = process.argv.slice(2);
const { hostname, port } = new URL(base);
const RS = '\x1e';
const query = `access_token=${encodeURIComponent(token)}`;
const negotiated = await fetch(`${base}/hubs/board/negotiate?negotiateVersion=1&${query}`, { method: 'POST' });
const { connectionToken } = await negotiated.json();

const socket = net.connect(Number(port), hostname);
await new Promise((resolve) => socket.once('connect', resolve));
let buffer = Buffer.alloc(0);
let ended = false;
socket.on('data', (chunk) => (buffer = Buffer.concat([buffer, chunk])));
socket.on('end', () => (ended = true));
socket.on('close', () => (ended = true));
socket.on('error', () => (ended = true));

const until = async (test) => {
  for (let i = 0; i < 200 && !test(); i++) await new Promise((resolve) => setTimeout(resolve, 25));
  if (!test()) throw new Error('no answer');
};

function frame(text) { // a masked text frame, as a client sends
  const payload = Buffer.from(text);
  const mask = randomBytes(4);
  const header = payload.length < 126
    ? Buffer.from([0x81, 0x80 | payload.length])
    : Buffer.from([0x81, 0x80 | 126, payload.length >> 8, payload.length & 0xff]);
  return Buffer.concat([header, mask, payload.map((byte, i) => byte ^ mask[i % 4])]);
}

socket.write(`GET /hubs/board?id=${encodeURIComponent(connectionToken)}&${query} HTTP/1.1\r\nHost: ${hostname}:${port}\r\n` +
  `Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: ${randomBytes(16).toString('base64')}\r\nSec-WebSocket-Version: 13\r\n\r\n`);
await until(() => buffer.includes('\r\n\r\n'));
if (!buffer.toString().startsWith('HTTP/1.1 101')) throw new Error(buffer.toString().split('\r\n')[0]);
buffer = Buffer.alloc(0);
socket.write(frame(JSON.stringify({ protocol: 'json', version: 1 }) + RS));
await until(() => buffer.includes(`{}${RS}`));
socket.write(frame(JSON.stringify({ type: 1, invocationId: '1', target: 'JoinProject', arguments: [projectId] }) + RS));
await until(() => buffer.includes('"invocationId":"1"'));
if (!buffer.includes('"result":null')) throw new Error(`not joined: ${buffer}`);

socket.pause();
console.log('joined');
await new Promise((resolve) => setTimeout(resolve, Number(seconds) * 1000));
// Whatever the server wrote before closing is read first, then the end.
socket.resume();
for (let i = 0; i < 100 && !ended; i++) await new Promise((resolve) => setTimeout(resolve, 50));
console.log(ended ? 'closed' : 'open');
socket.destroy();
JS
}

# Requests from several workers at once, each moving its own task between two columns for the seconds
# given: prints how many went through, their statuses and the slowest, in milliseconds.
mover() {
  [ -f "$scratch/mover.mjs" ] && return 0
  cat >"$scratch/mover.mjs" <<'JS'
// node mover.mjs <API address> <token> <seconds> <task:column:column...>
const [base, token, seconds, ...moves] = process.argv.slice(2);
const end = Date.now() + Number(seconds) * 1000;
const times = [];
const statuses = {};
await Promise.all(moves.map(async (move) => {
  const [task, ...columns] = move.split(':');
  for (let i = 1; Date.now() < end; i++) {
    const started = Date.now();
    const answer = await fetch(`${base}/api/tasks/${task}/position`, {
      method: 'PATCH',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ columnId: columns[i % 2] }),
    });
    await answer.arrayBuffer();
    times.push(Date.now() - started);
    statuses[answer.status] = (statuses[answer.status] ?? 0) + 1;
  }
}));
console.log(JSON.stringify({ count: times.length, slowest: Math.max(...times), statuses }));
JS
}

# MOV is the admin's, with 20 tasks to move. A board stops reading while the admin moves them for 40
# seconds: the hub's writes to it block, and the server closes it once its send timeout passes, which
# proves the block was real. No request waits for it meanwhile.
requests_dont_wait_for_a_stalled_board() {
  admin_is_ready || return 1
  stalled_client && mover || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Mutari","key":"MOV"}')" || return 1
  mov=$(body_field id)
  status_is 200 "$(as "$admin_token" GET "/api/projects/$mov/board")" || return 1
  first_column=$(json 'b.columns[0].id')
  # The tasks go between Backlog and a column of the project's own right after it, both before În lucru,
  # so they move both ways and nothing is asked of them (group 22).
  status_is 201 "$(as "$admin_token" POST "/api/projects/$mov/board-columns" '{"name":"Alături"}')" || return 1
  second_column=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$mov/board-columns/$second_column/position" '{"position":1}')" || return 1
  moves=
  for i in $(seq 1 20); do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$mov/tasks" "{\"title\":\"Mutare $i\",\"description\":\"\",\"priority\":\"Low\"}")" || return 1
    moves="$moves $(body_field id):$first_column:$second_column"
  done
  node "$(native "$scratch/stalled.mjs")" "$API_URL" "$admin_token" "$mov" 50 >"$scratch/stalled.txt" 2>&1 &
  stalled_pid=$!
  attempts=0
  until grep -q joined "$scratch/stalled.txt" 2>/dev/null; do
    attempts=$((attempts + 1))
    [ "$attempts" -le 100 ] && kill -0 "$stalled_pid" 2>/dev/null || {
      cat "$scratch/stalled.txt"
      return 1
    }
    sleep 0.1
  done
  # shellcheck disable=SC2086 # the moves are meant to split
  node "$(native "$scratch/mover.mjs")" "$API_URL" "$admin_token" 40 $moves >"$scratch/moves.json" || return 1
  wait "$stalled_pid"
  cat "$scratch/moves.json" "$scratch/stalled.txt"
  grep -qx closed "$scratch/stalled.txt" || {
    echo "the stalled board was never closed: its writes never blocked, which proves nothing"
    return 1
  }
  [ "$(node -e "console.log(JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).slowest)" "$(native "$scratch/moves.json")")" -lt 3000 ] || {
    echo "a request waited 3 seconds or more"
    return 1
  }
}

sender_tests_pass() {
  named_tests_pass Kanabol.Api.Tests     BoardSenderTests.Notify_WhileTheHubHangs_ReturnsAtOnce     BoardSenderTests.Sender_SendsToTheRightGroups_InTheOrderTheyCame     BoardSenderTests.Sender_WhileTheHubHangs_KeepsTheRestInOrder_AndSendsThemOnceItFrees     BoardSenderTests.Sender_WhenASendFails_LogsItAndSendsTheNext     BoardSenderTests.Stop_SendsWhatIsLeft_BeforeItReturns     BoardSenderTests.Stop_WhileTheHubHangs_GivesUpAfterFiveSeconds_AndSaysHowManyWereLeft     BoardSenderTests.Notify_AfterStop_IsDropped
}

# Every test that listens to the hub, now that the sends come after the request.
realtime_flow_tests_pass() {
  tests_pass Kanabol.Api.Tests "FullyQualifiedName~Kanabol.Api.Tests.Realtime|FullyQualifiedName~FlowTests" 120
}

# ORD is the admin's, with a task made before the board opens. The board hears five changes in the order
# the requests made them.
board_hears_in_order() {
  admin_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Ordine","key":"ORD"}')" || return 1
  ord=$(body_field id)
  status_is 200 "$(as "$admin_token" GET "/api/projects/$ord/board")" || return 1
  last_column=$(json 'b.columns[b.columns.length - 1].id')
  status_is 201 "$(as "$admin_token" POST "/api/projects/$ord/tasks" '{"title":"Primul","description":"","priority":"Low"}')" || return 1
  task=$(body_field id)
  hub "[{\"connect\":\"board\",\"token\":\"$admin_token\"},{\"join\":\"board\",\"project\":\"$ord\"},
    {\"request\":\"/api/projects/$ord/tasks\",\"method\":\"POST\",\"token\":\"$admin_token\",\"body\":{\"title\":\"Al doilea\",\"description\":\"\",\"priority\":\"Low\"}},
    {\"request\":\"/api/tasks/$task/position\",\"method\":\"PATCH\",\"token\":\"$admin_token\",\"body\":{\"columnId\":\"$last_column\"}},
    {\"request\":\"/api/tasks/$task/title\",\"method\":\"PUT\",\"token\":\"$admin_token\",\"body\":{\"title\":\"Primul, redenumit\"}},
    {\"request\":\"/api/tasks/$task\",\"method\":\"DELETE\",\"token\":\"$admin_token\"},
    {\"request\":\"/api/tasks/$task/restore\",\"method\":\"POST\",\"token\":\"$admin_token\"},
    {\"until\":\"board\",\"event\":\"TaskRestored\",\"count\":1,\"timeout\":10000},{\"wait\":500}]" || return 1
  steps_were 'connected|null|201|200|200|204|200|heard' || return 1
  is 'TaskCreated TaskMoved TaskUpdated TaskArchived TaskRestored' "$(heard "r.heard.board.map((m) => m.event).join(' ')")"
}

redaction_tests_pass() {
  stop_api
  dotnet build backend/tests/Kanabol.Api.Tests/Kanabol.Api.Tests.csproj --nologo -v q >/dev/null 2>&1 || return 1
  for name in Enrich_OfAListHoldingAToken_HidesItAndKeepsTheOtherItems     Enrich_OfAnObjectHoldingAToken_HidesIt_AndKeepsItsTypeAndOtherProperties     Enrich_OfADictionaryHoldingAToken_HidesItInItsValuesAndKeys     Enrich_OfAUriHoldingAToken_WritesItAsTextWithoutTheToken     Enrich_OfAUriWithoutAToken_KeepsTheUri     Enrich_OfATokenDeepInside_HidesItAtAnyDepth     Enrich_OfStructuredValuesWithoutAToken_KeepsTheSameValues; do
    test_exists Kanabol.Api.Tests "AccessTokenRedactionTests.$name" || return 1
  done
  test_exists Kanabol.Api.Tests ApiLogRedactionTests.Log_OfStructuredValuesHoldingAToken_ReachesTheSinkWithoutIt || return 1
  tests_pass Kanabol.Api.Tests "FullyQualifiedName~AccessTokenRedactionTests|FullyQualifiedName~ApiLogRedactionTests" 11
}

# Development writes ASP.NET's "Request starting" with the whole address, query included: a board's
# negotiation and WebSocket carry the token there, and the log keeps neither.
real_log_hides_the_token() {
  admin_is_ready || return 1
  start_api Development Alerts__OverdueCheckEnabled=false || return 1
  hub "[{\"connect\":\"board\",\"token\":\"$admin_token\"},{\"wait\":500}]" || return 1
  steps_were 'connected' || return 1
  sleep 1
  token_part=$(printf '%s' "$admin_token" | cut -d. -f3)
  if grep -qF -- "$token_part" "$api_log"; then
    grep -F -- "$token_part" "$api_log" | head -n 3 | cut -c1-300
    echo "the token is in the API's log"
    return 1
  fi
  grep -F '/hubs/board' "$api_log" | grep -qF 'access_token=[hidden]' || {
    echo "no hub request with access_token=[hidden] in the log, so it proves nothing:"
    grep -F '/hubs/board' "$api_log" | head -n 5
    return 1
  }
  api_is_up
}

task_3() {
  echo "19.3 the hub's sends through a channel, and tokens hidden in structured values"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the sends pass their tests: queued, in order, a failure logged, 5 seconds at stop" sender_tests_pass
  check "every test listening to the hub still passes" realtime_flow_tests_pass
  check "the real API starts in Production on a database of this run" api_is_up
  check "a board hears the changes in the order they were made" board_hears_in_order
  check "no request waits for a board that stopped reading, which the server closes" requests_dont_wait_for_a_stalled_board
  check "the redaction passes its tests: lists, objects, dictionaries, a Uri, at any depth, and the API's own log" redaction_tests_pass
  check "the real API in Development logs a board's address without its token" real_log_hides_the_token
}

# ---- 19.4: the refresh tokens' cleanup.

cleanup_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    DeleteOldRefreshTokensCommandHandlerTests.Handle_DeletesWhatExpiredOrWasRevokedOverThirtyDaysAgo_AndKeepsTheRest \
    DeleteOldRefreshTokensCommandHandlerTests.Handle_WithNothingOld_DeletesNothing \
    DeleteOldRefreshTokensCommandHandlerTests.Handle_IsOneDeleteInTheDatabase_AndLogsHowManyWent \
    RefreshTokensCleanupWorkerTests.Start_RunsTheCleanupAtOnce \
    RefreshTokensCleanupWorkerTests.Start_ThenRunsItEveryIntervalEachInAScopeOfItsOwn \
    RefreshTokensCleanupWorkerTests.Run_ThatThrows_IsLoggedAndTheNextOneStillComes \
    RefreshTokensCleanupWorkerTests.Run_ThatFails_IsLoggedWithItsCode \
    RefreshTokensCleanupWorkerTests.Start_WhenTurnedOff_RunsNothing \
    RefreshTokensCleanupWorkerTests.Stop_WhileItWaits_EndsTheWorker \
    AuthOptionsTests.Cleanup_WithoutSettings_RunsOnceADay \
    AuthOptionsTests.Cleanup_FromTheEnvironment_TakesTheIntervalAndTheSwitch \
    AuthOptionsTests.Cleanup_WithAnIntervalOfAnHour_IsAccepted \
    AuthOptionsTests.Cleanup_WithAnIntervalUnderAnHour_StopsTheStart \
    AuthOptionsTests.Cleanup_WithAnIntervalOverThirtyDays_StopsTheStart \
    AuthOptionsTests.Worker_StartsAfterTheDatabaseIsReady
}

api_tests_turn_the_cleanup_off() {
  named_tests_pass Kanabol.Api.Tests ApiFactoryTests.RefreshTokenCleanup_InTheseTests_IsOffWithTheDefaultInterval
}

# Rows of the admin's, made straight in the database while the API is stopped, each named by what it is:
# the old ones go at the next start, the others stay.
old_rows_are_in_the_database() {
  admin_is_ready || return 1
  stop_api
  in_db "DELETE FROM refresh_tokens WHERE token_hash LIKE 'check-%'" >/dev/null
  in_db "INSERT INTO refresh_tokens (id, user_id, family_id, token_hash, expires_at, created_at, revoked_at)
    SELECT gen_random_uuid(), id, gen_random_uuid(), row.hash, now() + row.expires, now() - interval '60 days', now() - row.revoked
    FROM users, (VALUES
      ('check-live', interval '3 days', NULL::interval),
      ('check-expired-29-days-ago', interval '-29 days', NULL),
      ('check-expired-31-days-ago', interval '-31 days', NULL),
      ('check-revoked-29-days-ago', interval '3 days', interval '29 days'),
      ('check-revoked-31-days-ago', interval '3 days', interval '31 days'),
      ('check-both-long-ago', interval '-40 days', interval '45 days')) AS row(hash, expires, revoked)
    WHERE email = '$admin_email'" >/dev/null || return 1
  is 6 "$(in_db "SELECT count(*) FROM refresh_tokens WHERE token_hash LIKE 'check-%'")"
}

rows_left() {
  in_db "SELECT string_agg(token_hash, ' ' ORDER BY token_hash) FROM refresh_tokens WHERE token_hash LIKE 'check-%'"
}

# The log's line of the run at start, which may come a moment after /health answers.
cleanup_logged() { # cleanup_logged <deleted count>
  attempts=0
  until api_logged 'Deleted {DeletedCount} refresh tokens expired or revoked over {KeptDays} days ago' "\"DeletedCount\":$1" >/dev/null; do
    attempts=$((attempts + 1))
    [ "$attempts" -le 40 ] || {
      api_logged 'Deleted {DeletedCount} refresh tokens expired or revoked over {KeptDays} days ago' "\"DeletedCount\":$1"
      return 1
    }
    sleep 0.25
  done
}

cleanup_turned_off_keeps_them() {
  old_rows_are_in_the_database || return 1
  start_api Production Alerts__OverdueCheckEnabled=false Auth__RefreshTokenCleanupEnabled=false || return 1
  sleep 2
  grep -qF "The refresh tokens' cleanup is turned off" "$api_log" || {
    echo "no line saying the cleanup is off"
    return 1
  }
  is 'check-both-long-ago check-expired-29-days-ago check-expired-31-days-ago check-live check-revoked-29-days-ago check-revoked-31-days-ago' "$(rows_left)"
}

cleanup_at_start_deletes_the_old_ones() {
  old_rows_are_in_the_database || return 1
  api_is_up || return 1
  cleanup_logged 3 || return 1
  is 'check-expired-29-days-ago check-live check-revoked-29-days-ago' "$(rows_left)"
}

# The admin's own sessions, days from their end, are still there.
live_sessions_stay() {
  [ "$(in_db "SELECT count(*) FROM refresh_tokens r JOIN users u ON u.id = r.user_id
    WHERE u.email = '$admin_email' AND r.token_hash NOT LIKE 'check-%' AND r.revoked_at IS NULL AND r.expires_at > now()")" -ge 1 ]
}

cleanup_interval_under_an_hour_stops_the_start() {
  stop_api
  need_database || return 1
  launch_api "$scratch/short-interval.log" Production Auth__RefreshTokenCleanupInterval=00:30:00
  pid=$started_pid
  attempts=0
  while kill -0 "$pid" 2>/dev/null && [ "$attempts" -lt 60 ]; do
    attempts=$((attempts + 1))
    sleep 0.5
  done
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid"
    echo "the API kept running with a cleanup every 30 minutes"
    return 1
  fi
  grep -q 'Auth:RefreshTokenCleanupInterval must be at least 01:00:00' "$scratch/short-interval.log" || {
    tail -n 5 "$scratch/short-interval.log"
    return 1
  }
}

task_4() {
  echo "19.4 the refresh tokens' cleanup"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the cleanup, its worker and its settings pass their tests" cleanup_tests_pass
  check "the API's tests run with the cleanup off" api_tests_turn_the_cleanup_off
  check "turned off, the real API keeps every old row, and says so" cleanup_turned_off_keeps_them
  check "at start, the real API deletes what ended over 30 days ago, one log line with the count" cleanup_at_start_deletes_the_old_ones
  check "the admin's live sessions stay" live_sessions_stay
  check "the API refuses to start with a cleanup every 30 minutes" cleanup_interval_under_an_hour_stops_the_start
}

# ---- 19.5: the first bundle, and the charts' colors.

web=frontend
app=$web/src/app

plain() { # plain <file>: without the terminal's colors
  sed 's/\x1b\[[0-9;]*m//g' "$1"
}

specs_pass() { # specs_pass <minimum passed> [spec file or glob, from frontend/]
  log="$scratch/specs.log"
  (cd "$web" && npx ng test --no-watch ${2:+--include "$2"}) >"$log" 2>&1 || {
    plain "$log" | tail -n 25
    return 1
  }
  passed=$(plain "$log" | sed -n 's/^ *Tests *\([0-9]*\) passed.*/\1/p' | tail -n 1)
  [ "${passed:-0}" -ge "$1" ] || {
    echo "only ${passed:-0} specs passed, expected at least $1"
    return 1
  }
}

frontend_lint_is_clean() {
  (cd "$web" && npx ng lint) >"$scratch/lint.log" 2>&1 || {
    plain "$scratch/lint.log" | tail -n 20
    return 1
  }
}

frontend_formatting_is_clean() {
  (cd "$web" && npx prettier --check .)
}

built() {
  [ -f "$scratch/built" ] && return 0
  (cd "$web" && npx ng build --stats-json --output-path "$(native "$scratch/dist")") >"$scratch/build.log" 2>&1 || {
    plain "$scratch/build.log" | tail -n 25
    return 1
  }
  touch "$scratch/built"
}

# Since group 19 the first bundle is under its budget: no warning at all, the budget's neither.
the_build_warns_of_nothing() {
  built || return 1
  if plain "$scratch/build.log" | grep -F 'WARNING'; then
    return 1
  fi
}

# docs/SPEC.md §6.2: what remained plus 5%, rounded up to 10 kB (706.01 kB → 741.31 → 750), the error at 1 MB.
the_budget_is_what_remained() {
  node -e "
    const build = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')).projects.kanabol.architect.build;
    const initial = build.configurations.production.budgets.find((budget) => budget.type === 'initial');
    if (initial.maximumWarning !== '750kB' || initial.maximumError !== '1MB') {
      console.log(JSON.stringify(initial));
      process.exit(1);
    }
  " "$(native "$web/angular.json")"
}

initial_total() {
  plain "$scratch/build.log" | sed -n 's/.*Initial total *| *\([0-9.]*\) kB.*/\1/p'
}

the_initial_total_is_under_the_budget() {
  built || return 1
  total=$(initial_total)
  [ -n "$total" ] || {
    echo "no Initial total in the build's output"
    return 1
  }
  node -e "process.exit(Number(process.argv[1]) < 750 && Number(process.argv[1]) < 800 ? 0 : 1)" "$total" || {
    echo "Initial total $total kB"
    return 1
  }
}

# The first bundle is what index.html loads: its scripts and the chunks it preloads.
initial_files() {
  grep -oE '<script src="[^"]*"|<link rel="modulepreload" href="[^"]*"' "$scratch/dist/browser/index.html" |
    sed 's/.*"\([^"]*\)"$/\1/'
}

# Names the minifier keeps, of what the shell doesn't need at start: the paginator, mat-select, the slide
# toggle, the forms they bring, chart.js and SignalR.
moved_markers='mat-mdc-paginator mat-mdc-select mat-mdc-slide-toggle ng-untouched formControlName datasetElementType|_getStackCount|getLabelAndValue WebSocketTransport|HubConnectionBuilder|negotiateVersion'

none_of_it_is_in_the_first_bundle() {
  built || return 1
  initial=$(initial_files)
  [ -n "$initial" ] || {
    echo "index.html loads nothing"
    return 1
  }
  missing=0
  for marker in $moved_markers; do
    for file in $initial; do
      if grep -qE "$marker" "$scratch/dist/browser/$file"; then
        echo "$file, in the first bundle, holds $marker"
        missing=1
      fi
    done
    # The marker is real: some lazy chunk holds it.
    grep -qlE "$marker" "$scratch"/dist/browser/*.js || {
      echo "no file holds $marker at all"
      missing=1
    }
  done
  [ "$missing" = 0 ]
}

# Every component with a mat-paginator provides its Romanian labels, which app.config no longer does; the
# only slide toggle sets its defaults, and the navbar loads it with @defer (on idle).
the_moved_providers_are_where_they_are_used() {
  for file in $(grep -rlE "MatPaginator[,} ]" "$app" --include='*.ts' | grep -v '\.spec\.ts$' | grep -v 'paginator-labels\.ts$'); do
    in_file 'providePaginatorLabels()' "$file" || {
      echo "$file shows a paginator without providePaginatorLabels()"
      return 1
    }
  done
  lacks "@angular/material/(paginator|slide-toggle)|PaginatorLabels" "$app/app.config.ts" &&
    in_file 'provide: MAT_SLIDE_TOGGLE_DEFAULT_OPTIONS,' "$app/shared/ui/theme-toggle.ts" &&
    [ "$(grep -rlF "MatSlideToggle" "$app" --include='*.ts' | grep -v '\.spec\.ts$')" = "$app/shared/ui/theme-toggle.ts" ] &&
    grep -A1 -F '@defer (on idle) {' "$app/core/layout/navbar.ts" | grep -qF '<kb-theme-toggle' &&
    [ "$(grep -cF '<kb-theme-toggle' "$app/core/layout/navbar.ts")" -eq 1 ]
}

in_file() { # in_file <text> <file>
  grep -qF -- "$1" "$2"
}

lacks() { # lacks <extended regex> <file...>
  ! grep -nE -- "$@" || return 1
}

# docs/UI_UX_SPEC.md §2.1, §2.3, §2.4: the charts' tokens in the built CSS, the light ones once and the dark
# ones in both dark rules (the media query and data-theme), and kb-chart reading them, not brand-800 and sky-300.
the_chart_tokens_are_in_the_css() {
  built || return 1
  tr -d ' \n\r' <"$(ls "$scratch"/dist/browser/styles-*.css | head -n 1)" >"$scratch/styles.min.css"
  for expected in '--kb-chart-1:#122d94:1' '--kb-chart-2:#135bf7:1' '--kb-chart-1:#91c4fb:2' '--kb-chart-2:#3d6fc9:2'; do
    token=${expected%:*}
    count=$(grep -oF -- "$token;" "$scratch/styles.min.css" | wc -l | tr -d ' ')
    [ "$count" = "${expected##*:}" ] || {
      echo "$token is in the built CSS $count times, not ${expected##*:}"
      return 1
    }
  done
}

the_chart_reads_its_tokens() {
  chart="$app/shared/ui/chart.ts"
  in_file "token(one.tone === 'primary' ? '--kb-chart-1' : '--kb-chart-2')" "$chart" &&
    lacks "kb-brand-800|kb-sky-300" "$chart" &&
    lacks "#[0-9a-fA-F]{3,8}\b" "$chart"
}

task_5() {
  echo "19.5 the first bundle, and the charts' colors"
  check "the specs pass, the paginators in Romanian from their own pages, the theme's switch deferred" specs_pass 736
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "the build warns of nothing, its budget included" the_build_warns_of_nothing
  check "the budget is what remained plus 5%, 750 kB, the error at 1 MB" the_budget_is_what_remained
  check "the Initial total is under the budget, and under 800 kB" the_initial_total_is_under_the_budget
  [ -f "$scratch/build.log" ] && printf '        %s\n' "$(plain "$scratch/build.log" | grep -F 'Initial total' | tr -s ' ')"
  check "the paginator, mat-select, the slide toggle, the forms, chart.js and SignalR are out of it" none_of_it_is_in_the_first_bundle
  check "their providers are where they're used, and the navbar defers the theme's switch" the_moved_providers_are_where_they_are_used
  check "the charts' tokens are in the built CSS, light and dark" the_chart_tokens_are_in_the_css
  check "kb-chart reads --kb-chart-1 and --kb-chart-2, and no hex" the_chart_reads_its_tokens
}

# ---- 19.6: the common test password hashed once per suite, and the hooks on main kept to the fast checks.

common_password_tests_pass() {
  named_tests_pass Kanabol.Api.Tests CommonPasswordTests.People_MadeByTheTests_ShareOneHashOfTheCommonPassword_AndSignInWithIt &&
    named_tests_pass Kanabol.Application.Tests CommonPasswordTests.People_MadeByTheTests_ShareOneHashOfTheCommonPassword
}

# No helper that makes people hashes the password itself any more.
nobody_hashes_per_person() {
  lacks 'IPasswordHasher>\(\)\.Hash\(Password\)' \
    backend/tests/Kanabol.Api.Tests/Support/*.cs backend/tests/Kanabol.Api.Tests/Reports/*.cs \
    backend/tests/Kanabol.Application.Tests/Features/Auth/AuthHandlerTest.cs \
    backend/tests/Kanabol.Application.Tests/Features/ManagementHandlerTest.cs &&
    in_file 'Lazy<string> _passwordHash = new(() => new PasswordHasher().Hash(Password));' backend/tests/Kanabol.Api.Tests/Support/AuthApiTest.cs &&
    in_file 'Lazy<string> _passwordHash = new(() => new PasswordHasher().Hash(Password));' backend/tests/Kanabol.Application.Tests/Features/Auth/AuthHandlerTest.cs
}

# docs/SPEC.md §14.3 and §14.6 (v1.33): the full suite left the hooks, so a merge into main has no time of
# its own to keep any more. verify.sh treats main as any branch, and nothing runs the suite on a push.
main_hooks_run_only_the_fast_checks() {
  ! grep -v '^[[:space:]]*#' scripts/verify.sh | grep -nE 'test-all\.sh|"main"' &&
    [ ! -e .husky/pre-push ] &&
    in_file 'exec scripts/verify.sh merge' .husky/pre-merge-commit &&
    in_file 'exec scripts/verify.sh commit' .husky/pre-commit || {
    echo "a hook still runs the full suite, or treats main apart"
    return 1
  }
}

task_6() {
  echo "19.6 the common password hashed once, and the hooks on main kept to the fast checks"
  check "Docker is running" docker_is_running
  check "the people the tests make share one hash of the common password" common_password_tests_pass
  check "no helper hashes it per person" nobody_hashes_per_person
  check "the hooks on main run only the fast checks, and no pre-push runs the suite" main_hooks_run_only_the_fast_checks
}
tasks=${*:-1 2 3 4 5 6}
for task in $tasks; do
  case "$task" in
    1) task_1 ;;
    2) task_2 ;;
    3) task_3 ;;
    4) task_4 ;;
    5) task_5 ;;
    6) task_6 ;;
    *)
      echo "No task $task."
      failures=$((failures + 1))
      ;;
  esac
done

if [ "$failures" -eq 0 ]; then
  echo "All checks passed."
else
  echo "$failures check(s) failed."
  exit 1
fi
