#!/bin/sh
# Acceptance checks for projects (group 11 in the project's Tasks.md).
# The tests hold the rules, so these checks run them, and add what they can't see: the migration on a
# database that already has projects, and the real API process in Production on a real PostgreSQL,
# with its real seed, its logs and its answers.
#
# Usage: scripts/check-projects.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The tests need Docker running. The real API and the migration need
# a PostgreSQL whose user may create databases, with its connection string in KANABOL_CHECK_DB or else in
# the API's user secrets (ConnectionStrings:Default). Each run works in its own databases and drops them.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_projects_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5286
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
  printf '10.79.%s.%s' "$((used / 256))" "$((used % 256))"
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

# ---- 11.1

migration_is_tracked() {
  tracked_glob "$migrations/*_AddProjectKey.cs"
}

migration_sql_is_right() {
  ef migrations script GuardUserActivation AddProjectKey -o "$scratch/add-project-key.sql" || return 1
  for line in \
    'ALTER TABLE projects ADD key character varying(5) NOT NULL DEFAULT '"''"';' \
    'ALTER TABLE projects ADD next_task_number integer NOT NULL DEFAULT 1;' \
    'CREATE UNIQUE INDEX ux_projects_key ON projects (key);'; do
    in_file "$line" "$scratch/add-project-key.sql" || {
      echo "not in the migration: $line"
      return 1
    }
  done
}

# A database that had projects before keys existed: each one gets its own key, and the index holds.
old_projects_get_keys() {
  need_database || return 1
  old_db="${check_db}_old"
  sql "$admin_connection" "CREATE DATABASE $old_db" || return 1
  ef database update GuardUserActivation --connection "$(connection_to "$old_db")" >"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  sql "$(connection_to "$old_db")" "INSERT INTO projects (id, name, created_at) SELECT gen_random_uuid(), 'Vechi ' || n, now() FROM generate_series(1, 30) AS n" || return 1
  ef database update --connection "$(connection_to "$old_db")" >>"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  is '30|30|30|30' "$(sql "$(connection_to "$old_db")" "SELECT count(*), count(DISTINCT key), count(*) FILTER (WHERE key ~ '^[A-Z]{2,5}\$'), count(*) FILTER (WHERE next_task_number = 1) FROM projects" | tr -d '\r')"
}

creates_kanabol() {
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Kanabol","key":"KAN","description":"Board-ul echipei"}')" || return 1
  kan_id=$(body_field id)
  is "/api/projects/$kan_id" "$(location_header)" || return 1
  in_file '"key":"KAN"' "$scratch/body" && in_file '"myRole":"Owner"' "$scratch/body" && in_file '"canManage":true' "$scratch/body"
}

kanabol_has_its_board() {
  is 'De făcut|0|False
În lucru|1|False
Gata|2|True' "$(in_db "SELECT name, position, is_done FROM board_columns WHERE project_id = '$kan_id' ORDER BY position")"
}

kanabol_is_owned_by_its_creator() {
  is "Owner|admin@kanabol.local|1" "$(in_db "SELECT m.role, u.email, p.next_task_number FROM project_members m JOIN users u ON u.id = m.user_id JOIN projects p ON p.id = m.project_id WHERE m.project_id = '$kan_id'")"
}

a_small_key_is_stored_in_capitals() {
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Site","key":"web"}')" || return 1
  in_file '"key":"WEB"' "$scratch/body"
}

a_taken_key_is_refused_whatever_its_case() {
  code_is 409 Project.KeyTaken "$(as "$admin_token" POST /api/projects '{"name":"Altul","key":"kan"}')"
}

a_wrong_key_is_refused() {
  code_is 400 Validation "$(as "$admin_token" POST /api/projects '{"name":"Altul","key":"K-1"}')" || return 1
  in_file '"key":' "$scratch/body"
}

creating_needs_projects_create() {
  code_is 403 Auth.PermissionRequired "$(as "$dan_token" POST /api/projects '{"name":"Al lui Dan","key":"DAN"}')"
}

an_outsider_sees_nothing() {
  code_is 403 Project.MemberRequired "$(as "$dan_token" GET "/api/projects/$kan_id")"
}

dan_is_added_as_a_member() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$kan_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  is "/api/projects/$kan_id/members/$dan_id" "$(location_header)" || return 1
  in_file '"role":"Member"' "$scratch/body"
}

a_member_sees_it_but_cannot_change_it() {
  status_is 200 "$(as "$dan_token" GET "/api/projects/$kan_id")" || return 1
  in_file '"myRole":"Member"' "$scratch/body" && in_file '"canManage":false' "$scratch/body" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$kan_id/members")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" PUT "/api/projects/$kan_id" '{"name":"Al lui Dan"}')"
}

