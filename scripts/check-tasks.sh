#!/bin/sh
# Acceptance checks for tasks (group 12 in the project's Tasks.md). Task 12.1, the active owner, is in
# check-projects.sh, next to the other checks of the owners.
# The tests hold the rules, so these checks run them, and add what they can't see: the migration on a
# database that already has tasks, and the real API process in Production on a real PostgreSQL, with
# its real seed, its logs and its answers.
#
# Usage: scripts/check-tasks.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The tests need Docker running. The real API and the migration need
# a PostgreSQL whose user may create databases, with its connection string in KANABOL_CHECK_DB or else in
# the API's user secrets (ConnectionStrings:Default). Each run works in its own databases and drops them.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_tasks_check_$"
api_dir=backend/src/Kanabol.Api
api_port=5288
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
migrations=backend/src/Kanabol.Infrastructure/Persistence/Migrations

admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'
initial_password='o parola initiala pentru dan'
dan_password='parola noua aleasa de dan'

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

tracked() {
  [ -f "$1" ] && [ -n "$(git ls-files -- "$1")" ]
}

tracked_glob() { # tracked_glob <pattern>: some tracked file matches
  [ -n "$(git ls-files -- "$1")" ] || {
    echo "nothing tracked matches $1"
    return 1
  }
}

in_file() { # in_file <text> <file>
  grep -qF -- "$1" "$2"
}

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
}

docker_is_running() {
  docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
  "$docker_cli" info --format '{{.ServerVersion}}'
}

tests_pass() { # tests_pass <project> <filter> <minimum passed>
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

solution_builds() {
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
}

formatting_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
}

ef() { # ef <arguments...>: runs dotnet ef from backend/, against the built projects
  (cd backend && dotnet ef "$@" -p src/Kanabol.Infrastructure -s src/Kanabol.Api --no-build)
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
// The column names have ă and Î, which the console would turn into a and I.
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

start_api() { # start_api <environment> <database> <port> <log>: leaves the process id in started_pid
  (
    cd "$api_dir" || exit 1
    ASPNETCORE_ENVIRONMENT=$1 \
      ConnectionStrings__Default=$(connection_to "$2") \
      Jwt__Secret=only-for-these-checks-and-long-enough-to-pass \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword=$admin_password \
      exec dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "http://127.0.0.1:$3"
  ) >"$4" 2>&1 &
  started_pid=$!

  attempts=0
  until curl -s -o /dev/null "http://127.0.0.1:$3/health"; do
    attempts=$((attempts + 1))
    if [ "$attempts" -gt 120 ] || ! kill -0 "$started_pid" 2>/dev/null; then
      started_pid=
      cat "$4"
      return 1
    fi
    sleep 0.5
  done
}

api_is_up() {
  [ -n "$api_pid" ] && return 0
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  start_api Production "$check_db" "$api_port" "$api_log" || return 1
  api_pid=$started_pid
}

stop_api() {
  [ -n "$api_pid" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
}

cleanup() {
  stop_api
  if [ -n "$admin_connection" ]; then
    sql "$admin_connection" "SELECT datname FROM pg_database WHERE datname LIKE '$check_db%'" | tr -d '\r' |
      while IFS= read -r database; do
        sql "$admin_connection" "DROP DATABASE IF EXISTS $database WITH (FORCE)" >/dev/null
      done
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# An address no other request of this run has used, so the limit per address stays out of the way.
# The count is in a file, because every caller runs in a subshell, where a new value would be lost.
unused_address() {
  printf x >>"$scratch/used-addresses"
  used=$(wc -c <"$scratch/used-addresses")
  printf '10.80.%s.%s' "$((used / 256))" "$((used % 256))"
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  curl -sS --max-time 30 -o "$scratch/body" -D "$scratch/headers" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

as() { # as <token> <method> <path> [JSON body]
  token=$1
  method=$2
  path=$3
  if [ "$#" -ge 4 ]; then
    send "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d "$4"
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

code_is() { # code_is <expected status> <expected code> <actual status>
  is "$1" "$3" || {
    cat "$scratch/body"
    return 1
  }
  in_file "\"code\":\"$2\"" "$scratch/body" || {
    echo "no code $2 in the answer:"
    cat "$scratch/body"
    return 1
  }
}

status_is() { # status_is <expected status> <actual status>: shows the answer when it differs
  is "$1" "$2" || {
    cat "$scratch/body"
    return 1
  }
}

location_header() {
  tr -d '\r' <"$scratch/headers" | sed -n 's/^[Ll]ocation: //p'
}

log_has() { # log_has <text>
  in_file "$1" "$api_log" || {
    echo "not in the log: $1"
    tail -n 5 "$api_log"
    return 1
  }
}

log_has_entry() { # log_has_entry <message template> <property, as JSON>: one entry with both
  grep -F -- "\"@mt\":\"$1\"" "$api_log" | grep -qF -- "$2" || {
    echo "no entry $1 with $2 in the log"
    return 1
  }
}

no_errors_in_the_log() {
  if grep -E 'Unhandled exception' "$api_log"; then
    return 1
  fi
}

# ---- Signing in: the first admin past the forced password change, and Dan, who has no permission.

admin_is_ready() {
  [ -n "${admin_token:-}" ] && return 0
  api_is_up || return 1
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
  status_is 200 "$(as "$first_token" POST /api/auth/change-password "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

dan_is_ready() {
  [ -n "${dan_token:-}" ] && return 0
  admin_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Fara permisiuni","permissions":[]}')" || return 1
  plain_role=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"dan@example.com\",\"displayName\":\"Dan Ionescu\",\"roleId\":\"$plain_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  dan_id=$(body_field id)
  status_is 200 "$(login dan@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$dan_password\"}")" || return 1
  dan_token=$(body_field accessToken)
  [ -n "$dan_token" ] && [ -n "$dan_id" ]
}

# ---- 12.2: the entity, which writes its own history.

task_2() {
  echo "12.2 TaskItem writes its history and raises its events"
  check "the entity is tracked" tracked backend/src/Kanabol.Domain/Tasks/TaskItem.cs
  check "its events are tracked" tracked_glob 'backend/src/Kanabol.Domain/Tasks/Events/*Event.cs'
  check "the history fields are tracked" tracked backend/src/Kanabol.Domain/Tasks/TaskHistoryFields.cs
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the entity's tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskItemTests' 30
  check "each of the eight changes has a method" every_change_has_a_method
  check "and writes its entry itself, not the handler" the_entity_writes_its_history
}

every_change_has_a_method() {
  for method in Rename ChangeDescription Assign SetPriority SetDueDate MoveTo Archive Restore; do
    grep -qF "public void $method(" backend/src/Kanabol.Domain/Tasks/TaskItem.cs || {
      echo "no method $method on TaskItem"
      return 1
    }
  done
}

# The history is the entity's own: nothing outside the domain makes an entry (SPEC section 5.1).
the_entity_writes_its_history() {
  if grep -rn --include=*.cs 'new TaskHistoryEntry(' backend/src | grep -v 'backend/src/Kanabol.Domain/'; then
    return 1
  fi
}

if [ "$#" -eq 0 ]; then
  set -- 2
fi

for task in "$@"; do
  if command -v "task_$task" >/dev/null 2>&1; then
    "task_$task"
  else
    echo "There is no task $task."
    failures=$((failures + 1))
  fi
done

echo
if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
