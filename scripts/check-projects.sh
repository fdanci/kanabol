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
  is 'Backlog|0|Backlog
În lucru|1|InProgress
În pauză|2|Paused
Gata|3|Done' "$(in_db "SELECT name, position, kind FROM board_columns WHERE project_id = '$kan_id' ORDER BY position")"
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
  is "1|1|4" "$(in_db "SELECT count(*) FILTER (WHERE archived_at IS NOT NULL), (SELECT count(*) FROM project_members WHERE project_id = '$kan_id'), (SELECT count(*) FROM board_columns WHERE project_id = '$kan_id') FROM projects WHERE id = '$kan_id'")"
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
  check "its board has the four fixed columns, Backlog, În lucru, În pauză and Gata" kanabol_has_its_board
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

# ---- 11.2

number_field() { # number_field <name>: the value of a number field in the last answer
  sed -n "s/.*\"$1\":\([0-9]*\).*/\1/p" "$scratch/body" | head -n 1
}

keys_listed() { # The project keys in the last answer, in order, on one line
  grep -o '"key":"[A-Z]*"' "$scratch/body" | sed 's/"key":"\([A-Z]*\)"/\1/' | paste -sd' ' -
}

creates_list_projects() {
  for key in LST ALT ARH; do
    status_is 201 "$(as "$admin_token" POST /api/projects "{\"name\":\"Lista $key\",\"key\":\"$key\"}")" || return 1
    eval "${key}_id=\$(body_field id)"
  done
  [ -n "$LST_id" ] && [ -n "$ARH_id" ]
}

someone_in_no_project_sees_none() {
  status_is 200 "$(as "$dan_token" GET /api/projects)" || return 1
  is 0 "$(number_field totalCount)"
}

a_member_sees_only_theirs() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$LST_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  status_is 200 "$(as "$dan_token" GET '/api/projects?page=1&pageSize=25')" || return 1
  is 1 "$(number_field totalCount)" || return 1
  is 25 "$(number_field pageSize)" || return 1
  is LST "$(keys_listed)" || return 1
  in_file '"myRole":"Member"' "$scratch/body"
}

view_all_sees_every_open_one() {
  open=$(in_db "SELECT count(*) FROM projects WHERE archived_at IS NULL")
  status_is 200 "$(as "$admin_token" GET /api/projects)" || return 1
  is "$open" "$(number_field totalCount)" || return 1
  is 50 "$(number_field pageSize)"
}

# upper() of name follows its ro-x-icu collation, as the API's sort does (docs/SPEC.md §5.3).
pages_go_by_name() {
  expected=$(in_db "SELECT key FROM projects WHERE archived_at IS NULL ORDER BY upper(name), key OFFSET 1 LIMIT 1")
  status_is 200 "$(as "$admin_token" GET '/api/projects?page=2&pageSize=1')" || return 1
  is "$expected" "$(keys_listed)"
}

archived_ones_only_when_asked() {
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$ARH_id")" || return 1
  status_is 200 "$(as "$admin_token" GET '/api/projects?pageSize=100')" || return 1
  case " $(keys_listed) " in *" ARH "*) echo "ARH is listed although archived"; return 1 ;; esac
  status_is 200 "$(as "$admin_token" GET '/api/projects?pageSize=100&includeArchived=true')" || return 1
  case " $(keys_listed) " in *" ARH "*) ;; *) echo "ARH isn't listed with includeArchived"; return 1 ;; esac
}

tasks_are_counted() {
  todo=$(in_db "SELECT id FROM board_columns WHERE project_id = '$LST_id' AND position = 0")
  done_column=$(in_db "SELECT id FROM board_columns WHERE project_id = '$LST_id' AND kind = 'Done'")
  in_db "INSERT INTO task_items (id, project_id, number, column_id, title, priority, position, due_date, created_at) VALUES
    (gen_random_uuid(), '$LST_id', 1, '$todo', 'Deschis', 'Medium', 0, NULL, now()),
    (gen_random_uuid(), '$LST_id', 2, '$todo', 'Intarziat', 'High', 1, now() - interval '1 day', now()),
    (gen_random_uuid(), '$LST_id', 3, '$done_column', 'Gata la timp', 'Low', 0, now() - interval '1 day', now()),
    (gen_random_uuid(), '$LST_id', 4, '$todo', 'Arhivat', 'Low', 2, now() - interval '1 day', now())" >/dev/null || return 1
  in_db "UPDATE task_items SET archived_at = now() WHERE title = 'Arhivat'" >/dev/null || return 1
  status_is 200 "$(as "$dan_token" GET /api/projects)" || return 1
  is 2 "$(number_field openTaskCount)" || return 1
  is 1 "$(number_field overdueTaskCount)"
}