the_last_owner_stays() {
  code_is 409 Project.LastOwner "$(as "$admin_token" DELETE "/api/projects/$kan_id/members/$admin_id")" || return 1
  code_is 409 Project.LastOwner "$(as "$admin_token" PUT "/api/projects/$kan_id/members/$admin_id" '{"role":"Member"}')" || return 1
  is 1 "$(in_db "SELECT count(*) FROM project_members WHERE project_id = '$kan_id' AND role = 'Owner'")"
}

an_owner_can_hand_over_and_take_back() {
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$kan_id/members/$dan_id" '{"role":"Owner"}')" || return 1
  in_file '"role":"Owner"' "$scratch/body" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$kan_id/members/$dan_id" '{"role":"Member"}')"
}

a_removed_member_loses_access() {
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$kan_id/members/$dan_id")" || return 1
  code_is 403 Project.MemberRequired "$(as "$dan_token" GET "/api/projects/$kan_id")"
}

the_project_is_renamed_but_keeps_its_key() {
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$kan_id" '{"name":"Kanabol v1","key":"ALT","description":null}')" || return 1
  in_file '"name":"Kanabol v1"' "$scratch/body" && in_file '"key":"KAN"' "$scratch/body"
}

archiving_keeps_the_row() {
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$kan_id")" || return 1
  is "1|1|3" "$(in_db "SELECT count(*) FILTER (WHERE archived_at IS NOT NULL), (SELECT count(*) FROM project_members WHERE project_id = '$kan_id'), (SELECT count(*) FROM board_columns WHERE project_id = '$kan_id') FROM projects WHERE id = '$kan_id'")"
}

an_archived_project_opens_but_does_not_change() {
  status_is 200 "$(as "$admin_token" GET "/api/projects/$kan_id")" || return 1
  in_file '"archivedAt":"' "$scratch/body" || return 1
  code_is 409 Project.Archived "$(as "$admin_token" PUT "/api/projects/$kan_id" '{"name":"Altceva"}')"
}

restoring_brings_it_back() {
  status_is 200 "$(as "$admin_token" POST "/api/projects/$kan_id/restore")" || return 1
  in_file '"archivedAt":null' "$scratch/body"
}

admin_id_is_known() {
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  [ -n "$admin_id" ]
}

task_1() {
  echo "11.1 Projects, their key, members and board"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked "$api_dir/Controllers/ProjectsController.cs"
  check "the migration is tracked" migration_is_tracked
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the entity's tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~ProjectTests|FullyQualifiedName~TestDataBuilderTests' 41
  check "the handlers' tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~Features.Projects' 123
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~Kanabol.Api.Tests.Projects|FullyQualifiedName~EndpointPermissionsTests' 34
  check "the model has no changes without a migration" ef migrations has-pending-model-changes
  check "the migration adds the key, the counter from 1 and ux_projects_key" migration_sql_is_right
  check "projects made before it get keys of their own" old_projects_get_keys
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a user without permissions signs in" dan_is_ready
  check "a project is created, with its Location" creates_kanabol
  check "its board has De făcut, În lucru and Gata, which is done" kanabol_has_its_board
  check "its creator is its only owner, and numbers start at 1" kanabol_is_owned_by_its_creator
  check "a key in small letters is stored in capitals" a_small_key_is_stored_in_capitals
  check "a taken key is refused, whatever its case" a_taken_key_is_refused_whatever_its_case
  check "a key that isn't 2 to 5 letters is refused" a_wrong_key_is_refused
  check "creating one needs projects.create" creating_needs_projects_create
  check "someone outside the project can't open it" an_outsider_sees_nothing
  check "an owner adds a member, with its Location" dan_is_added_as_a_member
  check "the member sees it, but can't change it" a_member_sees_it_but_cannot_change_it
  check "the admin's id is known" admin_id_is_known
  check "the last owner can't be removed or made a member" the_last_owner_stays
  check "an owner makes a member an owner, and back" an_owner_can_hand_over_and_take_back
  check "a removed member loses access" a_removed_member_loses_access
  check "a project is renamed, and keeps its key" the_project_is_renamed_but_keeps_its_key
  check "DELETE archives it, and keeps its members and columns" archiving_keeps_the_row
  check "an archived project opens, but doesn't change" an_archived_project_opens_but_does_not_change
  check "it's restored" restoring_brings_it_back
  check "the creation is logged, with the key" log_has_entry "Project {ProjectId} created with key {ProjectKey}" '"ProjectKey":"KAN"'
  check "and the archive" log_has_entry "Project {ProjectId} archived" "\"ProjectId\":\"$kan_id\""
  check "and the restore" log_has_entry "Project {ProjectId} restored" "\"ProjectId\":\"$kan_id\""
  check "and the member who left" log_has_entry "User {MemberUserId} removed from project {ProjectId}" "\"MemberUserId\":\"$dan_id\""
  check "the log has no unhandled exception" no_errors_in_the_log
}

if [ "$#" -eq 0 ]; then
  set -- 1
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
