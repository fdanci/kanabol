#!/bin/sh
# Acceptance checks for users and roles (group 8 in the project's Tasks.md).
# The test projects hold the rules, so these checks run them, and add what they can't see: the real
# API process in Production on a real PostgreSQL, with its real seed, its logs and its answers, and
# the real one in Development with its Swagger.
#
# Usage: scripts/check-users.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The tests need Docker running. The real API needs a PostgreSQL
# whose user may create databases, with its connection string in KANABOL_CHECK_DB or else in the
# API's user secrets (ConnectionStrings:Default). Each run works in its own databases and drops them.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_users_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5282
dev_port=5283
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
dev_pid=
admin_connection=

admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'
initial_password='o parola initiala pentru ana'
ana_password='parola noua aleasa de ana'
bob_password='parola noua aleasa de bob'

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

stop_process() { # stop_process <pid>
  [ -n "$1" ] || return 0
  kill "$1" 2>/dev/null
  wait "$1" 2>/dev/null
}

stop_api() {
  stop_process "$api_pid"
  api_pid=
}

stop_dev_api() {
  stop_process "$dev_pid"
  dev_pid=
}

cleanup() {
  stop_api
  stop_dev_api
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
# The API trusts this header from a connection on this machine. The count is in a file, because
# every caller runs in a subshell, where a variable's new value would be lost.
unused_address() {
  printf x >>"$scratch/used-addresses"
  used=$(wc -c <"$scratch/used-addresses")
  printf '10.78.%s.%s' "$((used / 256))" "$((used % 256))"
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

body_field() { # body_field <name>: the value of a text field in the last answer
  sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body"
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

refresh_cookie() { # The cookie's value in the last answer
  tr -d '\r' <"$scratch/headers" | grep -i '^set-cookie: refresh_token=' | sed 's/^[^=]*=\([^;]*\);.*/\1/'
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

log_lacks() { # log_lacks <text>
  ! in_file "$1" "$api_log"
}

# ---- Signing in: every check below needs the first admin past the forced password change.

first_admin_is_held_back() {
  is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
  code_is 403 Auth.PasswordChangeRequired "$(as "$first_token" GET /api/users)"
}

first_admin_changes_password() {
  is 200 "$(as "$first_token" POST /api/auth/change-password "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || {
    cat "$scratch/body"
    return 1
  }
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

admin_is_ready() {
  [ -n "${admin_token:-}" ] && return 0
  api_is_up || return 1
  first_admin_is_held_back || return 1
  first_admin_changes_password
}

# ---- 8.1

roles_answer_with_a_page() {
  is 200 "$(as "$admin_token" GET '/api/roles?page=1&pageSize=25')" || return 1
  in_file '"page":1' "$scratch/body" || return 1
  in_file '"pageSize":25' "$scratch/body" || return 1
  in_file '"totalCount":1' "$scratch/body" || return 1
  in_file '"isSystem":true' "$scratch/body"
}

permissions_are_listed() {
  is 200 "$(as "$admin_token" GET /api/permissions)" || return 1
  in_file '"tasks.create"' "$scratch/body" && in_file '"users.manage"' "$scratch/body"
}

creates_analyst_role() {
  is 201 "$(as "$admin_token" POST /api/roles '{"name":"Analist","permissions":["reports.view"]}')" || {
    cat "$scratch/body"
    return 1
  }
  analyst_role=$(body_field id)
  is "/api/roles/$analyst_role" "$(location_header)"
}

creates_manager_role() {
  is 201 "$(as "$admin_token" POST /api/roles '{"name":"Manager","permissions":["users.manage","reports.view"]}')" || return 1
  manager_role=$(body_field id)
  [ -n "$manager_role" ]
}

updates_a_role() {
  is 200 "$(as "$admin_token" PUT "/api/roles/$analyst_role" '{"name":"Analist senior","permissions":["reports.view","tasks.create"]}')" || return 1
  in_file '"name":"Analist senior"' "$scratch/body" && in_file '"tasks.create"' "$scratch/body"
}

deletes_an_unused_role() {
  is 201 "$(as "$admin_token" POST /api/roles '{"name":"De sters","permissions":[]}')" || return 1
  unused_role=$(body_field id)
  is 204 "$(as "$admin_token" DELETE "/api/roles/$unused_role")" || return 1
  code_is 404 Role.NotFound "$(as "$admin_token" GET "/api/roles/$unused_role")"
}

admin_role_id() {
  sql "$(connection_to "$check_db")" "select id from roles where is_system" | tr -d '\r'
}

admin_role_stays_fixed() {
  admin_role=$(admin_role_id)
  code_is 409 Role.SystemRoleIsFixed "$(as "$admin_token" PUT "/api/roles/$admin_role" '{"name":"Sef","permissions":[]}')" || return 1
  code_is 409 Role.SystemRoleIsFixed "$(as "$admin_token" DELETE "/api/roles/$admin_role")" || return 1
  is 'Admin|True|8' "$(sql "$(connection_to "$check_db")" "select r.name, r.is_system, count(*) from roles r join role_permissions p on p.role_id = r.id where r.is_system group by r.name, r.is_system" | tr -d '\r')"
}

admin_name_stays_reserved() {
  code_is 409 Role.NameTaken "$(as "$admin_token" POST /api/roles '{"name":"ADMIN","permissions":[]}')"
}

unknown_permission_is_refused() {
  is 400 "$(as "$admin_token" POST /api/roles '{"name":"Nou","permissions":["no.such.permission"]}')"
}

task_1() {
  echo "8.1 Roles"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked "$api_dir/Controllers/RolesController.cs"
  check "the entity's tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~RoleTests' 8
  check "the handlers' tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~Features.Roles' 52
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~RolesEndpointTests|FullyQualifiedName~EndpointPermissionsTests' 36
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "the roles come as a page" roles_answer_with_a_page
  check "the permissions are listed, with tasks.create" permissions_are_listed
  check "a role is created, with its Location" creates_analyst_role
  check "and another one, for later" creates_manager_role
  check "a role is renamed and its permissions replaced" updates_a_role
  check "an unused role is deleted" deletes_an_unused_role
  check "Admin can't be renamed, restricted or deleted" admin_role_stays_fixed
  check "nor can a role take its name in another case" admin_name_stays_reserved
  check "a permission that doesn't exist is refused" unknown_permission_is_refused
}

# ---- 8.2

create_user() { # create_user <email> <name> <role>
  as "$admin_token" POST /api/users "{\"email\":\"$1\",\"displayName\":\"$2\",\"roleId\":\"$3\",\"initialPassword\":\"$initial_password\"}"
}

creates_ana() {
  is 201 "$(create_user Ana.Pop@Example.com 'Ana Pop' "$manager_role")" || {
    cat "$scratch/body"
    return 1
  }
  ana_id=$(body_field id)
  is "/api/users/$ana_id" "$(location_header)" || return 1
  in_file '"email":"ana.pop@example.com"' "$scratch/body" || return 1
  in_file '"mustChangePassword":true' "$scratch/body"
}

answer_lacks_the_password() {
  ! in_file "$initial_password" "$scratch/body" && ! grep -qi 'passwordHash' "$scratch/body"
}

ana_signs_in_and_changes_it() {
  is 200 "$(login ana.pop@example.com "$initial_password")" || return 1
  in_file '"mustChangePassword":true' "$scratch/body" || return 1
  forced=$(body_field accessToken)
  code_is 403 Auth.PasswordChangeRequired "$(as "$forced" GET /api/users)" || return 1
  is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$ana_password\"}")" || return 1
  ana_token=$(body_field accessToken)
  ana_refresh=$(refresh_cookie)
  [ -n "$ana_token" ] && [ -n "$ana_refresh" ]
}

users_answer_with_a_page() {
  is 200 "$(as "$ana_token" GET '/api/users?page=1&pageSize=25')" || return 1
  in_file '"totalCount":2' "$scratch/body" && ! grep -qi 'passwordHash' "$scratch/body"
}

a_user_is_edited() {
  is 200 "$(as "$admin_token" PUT "/api/users/$ana_id" '{"email":"ana.pop@example.com","displayName":"Ana Popescu"}')" || return 1
  in_file '"displayName":"Ana Popescu"' "$scratch/body"
}

taken_email_is_refused() {
  code_is 409 User.EmailTaken "$(create_user admin@kanabol.local Altcineva "$manager_role")"
}

common_password_is_refused() {
  is 400 "$(as "$admin_token" POST /api/users "{\"email\":\"nou@example.com\",\"displayName\":\"Nou\",\"roleId\":\"$manager_role\",\"initialPassword\":\"password123\"}")" || return 1
  in_file '"initialPassword"' "$scratch/body"
}

missing_permission_is_refused() {
  code_is 403 Auth.PermissionRequired "$(as "$ana_token" GET /api/roles)" || return 1
  ! in_file 'roles.manage' "$scratch/body"
}

refusal_is_logged_with_the_user() {
  sleep 1
  line=$(grep -F 'Refused' "$api_log" | grep -F '/api/roles' | tail -n 1)
  [ -n "$line" ] || {
    echo "no refusal in the log"
    return 1
  }
  printf '%s' "$line" | grep -qF 'roles.manage' || {
    echo "$line"
    return 1
  }
  printf '%s' "$line" | grep -qF "\"UserId\":\"$ana_id\"" || {
    echo "$line"
    return 1
  }
}

no_one_gives_more_than_they_have() {
  admin_role=$(admin_role_id)
  code_is 403 User.RoleBeyondYours "$(as "$ana_token" PUT "/api/users/$ana_id/role" "{\"roleId\":\"$admin_role\"}")" || return 1
  code_is 403 User.RoleBeyondYours "$(as "$ana_token" POST /api/users "{\"email\":\"al.doilea@example.com\",\"displayName\":\"Altul\",\"roleId\":\"$admin_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  is 'Manager' "$(sql "$(connection_to "$check_db")" "select r.name from users u join roles r on r.id = u.role_id where u.id = '$ana_id'" | tr -d '\r')" || return 1
  is 0 "$(sql "$(connection_to "$check_db")" "select count(*) from users where email = 'al.doilea@example.com'" | tr -d '\r')"
}

no_one_touches_who_is_above_them() {
  admin_id=$(sql "$(connection_to "$check_db")" "select id from users where email = '$admin_email'" | tr -d '\r')
  code_is 403 User.PermissionsBeyondYours "$(as "$ana_token" POST "/api/users/$admin_id/deactivate")" || return 1
  code_is 403 User.PermissionsBeyondYours "$(as "$ana_token" PUT "/api/users/$admin_id" '{"email":"altul@example.com","displayName":"Altul"}')" || return 1
}

assignable_roles_are_only_what_they_can_give() {
  is 200 "$(as "$ana_token" GET /api/users/assignable-roles)" || return 1
  in_file '"name":"Manager"' "$scratch/body" || return 1
  ! in_file '"name":"Admin"' "$scratch/body"
}

role_change_ends_the_sessions() {
  is 200 "$(as "$admin_token" PUT "/api/users/$ana_id/role" "{\"roleId\":\"$analyst_role\"}")" || {
    cat "$scratch/body"
    return 1
  }
  in_file '"roleName":"Analist senior"' "$scratch/body" || return 1
  is 401 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$ana_refresh")"
}

old_token_no_longer_manages() {
  code_is 403 Auth.PermissionRequired "$(as "$ana_token" POST /api/users "{\"email\":\"inca.unul@example.com\",\"displayName\":\"Altul\",\"roleId\":\"$manager_role\",\"initialPassword\":\"$initial_password\"}")"
}

next_login_has_the_new_permissions() {
  is 200 "$(login ana.pop@example.com "$ana_password")" || return 1
  ana_token=$(body_field accessToken)
  code_is 403 Auth.PermissionRequired "$(as "$ana_token" GET /api/users)"
}

task_2() {
  echo "8.2 Users"
  check "the controller is tracked" tracked "$api_dir/Controllers/UsersController.cs"
  check "the entity's tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~Domain.Tests.Users.UserTests' 7
  check "the handlers' tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~Features.Users' 69
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~UsersEndpointTests' 26
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  [ -n "${manager_role:-}" ] || task_1 >/dev/null
  check "a user is created with an initial password, with their Location" creates_ana
  check "the answer has no password" answer_lacks_the_password
  check "they sign in and have to change it first" ana_signs_in_and_changes_it
  check "the users come as a page, without any password" users_answer_with_a_page
  check "a user is edited" a_user_is_edited
  check "a taken email is refused" taken_email_is_refused
  check "a common password is refused" common_password_is_refused
  check "a missing permission is refused, without naming it" missing_permission_is_refused
  check "and logged with the permission and the user" refusal_is_logged_with_the_user
  check "nobody gives a role with more than they have" no_one_gives_more_than_they_have
  check "nobody touches someone above them" no_one_touches_who_is_above_them
  check "the user form only offers what they can give" assignable_roles_are_only_what_they_can_give
  check "changing the role ends the user's sessions" role_change_ends_the_sessions
  check "a token from before doesn't manage users any more" old_token_no_longer_manages
  check "the next login has the new role's permissions" next_login_has_the_new_permissions
}

# ---- 8.3

last_admin_stays() {
  admin_id=$(sql "$(connection_to "$check_db")" "select id from users where email = '$admin_email'" | tr -d '\r')
  code_is 409 User.LastAdmin "$(as "$admin_token" POST "/api/users/$admin_id/deactivate")" || return 1
  code_is 409 User.LastAdmin "$(as "$admin_token" PUT "/api/users/$admin_id/role" "{\"roleId\":\"$analyst_role\"}")" || return 1
  is 'True|Admin' "$(sql "$(connection_to "$check_db")" "select u.is_active, r.name from users u join roles r on r.id = u.role_id where u.id = '$admin_id'" | tr -d '\r')"
}

second_admin_lets_the_first_go() {
  admin_role=$(admin_role_id)
  is 201 "$(create_user second.admin@example.com 'Al doilea' "$admin_role")" || return 1
  second_id=$(body_field id)
  is 200 "$(as "$admin_token" POST "/api/users/$second_id/deactivate")" || return 1
  code_is 409 User.LastAdmin "$(as "$admin_token" POST "/api/users/$admin_id/deactivate")"
}

# Two real requests through the real server, many times over: the last two admins never both leave.
racing_requests_leave_an_admin() {
  admin_role=$(admin_role_id)
  for round in 1 2 3 4 5 6 7 8; do
    is 201 "$(create_user "race$round@example.com" "Cursa $round" "$admin_role")" || return 1
    racer=$(body_field id)
    curl -sS -Z --parallel-immediate --max-time 30 -o /dev/null -o /dev/null -X POST -H "Authorization: Bearer $admin_token"       "$API_URL/api/users/$admin_id/deactivate" "$API_URL/api/users/$racer/deactivate" || return 1
    active=$(sql "$(connection_to "$check_db")" "select count(*) from users u join roles r on r.id = u.role_id where r.is_system and u.is_active" | tr -d '\r')
    [ "$active" -ge 1 ] || {
      echo "no active admin left after round $round"
      return 1
    }
    # Whoever is left carries on: the admin token only reads the database through its own user.
    if [ "$(sql "$(connection_to "$check_db")" "select is_active from users where id = '$admin_id'" | tr -d '\r')" = False ]; then
      admin_id=$racer
      is 200 "$(login "race$round@example.com" "$initial_password")" || return 1
      forced=$(body_field accessToken)
      is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$new_admin_password\"}")" || return 1
      admin_token=$(body_field accessToken)
    fi
  done
}

# A database that stopped at the previous migration, with a role in it, moves on without losing anything.
migration_keeps_existing_roles() {
  need_database || return 1
  database="${check_db}_upgrade"
  sql "$admin_connection" "CREATE DATABASE $database" || return 1
  connection=$(connection_to "$database")
  dotnet tool restore >/dev/null || return 1

  (cd backend && dotnet ef database update 20261105064818_AddFailedLoginAttempts -p src/Kanabol.Infrastructure -s src/Kanabol.Api --connection "$connection") >"$scratch/upgrade.log" 2>&1 || {
    tail -n 10 "$scratch/upgrade.log"
    return 1
  }
  sql "$connection" "insert into roles (id, name, is_system, created_at) values ('019a0000-0000-7000-8000-000000000001', 'Old', false, now())" || return 1

  (cd backend && dotnet ef database update -p src/Kanabol.Infrastructure -s src/Kanabol.Api --connection "$connection") >>"$scratch/upgrade.log" 2>&1 || {
    tail -n 10 "$scratch/upgrade.log"
    return 1
  }
  is 'Old|0' "$(sql "$connection" "select name, holders_version from roles" | tr -d '\r')"
}

task_3() {
  echo "8.3 The last admin"
  check "the guard is tracked" tracked backend/src/Kanabol.Application/Features/Users/LastAdminGuard.cs
  check "its tests pass, with the requests that race" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~LastAdminTests' 14
  check "the migration keeps existing roles" migration_keeps_existing_roles
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  [ -n "${analyst_role:-}" ] || task_1 >/dev/null
  check "the last admin can't be deactivated or moved" last_admin_stays
  check "a second admin can go, and the first still can't" second_admin_lets_the_first_go
  check "requests racing on the real server always leave an admin" racing_requests_leave_an_admin
}

# ---- 8.4

creates_and_signs_in_bob() {
  is 201 "$(create_user bob@example.com Bob "$manager_role")" || return 1
  bob_id=$(body_field id)
  is 200 "$(login bob@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$bob_password\"}")" || return 1
  bob_token=$(body_field accessToken)
  bob_refresh=$(refresh_cookie)
  is 200 "$(as "$bob_token" GET /api/users)"
}

bob_is_deactivated() {
  is 200 "$(as "$admin_token" POST "/api/users/$bob_id/deactivate")" || return 1
  in_file '"isActive":false' "$scratch/body"
}

row_stays() {
  is 'False|bob@example.com' "$(sql "$(connection_to "$check_db")" "select is_active, email from users where id = '$bob_id'" | tr -d '\r')"
}

sessions_are_over() {
  is 0 "$(sql "$(connection_to "$check_db")" "select count(*) from refresh_tokens where user_id = '$bob_id' and revoked_at is null" | tr -d '\r')" || return 1
  is 401 "$(send POST /api/auth/refresh -H "Cookie: refresh_token=$bob_refresh")"
}

login_is_refused() {
  code_is 401 Auth.InvalidCredentials "$(login bob@example.com "$bob_password")"
}

token_no_longer_manages() {
  code_is 401 Auth.SessionEnded "$(as "$bob_token" POST /api/users "{\"email\":\"cont.nou@example.com\",\"displayName\":\"Nou\",\"roleId\":\"$manager_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  is 0 "$(sql "$(connection_to "$check_db")" "select count(*) from users where email = 'cont.nou@example.com'" | tr -d '\r')"
}

email_stays_taken() {
  code_is 409 User.EmailTaken "$(create_user bob@example.com 'Alt Bob' "$manager_role")"
}

no_errors_in_the_log() {
  if grep -E 'Unhandled exception' "$api_log"; then
    return 1
  fi
}

passwords_stay_out_of_the_database() {
  is 0 "$(sql "$(connection_to "$check_db")" "select count(*) from users where password_hash like '%$initial_password%' or password_hash like '%$ana_password%'" | tr -d '\r')"
}

swagger_is_closed_in_production() {
  is 404 "$(send GET /swagger/v1/swagger.json)"
}

swagger_offers_a_token_in_development() {
  stop_dev_api
  start_api Development "$check_db" "$dev_port" "$scratch/dev-api.log" || return 1
  dev_pid=$started_pid
  curl -sS --max-time 30 -o "$scratch/swagger.json" "http://127.0.0.1:$dev_port/swagger/v1/swagger.json" || return 1
  in_file '"securitySchemes"' "$scratch/swagger.json" || return 1
  in_file '"bearer"' "$scratch/swagger.json" || return 1
  in_file '/api/users/{id}/deactivate' "$scratch/swagger.json" || return 1
  is 200 "$(curl -sS --max-time 30 -o /dev/null -w '%{http_code}' "http://127.0.0.1:$dev_port/swagger/index.html")"
}

task_4() {
  echo "8.4 Deactivation, and what the logs keep"
  check "the handler's tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~DeactivateUserCommandHandlerTests' 8
  check "the flow tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~DeactivationFlowTests' 10
  check "Swagger's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~SwaggerDocumentTests' 7
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  [ -n "${manager_role:-}" ] || task_1 >/dev/null
  check "a user with users.manage signs in" creates_and_signs_in_bob
  check "they are deactivated" bob_is_deactivated
  check "their row stays" row_stays
  check "their sessions are over" sessions_are_over
  check "they can't sign in" login_is_refused
  check "their token, still valid, can't make them a new account" token_no_longer_manages
  check "their email stays taken" email_stays_taken
  check "the first password isn't in the log" log_lacks "$admin_password"
  check "nor the new one" log_lacks "$new_admin_password"
  check "nor an initial password" log_lacks "$initial_password"
  check "nor one chosen later" log_lacks "$ana_password"
  check "nor a token" log_lacks "${bob_token:-no token}"
  check "a role change is logged" log_has "moved to role"
  check "a deactivation is logged" log_has "deactivated, which ended"
  check "no password is stored as text" passwords_stay_out_of_the_database
  check "the log has no unhandled exception" no_errors_in_the_log
  check "Swagger stays closed in Production" swagger_is_closed_in_production
  check "the real API is still up after all that" api_still_answers
  check "Swagger offers a token field in Development" swagger_offers_a_token_in_development
  stop_dev_api
  stop_api # They hold the files a full build replaces.
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
}

api_still_answers() {
  is 200 "$(send GET /health)"
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4
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