a_page_over_100_is_refused() {
  code_is 400 Validation "$(as "$admin_token" GET '/api/projects?pageSize=101')"
}

task_2() {
  echo "11.2 The list of projects"
  check "Docker is running" docker_is_running
  check "the query's tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetProjectsQueryHandlerTests' 15
  check "the endpoint's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ProjectListEndpointTests|FullyQualifiedName~EndpointPermissionsTests' 11
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a user without permissions signs in" dan_is_ready
  check "three projects are created for the list" creates_list_projects
  check "someone in no project sees none" someone_in_no_project_sees_none
  check "a member sees only theirs, as a Page" a_member_sees_only_theirs
  check "projects.view_all sees every open one, 50 to a page" view_all_sees_every_open_one
  check "pages go by name" pages_go_by_name
  check "archived ones are listed only when asked for" archived_ones_only_when_asked
  check "open and overdue tasks are counted, done and archived ones aren't" tasks_are_counted
  check "a page of more than 100 is refused" a_page_over_100_is_refused
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 11.3: the frontend, from its specs, its routes, its texts and its production build.

web=frontend
app=$web/src/app
projects=$app/features/projects

plain() { # Without the colors the tools print
  sed 's/\x1b\[[0-9;]*m//g' "$1"
}

lacks() { # lacks <extended regex> <file...>
  ! grep -nE -- "$@" || return 1
}

specs_pass() { # specs_pass <spec file or glob, from frontend/> <minimum passed>
  log="$scratch/specs-$(printf '%s' "$1" | tr '/*' '__').log"
  (cd "$web" && npx ng test --no-watch --include "$1") >"$log" 2>&1 || {
    plain "$log" | tail -n 25
    return 1
  }
  passed=$(plain "$log" | sed -n 's/^ *Tests *\([0-9]*\) passed.*/\1/p' | tail -n 1)
  [ "${passed:-0}" -ge "$2" ] || {
    echo "only ${passed:-0} specs passed, expected at least $2"
    return 1
  }
}

# The production build, made once per run.
built() {
  [ -f "$scratch/built" ] && return 0
  (cd "$web" && npx ng build --output-path "$scratch/dist") >"$scratch/build.log" 2>&1 || {
    plain "$scratch/build.log" | tail -n 25
    return 1
  }
  cat "$scratch"/dist/browser/*.js >"$scratch/bundle.js" && touch "$scratch/built"
}

bundle_has() { # bundle_has <text>
  built && in_file "$1" "$scratch/bundle.js"
}

css_has() { # css_has <text>: compares without whitespace, which the minifier keeps in places
  built || return 1
  tr -d '
' <"$(ls "$scratch"/dist/browser/styles-*.css | head -n 1)" | grep -qF -- "$1" || {
    echo "not in the built CSS: $1"
    return 1
  }
}

# The texts as ng extract-i18n writes them for translation, made once per run.
extracted() {
  [ -f "$scratch/locale/messages.xlf" ] && return 0
  (cd "$web" && npx ng extract-i18n --output-path "$scratch/locale") >"$scratch/extract.log" 2>&1 || {
    plain "$scratch/extract.log" | tail -n 25
    return 1
  }
}

messages_have() { # messages_have <text>
  extracted && in_file "$1" "$scratch/locale/messages.xlf"
}

# /projects loads with its first visit, for anyone signed in: the API decides what each one sees.
projects_route_is_lazy_and_open() {
  node -e "
    const source = require('fs').readFileSync('$app/app.routes.ts', 'utf8');
    if (!/path: 'projects',[^}]*loadChildren: \\(\\) =>\\s*import\\('.\\/features\\/projects\\/projects.routes'\\)/.test(source)) {
      console.log('no lazy route for projects in app.routes.ts'); process.exit(1);
    }
    if (/withPermission\\([^,]+, \\{\\s*path: 'projects'/.test(source)) { console.log('projects sits behind a permission'); process.exit(1); }
    if (source.includes(\"placeholder('projects'\")) { console.log('the placeholder for projects is still there'); process.exit(1); }
  "
}

# Each view of a project is a child route of /projects/:id, which opens on the board.
project_views_are_routes() {
  for view in board list members reports settings; do
    in_file "path: '$view'" "$projects/projects.routes.ts" || {
      echo "no route for $view"
      return 1
    }
  done
  in_file "redirectTo: 'board'" "$projects/projects.routes.ts"
}

# Every refusal ProjectErrors.cs names has a Romanian message for the snackbar.
every_project_error_has_a_message() {
  codes=$(grep -o '"Project\.[A-Za-z]*"' backend/src/Kanabol.Application/Features/Projects/ProjectErrors.cs | tr -d '"' | sort -u)
  [ -n "$codes" ] || return 1
  for code in $codes; do
    in_file "'$code':" "$app/core/api/error-interceptor.ts" || {
      echo "no message for $code in error-interceptor.ts"
      return 1
    }
  done
}

# A chip's text on the neutral chip background, color-mix(navy 6%, surface-0) (UI spec 6.5), has to
# pass AA at 12px in both themes, computed from the tokens.
chip_reads() { # chip_reads <kind>
  node -e "
    const fs = require('fs');
    const chip = fs.readFileSync('$app/shared/ui/chip.ts', 'utf8');
    const rule = chip.match(/((?::host\\([a-z-]+\\),?\\s*)*:host\\(\\.$1\\)[^{]*)\\{([^}]*)\\}/);
    const text = rule && (rule[0].match(/--_fg:\\s*var\\(--kb-([a-z0-9-]+)\\)/) || [])[1];
    if (!text) { console.log('no text color for .$1 in chip.ts'); process.exit(1); }
    const source = fs.readFileSync('$web/src/styles/_tokens.scss', 'utf8');
    const split = source.indexOf('@mixin dark');
    const tokens = (part) => Object.fromEntries([...part.matchAll(/--kb-([a-z0-9-]+)\\s*:\\s*(#[0-9a-fA-F]{6})\\s*;/g)].map((m) => [m[1], m[2]]));
    const light = tokens(source.slice(0, split));
    const dark = { ...light, ...tokens(source.slice(split)) };
    const channels = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
    const mix = (a, b, share) => channels(a).map((c, i) => c * share + channels(b)[i] * (1 - share));
    const luminance = (rgb) => {
      const [r, g, b] = rgb.map((c) => c / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    let bad = 0;
    for (const [mode, t] of [['light', light], ['dark', dark]]) {
      const [high, low] = [luminance(channels(t[text])), luminance(mix(t['navy-900'], t['surface-0'], 0.06))].sort((x, y) => y - x);
      const ratio = (high + 0.05) / (low + 0.05);
      if (ratio < 4.5) { console.log(mode + ': ' + text + ' on the neutral chip is ' + ratio.toFixed(2) + ':1'); bad++; }
    }
    process.exit(bad ? 1 : 0);
  "
}

the_archived_chip_is_text_600() {
  node -e "
    const chip = require('fs').readFileSync('$app/shared/ui/chip.ts', 'utf8');
    const rule = chip.match(/((?::host\\([a-z-]+\\),?\\s*)*:host\\(\\.archived\\)[^{]*)\\{([^}]*)\\}/);
    if (!rule || !/--_fg:\\s*var\\(--kb-text-600\\)/.test(rule[0])) { console.log('.archived is not text-600'); process.exit(1); }
    if (/:host\\(\\.archived\\)[^{]*\\{[^}]*archived-400/.test(chip)) { console.log('.archived still uses archived-400'); process.exit(1); }
  "
}

no_colors_by_hand() {
  lacks '#[0-9a-fA-F]{3,8}\b' "$projects"/*.scss "$projects"/*.ts "$app/shared/ui/segmented-nav.ts"
}

no_deep_selectors() {
  lacks '::ng-deep|\.mat-mdc-|\.mdc-' "$projects"/*.scss "$projects"/*.ts "$app/shared/ui/segmented-nav.ts"
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

task_3() {
  echo "11.3 The projects pages"
  for file in models.ts projects-api.ts projects-store.ts project-store.ts project-fields.ts projects-page.ts \
    projects-page.html project-sheet.ts project-sheet.html project-page.ts project-page.html \
    project-members.ts project-members.html member-sheet.ts member-sheet.html project-settings.ts \
    project-settings.html sheet.scss projects.routes.ts; do
    check "features/projects/$file is tracked" tracked "$projects/$file"
  done
  check "shared/ui/segmented-nav.ts is tracked" tracked "$app/shared/ui/segmented-nav.ts"
  check "shared/ui/field.ts is tracked, for every sheet" tracked "$app/shared/ui/field.ts"
  check "shared/pipes/relative-time-pipe.ts is tracked" tracked "$app/shared/pipes/relative-time-pipe.ts"
  check "the specs of the projects pass" specs_pass "src/app/features/projects/*.spec.ts" 63
  check "times read as the UI spec writes them" specs_pass src/app/shared/pipes/relative-time-pipe.spec.ts 5
  check "the users pages still pass, with the shared field" specs_pass "src/app/features/users/*.spec.ts" 23
  check "the shell still opens its pages" specs_pass src/app/core/layout/layout.spec.ts 15
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "/projects loads with its first visit, for anyone signed in" projects_route_is_lazy_and_open
  check "a project has its board, list, members, reports and settings, from the board" project_views_are_routes
  check "every refusal of a project has a Romanian message" every_project_error_has_a_message
  check "the Arhivat chip is text-600, as UI spec v1.7 has it" the_archived_chip_is_text_600
  check "and it passes AA, in both themes" chip_reads archived
  check "as Dezactivat still does" chip_reads inactive
  check "the pages write no colors by hand" no_colors_by_hand
  check "nor reach into Material's classes" no_deep_selectors
  check "a filter toggle is a 26px pill, fill-accent while on" css_has ".kb-filter-chip[aria-pressed=true]{border-color:var(--kb-fill-accent);background:var(--kb-fill-accent)"
  check "the list is named and offers a new project" messages_have 'Proiect nou'
  check "a new project explains its key" messages_have 'Nu se mai schimbă.'
  check "an archived project says so" messages_have 'Acest proiect e arhivat'
  check "a member is added from a search" messages_have 'Adaugă un membru'
  check "the last owner's refusal is explained" messages_have 'Proiectul are nevoie de un owner.'
  check "a closed project says who can open it" messages_have 'Cere unui owner al proiectului să te adauge în el.'
  check "the build asks for whom to add" bundle_has 'member-candidates'
  check "and archives with DELETE, restoring with /restore" bundle_has '/restore'
}

# ---- 11.4: access by role, and the owner a project keeps.

creates_eva_and_her_project() {
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"eva@example.com\",\"displayName\":\"Eva Rusu\",\"roleId\":\"$creator_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  eva_id=$(body_field id)
  status_is 200 "$(login eva@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$dan_password\"}")" || return 1
  eva_token=$(body_field accessToken)
  status_is 201 "$(as "$eva_token" POST /api/projects '{"name":"Al Evei","key":"EVA"}')" || return 1
  eva_project=$(body_field id)
  in_file '"myRole":"Owner"' "$scratch/body"
}

creator_role_exists() {
  status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Creator de proiecte","permissions":["projects.create"]}')" || return 1
  creator_role=$(body_field id)
  [ -n "$creator_role" ]
}

viewer_is_ready() {
  status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Vede tot","permissions":["projects.view_all"]}')" || return 1
  viewer_role=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"vio@example.com\",\"displayName\":\"Vio Pop\",\"roleId\":\"$viewer_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  status_is 200 "$(login vio@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$dan_password\"}")" || return 1
  vio_token=$(body_field accessToken)
  [ -n "$vio_token" ]
}

an_outsider_neither_reads_nor_changes() {
  code_is 403 Project.MemberRequired "$(as "$dan_token" GET "/api/projects/$eva_project")" || return 1
  code_is 403 Project.MemberRequired "$(as "$dan_token" GET "/api/projects/$eva_project/members")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" PUT "/api/projects/$eva_project" '{"name":"Al lui Dan"}')" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" DELETE "/api/projects/$eva_project")"
}

a_member_reads_but_does_not_change() {
  status_is 201 "$(as "$eva_token" POST "/api/projects/$eva_project/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$eva_project/members")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" POST "/api/projects/$eva_project/members" "{\"userId\":\"$admin_id\",\"role\":\"Owner\"}")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" PUT "/api/projects/$eva_project/members/$dan_id" '{"role":"Owner"}')" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" GET "/api/projects/$eva_project/member-candidates")"
}

view_all_reads_but_does_not_change() {
  status_is 200 "$(as "$vio_token" GET "/api/projects/$eva_project")" || return 1
  in_file '"myRole":null' "$scratch/body" && in_file '"canManage":false' "$scratch/body" || return 1
  status_is 200 "$(as "$vio_token" GET "/api/projects/$eva_project/members")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$vio_token" PUT "/api/projects/$eva_project" '{"name":"Al lui Vio"}')"
}

manage_changes_a_project_it_is_not_in() {
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$eva_project" '{"name":"Al Evei, redenumit"}')" || return 1
  in_file '"canManage":true' "$scratch/body" && in_file '"myRole":null' "$scratch/body"
}

the_owner_cannot_leave_alone() {
  code_is 409 Project.LastOwner "$(as "$eva_token" DELETE "/api/projects/$eva_project/members/$eva_id")" || return 1
  code_is 409 Project.LastOwner "$(as "$eva_token" PUT "/api/projects/$eva_project/members/$eva_id" '{"role":"Member"}')"
}

# A deactivated owner (SPEC section 4.4, decided 2026-11-06): deactivating the only owner isn't refused,
# their token still valid opens nothing, and projects.manage hands the project on. Always an owner means
# an active one, so whoever owns it beside a deactivated owner can't leave it or step down.
a_deactivated_owner_is_shut_out() {
  status_is 200 "$(as "$admin_token" POST "/api/users/$eva_id/deactivate")" || return 1
  code_is 403 Project.MemberRequired "$(as "$eva_token" GET "/api/projects/$eva_project")" || return 1
  is "Owner|False" "$(in_db "SELECT m.role, u.is_active FROM project_members m JOIN users u ON u.id = m.user_id WHERE m.project_id = '$eva_project' AND m.user_id = '$eva_id'")"
}

manage_makes_a_new_owner() {
  code_is 409 Project.LastOwner "$(as "$admin_token" DELETE "/api/projects/$eva_project/members/$eva_id")" || return 1
  code_is 409 Project.LastOwner "$(as "$admin_token" PUT "/api/projects/$eva_project/members/$eva_id" '{"role":"Member"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$eva_project/members/$dan_id" '{"role":"Owner"}')"
}

the_owner_beside_a_deactivated_one_stays() {
  code_is 409 Project.LastOwner "$(as "$dan_token" DELETE "/api/projects/$eva_project/members/$dan_id")" || return 1
  code_is 409 Project.LastOwner "$(as "$dan_token" PUT "/api/projects/$eva_project/members/$dan_id" '{"role":"Member"}')" || return 1
  is "Owner|True
Owner|False" "$(in_db "SELECT m.role, u.is_active FROM project_members m JOIN users u ON u.id = m.user_id WHERE m.project_id = '$eva_project' ORDER BY u.is_active DESC")"
}

manage_removes_the_deactivated_owner() {
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$eva_project/members/$eva_id")" || return 1
  is "Owner|dan@example.com" "$(in_db "SELECT m.role, u.email FROM project_members m JOIN users u ON u.id = m.user_id WHERE m.project_id = '$eva_project'")"
}

task_4() {
  echo "11.4 Access by role, and always an active owner"
  check "Docker is running" docker_is_running
  check "every endpoint answers each kind of caller as its role allows" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ProjectAccessMatrixTests' 54
  check "the access rules pass, member, non-member and view_all" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~ProjectAccessTests|FullyQualifiedName~ProjectQueryHandlerTests|FullyQualifiedName~GetProjectsQueryHandlerTests' 50
  check "the last owner stays, also when two requests race" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~ProjectMemberCommandHandlerTests|FullyQualifiedName~ConcurrentMemberChangesTests' 33
  check "and in the entity" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~ProjectTests' 42
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a user without permissions signs in" dan_is_ready
  check "the admin's id is known" admin_id_is_known
  check "a role that may create projects exists" creator_role_exists
  check "a user creates a project and owns it" creates_eva_and_her_project
  check "a user with projects.view_all signs in" viewer_is_ready
  check "someone outside the project neither reads nor changes it" an_outsider_neither_reads_nor_changes
  check "a member reads it, but changes nothing" a_member_reads_but_does_not_change
  check "projects.view_all reads it, but changes nothing" view_all_reads_but_does_not_change
  check "projects.manage changes a project it isn't in" manage_changes_a_project_it_is_not_in
  check "the only owner can't leave or step down" the_owner_cannot_leave_alone
  check "a deactivated owner's token opens nothing, and they stay owner" a_deactivated_owner_is_shut_out
  check "projects.manage can't remove or demote them, and makes a new owner first" manage_makes_a_new_owner
  check "the new owner can't leave or step down beside the deactivated one" the_owner_beside_a_deactivated_one_stays
  check "projects.manage then removes the deactivated owner" manage_removes_the_deactivated_owner
  check "the log has no unhandled exception" no_errors_in_the_log
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
