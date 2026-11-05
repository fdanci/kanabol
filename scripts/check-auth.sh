#!/bin/sh
# Acceptance checks for authentication (group 6 in the project's Tasks.md).
# The test projects hold the rules, so these checks run them, and add what they can't see: the real
# API process in Production on a real PostgreSQL, with its headers, its logs and the real clock.
# How it behaves behind the real nginx is in scripts/check-docker.sh.
#
# Usage: scripts/check-auth.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The tests need Docker running. The real API needs a PostgreSQL
# whose user may create databases, with its connection string in KANABOL_CHECK_DB or else in the
# API's user secrets (ConnectionStrings:Default). Each run works in its own databases and drops them.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_auth_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5280
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
next_address=0

admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'
wrong_password='nu este parola buna'

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
# The API trusts this header from a connection on this machine.
unused_address() {
  next_address=$((next_address + 1))
  printf '10.77.0.%s' "$next_address"
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  curl -sS --max-time 30 -o "$scratch/body" -D "$scratch/headers" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

login() { # login <email> <password> [address]
  send POST /api/auth/login -H 'Content-Type: application/json' -H "X-Forwarded-For: ${3:-$(unused_address)}" \
    -d "{\"email\":\"$1\",\"password\":\"$2\"}"
}

body_field() { # body_field <name>: the value of a text field in the last answer
  sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body"
}

set_cookie_line() {
  tr -d '\r' <"$scratch/headers" | grep -i '^set-cookie: refresh_token='
}

refresh_cookie() { # The cookie's value in the last answer
  set_cookie_line | sed 's/^[^=]*=\([^;]*\);.*/\1/'
}

# ---- 6.1

task_1() {
  echo "6.1 Password hasher and policy"
  check "the hasher is tracked" tracked backend/src/Kanabol.Infrastructure/Authentication/PasswordHasher.cs
  check "it's built on PasswordHasher<User>" in_file 'PasswordHasher<User>' backend/src/Kanabol.Infrastructure/Authentication/PasswordHasher.cs
  check "the policy is tracked" tracked backend/src/Kanabol.Application/Features/Auth/PasswordPolicy.cs
  check "the list of common passwords is tracked" tracked backend/src/Kanabol.Application/Features/Auth/common-passwords.txt
  check "their tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~PasswordPolicyTests|FullyQualifiedName~PasswordHasherTests' 17
}

# ---- 6.2

task_2() {
  echo "6.2 JwtTokenService"
  check "the service is tracked" tracked backend/src/Kanabol.Infrastructure/Authentication/JwtTokenService.cs
  check "its tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~JwtTokenServiceTests' 10
}

# ---- 6.3

admin_signs_in() {
  is 200 "$(login "$admin_email" "$admin_password")" || {
    cat "$scratch/body"
    return 1
  }
  admin_token=$(body_field accessToken)
  admin_refresh=$(refresh_cookie)
  [ -n "$admin_token" ] && [ -n "$admin_refresh" ] || {
    echo "no token in the answer"
    return 1
  }
  in_file '"mustChangePassword":true' "$scratch/body"
}

cookie_has_the_specs_attributes() {
  for attribute in '; path=/api/auth' '; secure' '; samesite=strict' '; httponly' '; expires='; do
    set_cookie_line | grep -qi -- "$attribute" || {
      set_cookie_line | sed 's/=[^;]*;/=<value>;/'
      echo "no $attribute"
      return 1
    }
  done
}

body_lacks_the_refresh_token() {
  ! in_file "$admin_refresh" "$scratch/body"
}

database_has_only_the_hash() {
  is 1 "$(sql "$(connection_to "$check_db")" "select count(*) from refresh_tokens where length(token_hash) = 64 and token_hash = lower(token_hash)" | tr -d '\r')" || return 1
  is 0 "$(sql "$(connection_to "$check_db")" "select count(*) from refresh_tokens where token_hash = '$admin_refresh'" | tr -d '\r')"
}

refresh_rotates() {
  is 200 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$admin_refresh")" || return 1
  rotated_refresh=$(refresh_cookie)
  [ -n "$rotated_refresh" ] && [ "$rotated_refresh" != "$admin_refresh" ]
}

reuse_ends_the_session() {
  is 401 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$admin_refresh")" || return 1
  set_cookie_line | grep -qi 'expires=Thu, 01 Jan 1970' || {
    echo "the cookie wasn't cleared"
    return 1
  }
  is 401 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$rotated_refresh")"
}

logout_ends_the_session() {
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  session=$(refresh_cookie)
  is 204 "$(send POST /api/auth/logout -H "Cookie: refresh_token=$session")" || return 1
  is 401 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$session")"
}

# A database that stopped at the first migration, with a user in it, moves on without losing anyone.
migration_keeps_existing_users() {
  need_database || return 1
  database="${check_db}_upgrade"
  sql "$admin_connection" "CREATE DATABASE $database" || return 1
  connection=$(connection_to "$database")
  dotnet tool restore >/dev/null || return 1

  (cd backend && dotnet ef database update 20261104191903_InitialSchema -p src/Kanabol.Infrastructure -s src/Kanabol.Api --connection "$connection") >"$scratch/upgrade.log" 2>&1 || {
    tail -n 10 "$scratch/upgrade.log"
    return 1
  }
  sql "$connection" "insert into roles (id, name, is_system, created_at) values ('019a0000-0000-7000-8000-000000000001', 'Old', false, now())" || return 1
  sql "$connection" "insert into users (id, email, display_name, password_hash, role_id, is_active, must_change_password, created_at) values ('019a0000-0000-7000-8000-000000000002', 'old@example.com', 'Old', 'x', '019a0000-0000-7000-8000-000000000001', true, false, now())" || return 1

  (cd backend && dotnet ef database update -p src/Kanabol.Infrastructure -s src/Kanabol.Api --connection "$connection") >>"$scratch/upgrade.log" 2>&1 || {
    tail -n 10 "$scratch/upgrade.log"
    return 1
  }
  is 'old@example.com|0' "$(sql "$connection" "select email, failed_login_attempts from users" | tr -d '\r')"
}

task_3() {
  echo "6.3 Login, refresh and logout"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked "$api_dir/Controllers/AuthController.cs"
  check "the entities' tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~Domain.Tests.Users' 8
  check "the handlers' tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~LoginUserCommandHandlerTests|FullyQualifiedName~RefreshSessionCommandHandlerTests|FullyQualifiedName~LogoutUserCommandHandlerTests' 30
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~LoginEndpointTests|FullyQualifiedName~RefreshEndpointTests|FullyQualifiedName~LogoutEndpointTests' 12
  check "the migration keeps existing users" migration_keeps_existing_users
  check "the real API starts in Production" api_is_up
  check "the first admin signs in and is asked to change the password" admin_signs_in
  check "the cookie has the spec's attributes" cookie_has_the_specs_attributes
  check "the refresh token stays out of the body" body_lacks_the_refresh_token
  check "the database has only its hash" database_has_only_the_hash
  check "a refresh rotates the cookie" refresh_rotates
  check "reusing the old one ends the session and clears the cookie" reuse_ends_the_session
  check "logging out ends the session" logout_ends_the_session
}

# ---- 6.4

send_with_token() { # send_with_token <token> <method> <path> [curl arguments...]
  token=$1
  shift
  method=$1
  path=$2
  shift 2
  send "$method" "$path" -H "Authorization: Bearer $token" "$@"
}

change_password() { # change_password <token> <current> <new>
  send_with_token "$1" POST /api/auth/change-password -H 'Content-Type: application/json' \
    -d "{\"currentPassword\":\"$2\",\"newPassword\":\"$3\"}"
}

common_password_is_refused() {
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  forced_token=$(body_field accessToken)
  is 400 "$(change_password "$forced_token" "$admin_password" password123)" || return 1
  in_file '"newPassword"' "$scratch/body"
}

short_password_is_refused() {
  is 400 "$(change_password "$forced_token" "$admin_password" scurta)"
}

forced_change_works() {
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  earlier_session=$(refresh_cookie)
  forced_token=$(body_field accessToken)
  is 200 "$(change_password "$forced_token" "$admin_password" "$new_admin_password")" || {
    cat "$scratch/body"
    return 1
  }
  admin_password=$new_admin_password
  in_file '"mustChangePassword":false' "$scratch/body"
}

earlier_sessions_are_over() {
  is 401 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$earlier_session")"
}

new_password_signs_in() {
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  in_file '"mustChangePassword":false' "$scratch/body"
}

task_4() {
  echo "6.4 Change password"
  check "the handler's tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~ChangePasswordCommandHandlerTests' 11
  check "the endpoint's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ChangePasswordEndpointTests|FullyQualifiedName~AccessTokenValidationTests' 23
  check "the real API starts in Production" api_is_up
  check "a common password is refused" common_password_is_refused
  check "and so is a short one" short_password_is_refused
  check "the forced change works and clears the flag" forced_change_works
  check "earlier sessions are over" earlier_sessions_are_over
  check "the new password signs in, with no flag" new_password_signs_in
}

# ---- 6.5

# All on the real clock: the window has to really close and really open again.
limit_holds_and_reopens() {
  address=10.77.1.1
  for attempt in 1 2 3 4 5; do
    is 401 "$(login nobody@example.com "$wrong_password" "$address")" || return 1
  done
  is 429 "$(login nobody@example.com "$wrong_password" "$address")" || return 1
  in_file '"code":"Auth.TooManyLoginAttempts"' "$scratch/body" || return 1

  wait_seconds=$(tr -d '\r' <"$scratch/headers" | sed -n 's/^[Rr]etry-[Aa]fter: //p')
  [ -n "$wait_seconds" ] && [ "$wait_seconds" -le 60 ] || {
    echo "Retry-After: '$wait_seconds'"
    return 1
  }

  is 401 "$(login nobody@example.com "$wrong_password" 10.77.1.2)" || return 1

  sleep $((wait_seconds + 1))
  is 401 "$(login nobody@example.com "$wrong_password" "$address")"
}

task_5() {
  echo "6.5 Rate limit on login"
  check "its tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~LoginRateLimitTests' 7
  check "the real API starts in Production" api_is_up
  check "after 5 attempts an address waits until the minute is over, and others don't" limit_holds_and_reopens
}

# ---- 6.6

me_answers() {
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  me_token=$(body_field accessToken)
  is 200 "$(send_with_token "$me_token" GET /api/auth/me -H 'X-Correlation-Id: check-auth-me')" || return 1
  in_file "\"email\":\"$admin_email\"" "$scratch/body" || return 1
  in_file '"users.manage"' "$scratch/body" || return 1
  me_id=$(body_field id)
  [ -n "$me_id" ]
}

me_needs_a_token() {
  is 401 "$(send GET /api/auth/me)" || return 1
  in_file '"status":401' "$scratch/body"
}

request_log_names_the_user() {
  sleep 1
  grep -F 'check-auth-me' "$api_log" | grep -F '/api/auth/me' | grep -qF "\"UserId\":\"$me_id\"" || {
    grep -F 'check-auth-me' "$api_log" | tail -n 3
    return 1
  }
}

task_6() {
  echo "6.6 ICurrentUser and /api/auth/me"
  check "the handler's tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetCurrentUserQueryHandlerTests' 6
  check "the endpoint's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~CurrentUserEndpointTests' 6
  check "the real API starts in Production" api_is_up
  check "it answers with the user and the permissions" me_answers
  check "it needs a token" me_needs_a_token
  check "the request's log line names the user" request_log_names_the_user
}

# ---- 6.7

log_has() { # log_has <text>
  in_file "$1" "$api_log" || {
    echo "not in the log: $1"
    tail -n 5 "$api_log"
    return 1
  }
}

log_lacks() { # log_lacks <text>
  ! in_file "$1" "$api_log"
}

real_session_for_the_log() {
  is 401 "$(login "$admin_email" "$wrong_password")" || return 1
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  log_token=$(body_field accessToken)
  log_refresh=$(refresh_cookie)
  log_refresh_hash=$(printf '%s' "$log_refresh" | sha256sum | cut -d ' ' -f 1)
  is 200 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$log_refresh" -H "Authorization: Bearer $log_token")"
  sleep 1
}

task_7() {
  echo "6.7 Whole sessions, and what the logs keep"
  check "the flow tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~AuthFlowTests' 4
  check "the real API starts in Production" api_is_up
  check "a whole session runs on it" real_session_for_the_log
  check "a refused login is logged, with the email" log_has "Login refused for"
  check "a login is logged" log_has "signed in"
  check "the wrong password isn't in the log" log_lacks "$wrong_password"
  check "the right one isn't either" log_lacks "$admin_password"
  check "nor the access token" log_lacks "$log_token"
  check "nor the refresh token" log_lacks "$log_refresh"
  check "nor its hash" log_lacks "$log_refresh_hash"
  check "the real API stops, since it holds the files a full build replaces" stop_api
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 5 6 7
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
