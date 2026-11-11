#!/bin/sh
# Acceptance checks for authorization on permissions (group 7 in the project's Tasks.md).
# The test projects hold the rules, so these checks run them, and add what they can't see: the real
# API process in Production on a real PostgreSQL. No real endpoint uses [HasPermission] until group 8,
# so a refused permission on the real API is checked in scripts/check-users.sh.
#
# Usage: scripts/check-permissions.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The tests need Docker running. The real API needs a PostgreSQL
# whose user may create databases, with its connection string in KANABOL_CHECK_DB or else in the
# API's user secrets (ConnectionStrings:Default). Each run works in its own database and drops it.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_permissions_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5281
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=

admin_email=admin@kanabol.local
admin_password=only-for-these-checks

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

api_is_up() {
  [ -n "$api_pid" ] && return 0
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1

  (
    cd "$api_dir" || exit 1
    ASPNETCORE_ENVIRONMENT=Production \
      ConnectionStrings__Default=$(connection_to "$check_db") \
      Jwt__Secret=only-for-these-checks-and-long-enough-to-pass \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword=$admin_password \
      exec dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$api_log" 2>&1 &
  api_pid=$!

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
  log_lines_at_startup=$(wc -l <"$api_log")
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
    sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  curl -sS --max-time 30 -o "$scratch/body" -D "$scratch/headers" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

body_field() { # body_field <name>: the value of a text field in the last answer
  sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body"
}

# ---- 7.1

task_1() {
  echo "7.1 Permissions, [HasPermission], the handler and the policy provider"
  check "Docker is running" docker_is_running
  check "the attribute is tracked" tracked "$api_dir/Authorization/HasPermissionAttribute.cs"
  check "the handler is tracked" tracked "$api_dir/Authorization/PermissionAuthorizationHandler.cs"
  check "the policy provider is tracked" tracked "$api_dir/Authorization/PermissionPolicyProvider.cs"
  check "the list of permissions is the spec's" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~PermissionsTests' 2
  check "their tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~PermissionAuthorizationTests|FullyQualifiedName~PermissionPolicyProviderTests' 19
}

# ---- 7.2

task_2() {
  echo "7.2 IProjectAccess"
  check "the implementation is tracked" tracked backend/src/Kanabol.Application/Features/Projects/ProjectAccess.cs
  check "its tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~ProjectAccessTests' 28
}

# ---- 7.3

no_token_gets_401() {
  is 401 "$(send GET /api/auth/me)" || return 1
  in_file '"status":401' "$scratch/body"
}

admin_signs_in() {
  is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' \
    -d "{\"email\":\"$admin_email\",\"password\":\"$admin_password\"}")" || {
    cat "$scratch/body"
    return 1
  }
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

# The token's claims, from the real seed on the real database: every permission in the spec's list.
token_lists_every_permission() {
  claims=$(printf '%s' "$admin_token" | node -e '
    let token = "";
    process.stdin.on("data", (part) => (token += part));
    process.stdin.on("end", () => {
      const payload = JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString());
      console.log([].concat(payload.permission).sort().join(" "));
    });
  ' | tr -d '\r')
  is 'columns.manage projects.create projects.manage projects.view_all reports.view roles.manage settings.manage tasks.archive tasks.create users.manage' "$claims"
}

default_policy_still_opens_me() {
  is 200 "$(send GET /api/auth/me -H "Authorization: Bearer $admin_token")" || {
    cat "$scratch/body"
    return 1
  }
  in_file '"users.manage"' "$scratch/body"
}

# The endpoints the tests add for themselves must not exist in what ships.
test_endpoints_are_absent() {
  for path in /api/test-permissions/users /api/test-permissions/nobody /api/test-permissions-on-controller \
    /api/test-project-access/019a0000-0000-7000-8000-000000000001/member \
    /api/test-project-access/019a0000-0000-7000-8000-000000000001/owner /api/test-protected /api/test-samples/boom; do
    is 404 "$(send GET "$path" -H "Authorization: Bearer $admin_token")" || {
      echo "on $path"
      return 1
    }
  done
}

api_still_answers() {
  is 200 "$(send GET /health)"
}

# Only what came after startup: on a new database, EF logs an error while it looks for its history table.
no_errors_since_startup() {
  sleep 1
  if tail -n "+$((log_lines_at_startup + 1))" "$api_log" | grep -E '"@l":"(Error|Fatal)"|Unhandled exception'; then
    return 1
  fi
}

task_3() {
  echo "7.3 Over HTTP, and on the real API"
  check "the flow tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~AuthorizationFlowTests|FullyQualifiedName~PermissionDeniedResultHandlerTests|FullyQualifiedName~RefusalLogTests' 23
  check "the real API starts in Production" api_is_up
  check "a request without a token still gets a 401, with the custom policy provider" no_token_gets_401
  check "the first admin signs in" admin_signs_in
  check "their token lists every permission in the spec" token_lists_every_permission
  check "an endpoint with only the default policy still opens" default_policy_still_opens_me
  check "the tests' own endpoints aren't in what ships" test_endpoints_are_absent
  check "the log has no errors since startup" no_errors_since_startup
  check "the real API is still up after all that" api_still_answers
  stop_api # It holds the files a full build replaces.
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3
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
