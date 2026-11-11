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

# ---- 12.3: the key, a number from the project's counter, and creations that race.

task_number_migration_is_tracked() {
  tracked_glob "$migrations/*_AddTaskNumber.cs"
}

task_number_sql_is_right() {
  ef migrations script AddProjectKey AddTaskNumber -o "$scratch/add-task-number.sql" || return 1
  for line in \
    'ALTER TABLE task_items ADD number integer NOT NULL DEFAULT 0;' \
    'ALTER TABLE task_items ALTER COLUMN number DROP DEFAULT;' \
    'CREATE UNIQUE INDEX ux_task_items_project_id_number ON task_items (project_id, number);'; do
    in_file "$line" "$scratch/add-task-number.sql" || {
      echo "not in the migration: $line"
      return 1
    }
  done
}

# A database that had tasks before numbers existed: each project numbers its own from 1, in the order
# they were made, archived ones too, and its counter goes on after the last.
old_tasks_get_numbers() {
  need_database || return 1
  old_db="${check_db}_old"
  sql "$admin_connection" "CREATE DATABASE $old_db" || return 1
  ef database update AddProjectKey --connection "$(connection_to "$old_db")" >"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  old=$(connection_to "$old_db")
  sql "$old" "INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES
    ('00000000-0000-7000-8000-000000000001', 'Vechi', 'OLD', 1, now()),
    ('00000000-0000-7000-8000-000000000002', 'Altul', 'ALT', 1, now()),
    ('00000000-0000-7000-8000-000000000003', 'Gol', 'GOL', 1, now())" || return 1
  sql "$old" "INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES
    ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-000000000001', 'De făcut', 0, false, now()),
    ('00000000-0000-7000-8000-0000000000c2', '00000000-0000-7000-8000-000000000002', 'De făcut', 0, false, now())" || return 1
  sql "$old" "INSERT INTO task_items (id, project_id, column_id, title, priority, position, created_at, archived_at) VALUES
    (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000c1', 'Al doilea', 'Low', 1, now() - interval '1 day', NULL),
    (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000c1', 'Primul', 'Low', 0, now() - interval '2 days', NULL),
    (gen_random_uuid(), '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-0000000000c1', 'Al treilea, arhivat', 'Low', 2, now(), now()),
    (gen_random_uuid(), '00000000-0000-7000-8000-000000000002', '00000000-0000-7000-8000-0000000000c2', 'Singurul', 'Low', 0, now(), NULL)" || return 1
  ef database update --connection "$old" >>"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  is 'OLD|Primul|1
OLD|Al doilea|2
OLD|Al treilea, arhivat|3
ALT|Singurul|1' "$(sql "$old" "SELECT p.key, t.title, t.number FROM task_items t JOIN projects p ON p.id = t.project_id ORDER BY p.key DESC, t.number" | tr -d '\r')" || return 1
  is 'ALT|2
GOL|1
OLD|4' "$(sql "$old" "SELECT key, next_task_number FROM projects ORDER BY key" | tr -d '\r')" || return 1
  is '' "$(sql "$old" "SELECT column_default FROM information_schema.columns WHERE table_name = 'task_items' AND column_name = 'number'" | tr -d '\r')"
}

tasks_of() { # tasks_of <project id>
  printf '/api/projects/%s/tasks' "$1"
}

creates_the_task_project() {
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Task-uri","key":"TSK"}')" || return 1
  tsk_id=$(body_field id)
  [ -n "$tsk_id" ]
}

the_first_task_is_tsk_1() {
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$tsk_id")" '{"title":"Login SSO","priority":"High","description":"Cu **Azure AD**"}')" || return 1
  first_task=$(body_field id)
  is "/api/tasks/$first_task" "$(location_header)" || return 1
  in_file '"key":"TSK-1"' "$scratch/body" && in_file '"number":1' "$scratch/body" && in_file '"priority":"High"' "$scratch/body"
}

the_next_one_is_tsk_2_at_the_end() {
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$tsk_id")" '{"title":"Al doilea","priority":"Low"}')" || return 1
  in_file '"key":"TSK-2"' "$scratch/body" && in_file '"position":1' "$scratch/body" || return 1
  is "2" "$(in_db "SELECT next_task_number - 1 FROM projects WHERE id = '$tsk_id'")"
}

the_key_opens_it_in_any_case() {
  status_is 200 "$(as "$admin_token" GET /api/tasks/by-key/tsk-1)" || return 1
  in_file "\"id\":\"$first_task\"" "$scratch/body" && in_file '"title":"Login SSO"' "$scratch/body"
}

a_key_no_task_has_is_not_found() {
  code_is 404 Task.NotFound "$(as "$admin_token" GET /api/tasks/by-key/TSK-99)" || return 1
  code_is 404 Task.NotFound "$(as "$admin_token" GET /api/tasks/by-key/nu-e-o-cheie)"
}

creating_needs_tasks_create() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$tsk_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  code_is 403 Auth.PermissionRequired "$(as "$dan_token" POST "$(tasks_of "$tsk_id")" '{"title":"Al lui Dan","priority":"Low"}')" || return 1
  status_is 200 "$(as "$dan_token" GET /api/tasks/by-key/TSK-1)"
}

# projects.view_all sees the project, but adds tasks only once it's a member (SPEC section 4.4).
viewer_needs_a_real_membership() {
  status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Vede tot si creeaza","permissions":["projects.view_all","tasks.create"]}')" || return 1
  role=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"vio@example.com\",\"displayName\":\"Vio Pop\",\"roleId\":\"$role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  vio_id=$(body_field id)
  status_is 200 "$(login vio@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$dan_password\"}")" || return 1
  vio_token=$(body_field accessToken)
  status_is 200 "$(as "$vio_token" GET "/api/projects/$tsk_id")" || return 1
  code_is 403 Project.MemberRequired "$(as "$vio_token" POST "$(tasks_of "$tsk_id")" '{"title":"Al lui Vio","priority":"Low"}')" || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$tsk_id/members" "{\"userId\":\"$vio_id\",\"role\":\"Member\"}")" || return 1
  status_is 201 "$(as "$vio_token" POST "$(tasks_of "$tsk_id")" '{"title":"Al lui Vio","priority":"Low"}')" || return 1
  in_file '"key":"TSK-3"' "$scratch/body"
}

# Creations sent at the same moment, by one curl that doesn't wait for one before the next: each gets its
# own number, and none of them sees a 409 (SPEC sections 5.1 and 5.3). The number comes from one
# UPDATE … RETURNING in the transaction of the creation, with no retry (decided 2026-11-06, task 13.1),
# so ten at once give ten 201s; with the three retries of group 12 they gave two 409s.
ten_at_once_get_ten_numbers() { # ten_at_once_get_ten_numbers [project key]: PAR unless given
  key=${1:-PAR}
  status_is 201 "$(as "$admin_token" POST /api/projects "{\"name\":\"In paralel $key\",\"key\":\"$key\"}")" || return 1
  par_id=$(body_field id)
  url="$API_URL$(tasks_of "$par_id")"
  curl -sS --max-time 60 -Z --parallel-immediate --parallel-max 10 \
    -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' \
    -d '{"title":"In paralel","priority":"Low"}' -w '\nSTATUS %{http_code}\n' \
    "$url" "$url" "$url" "$url" "$url" "$url" "$url" "$url" "$url" "$url" >"$scratch/parallel" 2>&1 || {
    tail -n 5 "$scratch/parallel"
    return 1
  }
  is 10 "$(grep -c '^STATUS 201$' "$scratch/parallel")" || {
    grep '^STATUS' "$scratch/parallel" | sort | uniq -c
    grep -o '"code":"[^"]*"' "$scratch/parallel" | sort | uniq -c
    return 1
  }
  is 10 "$(grep -o "\"key\":\"$key-[0-9]*\"" "$scratch/parallel" | sort -u | wc -l | tr -d ' ')" || return 1
  is '10|10|1|10|11|10' "$(in_db "SELECT count(*), count(DISTINCT t.number), min(t.number), max(t.number), max(p.next_task_number), count(DISTINCT t.position) FROM task_items t JOIN projects p ON p.id = t.project_id WHERE t.project_id = '$par_id'")"
}

task_3() {
  echo "12.3 The task key, from the project's counter, and creations that race"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked "$api_dir/Controllers/TasksController.cs"
  check "the migration is tracked" task_number_migration_is_tracked
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the key's and the creation's domain tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskKeyTests|FullyQualifiedName~TaskCreationTests' 30
  check "the handlers' tests pass, two creations at once among them" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~CreateTaskCommandHandlerTests|FullyQualifiedName~TaskQueryHandlerTests' 43
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~Kanabol.Api.Tests.Tasks|FullyQualifiedName~EndpointPermissionsTests' 18
  check "the model has no changes without a migration" ef migrations has-pending-model-changes
  check "the migration adds the number, without a default, and ux_task_items_project_id_number" task_number_sql_is_right
  check "tasks made before it get numbers, and projects a counter after them" old_tasks_get_numbers
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a user without permissions signs in" dan_is_ready
  check "a project for the tasks is created" creates_the_task_project
  check "its first task is TSK-1, with its Location" the_first_task_is_tsk_1
  check "the next one is TSK-2, at the end of its column" the_next_one_is_tsk_2_at_the_end
  check "GET /api/tasks/by-key opens it, in any case" the_key_opens_it_in_any_case
  check "a key no task has is not found" a_key_no_task_has_is_not_found
  check "a member without tasks.create can't create one, but reads them" creating_needs_tasks_create
  check "projects.view_all with tasks.create creates one only once a member" viewer_needs_a_real_membership
  check "ten creations at the same moment get ten numbers, and no 409" ten_at_once_get_ten_numbers
  check "the creation is logged, with the key" log_has_entry "Task {TaskId} created as {TaskKey} in project {ProjectId}" '"TaskKey":"TSK-1"'
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 12.4: changing a task, assigning it, archiving and restoring it, and the project's list.

creates_the_edit_project() {
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Editare","key":"EDT"}')" || return 1
  edt_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$edt_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$edt_id")" '{"title":"Login SSO","priority":"Medium"}')" || return 1
  edt_task=$(body_field id)
  [ -n "$edt_task" ]
}

each_change_is_saved_and_written_down() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$edt_task/title" '{"title":"Login cu SSO"}')" || return 1
  in_file '"title":"Login cu SSO"' "$scratch/body" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$edt_task/description" '{"description":"Cu **Azure AD**"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$edt_task/priority" '{"priority":"Critical"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$edt_task/due-date" '{"dueDate":"2027-01-10T09:00:00+02:00"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$edt_task/assignees" "{\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  in_file "\"assignees\":[{\"userId\":\"$dan_id\",\"displayName\":\"Dan Ionescu\"}]" "$scratch/body" || return 1
  is "Title|Login SSO|Login cu SSO
Description||Cu **Azure AD**
Priority|Medium|Critical
DueDate||2027-01-10T07:00:00.0000000+00:00
Assignees||$dan_id" "$(in_db "SELECT field, coalesce(old_value, ''), coalesce(new_value, '') FROM task_history_entries WHERE task_id = '$edt_task' ORDER BY changed_at, CASE field WHEN 'Title' THEN 1 WHEN 'Description' THEN 2 WHEN 'Priority' THEN 3 WHEN 'DueDate' THEN 4 ELSE 5 END")" || return 1
  is "$admin_id" "$(in_db "SELECT DISTINCT changed_by_id FROM task_history_entries WHERE task_id = '$edt_task'")"
}

the_same_value_writes_nothing() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$edt_task/priority" '{"priority":"Critical"}')" || return 1
  is 5 "$(in_db "SELECT count(*) FROM task_history_entries WHERE task_id = '$edt_task'")"
}

only_a_member_is_assigned() {
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"out@example.com\",\"displayName\":\"Din afara\",\"roleId\":\"$plain_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  outsider=$(body_field id)
  code_is 400 Validation "$(as "$admin_token" PUT "/api/tasks/$edt_task/assignees" "{\"assigneeIds\":[\"$outsider\"]}")" || return 1
  in_file '"assigneeIds":' "$scratch/body" || return 1
  is "$dan_id" "$(in_db "SELECT user_id FROM task_assignees WHERE task_id = '$edt_task'")"
}

a_member_without_permissions_changes_it() {
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$edt_task/priority" '{"priority":"Low"}')" || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$edt_task/assignees" '{"assigneeIds":[]}')" || return 1
  in_file '"assignees":[]' "$scratch/body"
}

an_outsider_neither_reads_nor_changes_it() {
  status_is 200 "$(login out@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$dan_password\"}")" || return 1
  out_token=$(body_field accessToken)
  code_is 403 Project.MemberRequired "$(as "$out_token" GET "/api/tasks/$edt_task")" || return 1
  code_is 403 Project.MemberRequired "$(as "$out_token" PUT "/api/tasks/$edt_task/title" '{"title":"Al meu"}')" || return 1
  code_is 403 Project.MemberRequired "$(as "$out_token" GET "$(tasks_of "$edt_id")")"
}

delete_archives_and_keeps_the_row() {
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$edt_id")" '{"title":"De arhivat","priority":"Low"}')" || return 1
  gone=$(body_field id)
  status_is 204 "$(as "$dan_token" DELETE "/api/tasks/$gone")" || return 1
  is "1|$dan_id|1" "$(in_db "SELECT count(*) FILTER (WHERE archived_at IS NOT NULL), max(archived_by_id::text), (SELECT count(*) FROM task_history_entries WHERE task_id = '$gone' AND field = 'ArchivedAt') FROM task_items WHERE id = '$gone'")" || return 1
  code_is 404 Task.NotFound "$(as "$dan_token" GET "/api/tasks/$gone")"
}

the_list_leaves_archived_ones_out() {
  status_is 200 "$(as "$dan_token" GET "$(tasks_of "$edt_id")")" || return 1
  is 1 "$(number_field totalCount)" || return 1
  in_file '"key":"EDT-1"' "$scratch/body"
}

archived_ones_need_tasks_archive() {
  code_is 403 Auth.PermissionRequired "$(as "$dan_token" GET "$(tasks_of "$edt_id")?includeArchived=true")" || return 1
  code_is 403 Auth.PermissionRequired "$(as "$dan_token" POST "/api/tasks/$gone/restore")" || return 1
  status_is 200 "$(as "$admin_token" GET "$(tasks_of "$edt_id")?includeArchived=true")" || return 1
  is 2 "$(number_field totalCount)"
}

tasks_archive_restores_it() {
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$gone/restore")" || return 1
  in_file '"archivedAt":null' "$scratch/body" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/tasks/$gone")"
}

the_list_filters_and_sorts() {
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$edt_id")" "{\"title\":\"Raport 100% gata\",\"priority\":\"High\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"2020-01-01T00:00:00Z\"}")" || return 1
  status_is 200 "$(as "$dan_token" GET "$(tasks_of "$edt_id")?assigneeId=$dan_id")" || return 1
  is EDT-3 "$(keys_listed)" || return 1
  status_is 200 "$(as "$dan_token" GET "$(tasks_of "$edt_id")?priority=High&priority=Low&sort=key:asc")" || return 1
  is 'EDT-1 EDT-2 EDT-3' "$(keys_listed)" || return 1
  status_is 200 "$(as "$dan_token" GET "$(tasks_of "$edt_id")?overdue=true")" || return 1
  is EDT-3 "$(keys_listed)" || return 1
  status_is 200 "$(as "$dan_token" GET "$(tasks_of "$edt_id")?search=100%25")" || return 1
  is EDT-3 "$(keys_listed)" || return 1
  status_is 200 "$(as "$dan_token" GET "$(tasks_of "$edt_id")?sort=title:asc&pageSize=2")" || return 1
  is 'EDT-2 EDT-1' "$(keys_listed)" || return 1
  code_is 400 Validation "$(as "$dan_token" GET "$(tasks_of "$edt_id")?sort=color:asc")"
}

keys_listed() { # The task keys in the last answer, in order, on one line
  grep -o '"key":"[A-Z]*-[0-9]*"' "$scratch/body" | sed 's/"key":"\([A-Z]*-[0-9]*\)"/\1/' | paste -sd' ' -
}

number_field() { # number_field <name>: the value of a number field in the last answer
  sed -n "s/.*\"$1\":\([0-9]*\).*/\1/p" "$scratch/body" | head -n 1
}

admin_id_is_known() {
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  [ -n "$admin_id" ]
}

task_4() {
  echo "12.4 Changing, assigning, archiving and listing tasks"
  check "Docker is running" docker_is_running
  check "the handlers' tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~TaskChangeCommandHandlerTests|FullyQualifiedName~GetProjectTasksQueryHandlerTests' 62
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~TaskChangesEndpointTests|FullyQualifiedName~EndpointPermissionsTests' 18
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a user without permissions signs in" dan_is_ready
  check "the admin's id is known" admin_id_is_known
  check "a project with a member and a task is created" creates_the_edit_project
  check "title, description, priority, deadline and assignee change, each with its history" each_change_is_saved_and_written_down
  check "the same value again writes no history" the_same_value_writes_nothing
  check "only a member of the project is assigned" only_a_member_is_assigned
  check "a member without permissions changes and unassigns it" a_member_without_permissions_changes_it
  check "someone outside the project neither reads nor changes it" an_outsider_neither_reads_nor_changes_it
  check "DELETE archives it, keeping the row and writing it down" delete_archives_and_keeps_the_row
  check "the list leaves the archived task out" the_list_leaves_archived_ones_out
  check "archived tasks and their restore need tasks.archive" archived_ones_need_tasks_archive
  check "tasks.archive restores it" tasks_archive_restores_it
  check "the list filters by assignee, priority, deadline and title, and sorts" the_list_filters_and_sorts
  check "the archive is logged" log_has_entry "Task {TaskId} archived" "\"TaskId\":\"$gone\""
  check "and the restore" log_has_entry "Task {TaskId} restored" "\"TaskId\":\"$gone\""
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 12.5: the new assignee is notified, unless they assigned it to themselves.

creates_the_notified_project() {
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Notificari","key":"NOT"}')" || return 1
  not_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$not_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$not_id")" '{"title":"Login SSO","priority":"Medium"}')" || return 1
  not_task=$(body_field id)
  [ -n "$not_task" ]
}

# Only those about the tasks of NOT: a full run of this script has notified Dan before, in 12.4.
not_notifications() { # not_notifications [user id]: the condition, for everyone or for one user
  printf "task_id IN (SELECT id FROM task_items WHERE project_id = '%s')%s" "$not_id" "${1:+ AND user_id = '$1'}"
}

notifications_of() { # notifications_of <user id>: type, task and message of each, oldest first
  in_db "SELECT type, task_id, message, read_at IS NULL FROM notifications WHERE $(not_notifications "$1") ORDER BY created_at, id"
}

assigning_someone_notifies_them() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$not_task/assignees" "{\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  is "TaskAssigned|$not_task|Administrator assigned you NOT-1: Login SSO|True" "$(notifications_of "$dan_id")"
}

assigning_yourself_notifies_nobody() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$not_task/assignees" "{\"assigneeIds\":[\"$admin_id\"]}")" || return 1
  is '' "$(notifications_of "$admin_id")" || return 1
  is 1 "$(in_db "SELECT count(*) FROM notifications WHERE $(not_notifications)")"
}

creating_one_for_someone_notifies_them() {
  status_is 201 "$(as "$admin_token" POST "$(tasks_of "$not_id")" "{\"title\":\"Raport\",\"priority\":\"High\",\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  second=$(body_field id)
  is 2 "$(in_db "SELECT count(*) FROM notifications WHERE $(not_notifications "$dan_id") AND type = 'TaskAssigned'")" || return 1
  is "$second" "$(in_db "SELECT task_id FROM notifications WHERE $(not_notifications "$dan_id") ORDER BY created_at DESC, id DESC LIMIT 1")"
}

dan_taking_it_himself_notifies_nobody() {
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$not_task/assignees" "{\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  is 2 "$(in_db "SELECT count(*) FROM notifications WHERE $(not_notifications)")"
}

admin_name_is_known() {
  is Administrator "$(in_db "SELECT display_name FROM users WHERE email = '$admin_email'")"
}

task_5() {
  echo "12.5 A TaskAssigned notification for the new assignee"
  check "Docker is running" docker_is_running
  check "the handler is tracked" tracked backend/src/Kanabol.Application/Features/Notifications/TaskAssignedEventHandler.cs
  check "the handler's tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~TaskAssignedNotificationTests' 8
  check "and the notification's own" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~NotificationTests' 2
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a user without permissions signs in" dan_is_ready
  check "the admin's id is known" admin_id_is_known
  check "the first admin is named Administrator, as the seed has it" admin_name_is_known
  check "a project with a member and a task is created" creates_the_notified_project
  check "assigning a task to someone else notifies them, unread" assigning_someone_notifies_them
  check "assigning it to yourself notifies nobody" assigning_yourself_notifies_nobody
  check "creating a task for someone notifies them" creating_one_for_someone_notifies_them
  check "taking a task yourself notifies nobody" dan_taking_it_himself_notifies_nobody
  check "the notification is logged" log_has_entry "User {AssigneeUserId} notified of being assigned task {TaskId}, notification {NotificationId}" "\"AssigneeUserId\":\"$dan_id\""
  check "the log has no unhandled exception, nor a failed handler" no_errors_or_failed_handlers
}

no_errors_or_failed_handlers() {
  no_errors_in_the_log || return 1
  if grep -F 'failed on domain event' "$api_log"; then
    return 1
  fi
}

# ---- 12.6: moving a task to a place in a column, with 409 when two moves cross.

creates_the_board_project() {
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Board","key":"BRD"}')" || return 1
  brd_id=$(body_field id)
  todo=$(in_db "SELECT id FROM board_columns WHERE project_id = '$brd_id' AND position = 0")
  doing=$(in_db "SELECT id FROM board_columns WHERE project_id = '$brd_id' AND position = 1")
  done_column=$(in_db "SELECT id FROM board_columns WHERE project_id = '$brd_id' AND kind = 'Done'")
  # With an estimate, so they enter În lucru without one and their history holds only the moves (group 22).
  for title in A B C D; do
    status_is 201 "$(as "$admin_token" POST "$(tasks_of "$brd_id")" "{\"title\":\"$title\",\"priority\":\"Low\",\"estimatedMinutes\":60}")" || return 1
    eval "task_$title=\$(body_field id)"
  done
  [ -n "$task_D" ] && [ -n "$doing" ]
}

board_of() { # board_of <project id>: each column's titles in order, and whether its places are 0, 1, 2...
  in_db "SELECT c.position, string_agg(t.title, ' ' ORDER BY t.position), bool_and(t.position = sub.rank) FROM board_columns c
    JOIN task_items t ON t.column_id = c.id AND t.archived_at IS NULL
    JOIN (SELECT id, (row_number() OVER (PARTITION BY column_id ORDER BY position, number) - 1)::int AS rank FROM task_items WHERE archived_at IS NULL) sub ON sub.id = t.id
    WHERE c.project_id = '$1' GROUP BY c.position ORDER BY c.position"
}

a_move_puts_it_in_place() {
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_A/position" "{\"columnId\":\"$doing\",\"position\":0}")" || return 1
  in_file '"position":0' "$scratch/body" && in_file '"columnName":"În lucru"' "$scratch/body" || return 1
  is '0|B C D|True
1|A|True' "$(board_of "$brd_id")"
}

a_reorder_keeps_the_places() {
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_D/position" "{\"columnId\":\"$todo\",\"position\":0}")" || return 1
  is '0|D B C|True
1|A|True' "$(board_of "$brd_id")"
}

no_place_means_the_end() {
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_B/position" "{\"columnId\":\"$doing\"}")" || return 1
  in_file '"position":1' "$scratch/body"
}

the_done_column_completes_it() {
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_C/position" "{\"columnId\":\"$done_column\",\"position\":0}")" || return 1
  is 'True' "$(in_db "SELECT completed_at IS NOT NULL FROM task_items WHERE id = '$task_C'")" || return 1
  # Out of Gata a task goes to În lucru or after it, never back before it (group 22).
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_C/position" "{\"columnId\":\"$doing\"}")" || return 1
  is 'True' "$(in_db "SELECT completed_at IS NULL FROM task_items WHERE id = '$task_C'")"
}

only_a_column_change_is_written_down() {
  is "ColumnId|$todo|$doing" "$(in_db "SELECT field, old_value, new_value FROM task_history_entries WHERE task_id = '$task_A'")" || return 1
  is 0 "$(in_db "SELECT count(*) FROM task_history_entries WHERE task_id = '$task_D'")"
}

a_column_of_another_project_is_refused() {
  other_column=$(in_db "SELECT id FROM board_columns WHERE project_id <> '$brd_id' LIMIT 1")
  code_is 400 Validation "$(as "$admin_token" PATCH "/api/tasks/$task_A/position" "{\"columnId\":\"$other_column\",\"position\":0}")" || return 1
  in_file '"columnId":' "$scratch/body"
}

# Two moves through one column, sent at the same moment: either both land one after the other, or the
# second gets 409 Task.ChangedMeanwhile. Never a 500, and the places stay 0, 1, 2... either way.
two_moves_at_once_keep_the_order() {
  # Both through În lucru: B, there already, to its top, and D into it. B can't go back to Backlog (group 22).
  printf '{"columnId":"%s","position":0}' "$doing" >"$scratch/move-1"
  printf '{"columnId":"%s","position":0}' "$doing" >"$scratch/move-2"
  # --next starts the second request with its own body; -Z still sends both at once.
  curl -sS --max-time 60 -Z --parallel-immediate \
    -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' -X PATCH -w '\nSTATUS %{http_code}\n' \
    -d "@$scratch/move-1" "$API_URL/api/tasks/$task_B/position" --next \
    -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' -X PATCH -w '\nSTATUS %{http_code}\n' \
    -d "@$scratch/move-2" "$API_URL/api/tasks/$task_D/position" >"$scratch/moves" 2>&1 || {
    tail -n 5 "$scratch/moves"
    return 1
  }
  statuses=$(grep '^STATUS' "$scratch/moves" | sort | paste -sd' ' -)
  case "$statuses" in
    'STATUS 200 STATUS 200') ;;
    'STATUS 200 STATUS 409') in_file '"code":"Task.ChangedMeanwhile"' "$scratch/moves" || return 1 ;;
    *)
      echo "unexpected answers: $statuses"
      return 1
      ;;
  esac
  board_of "$brd_id" | grep -v '|True$' && return 1
  return 0
}

task_6() {
  echo "12.6 Moving a task, with 409 on the versions"
  check "Docker is running" docker_is_running
  check "the handler's tests pass, two moves at once among them" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~MoveTaskCommandHandlerTests' 20
  check "the endpoint's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~TaskMoveEndpointTests' 3
  check "the entity's tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskItemTests' 32
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "a project with four tasks is created" creates_the_board_project
  check "a task moves to a place in another column, closing the gap it left" a_move_puts_it_in_place
  check "a reorder in the column keeps the places 0, 1, 2..." a_reorder_keeps_the_places
  check "without a place it goes last" no_place_means_the_end
  check "the done column completes it, and leaving it clears that" the_done_column_completes_it
  check "only a change of column is written in the history" only_a_column_change_is_written_down
  check "a column of another project is refused" a_column_of_another_project_is_refused
  check "two moves at once never break the order" two_moves_at_once_keep_the_order
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 12.9: the four rules Tasks.md names for the group, each by the tests that hold it.

test_exists() { # test_exists <project> <fully qualified name>: the test is there, so its filter can't run nothing
  dotnet test "backend/tests/$1/$1.csproj" --nologo --no-build --list-tests 2>/dev/null | tr -d '\r' | grep -qF "$2" || {
    echo "no test $2 in $1"
    return 1
  }
}

named_tests_pass() { # named_tests_pass <project> <test name...>: each exists, and all of them pass
  project=$1
  shift
  dotnet build "backend/tests/$project/$project.csproj" --nologo -v q >/dev/null 2>&1 || return 1
  filter=
  for name in "$@"; do
    test_exists "$project" "$name" || return 1
    filter="${filter:+$filter|}FullyQualifiedName~$name"
  done
  tests_pass "$project" "$filter" "$#"
}

every_change_writes_its_history() {
  named_tests_pass Kanabol.Domain.Tests \
    TaskItemTests.Changes_InARow_EachWriteTheirOwnEntryInOrder \
    TaskItemTests.Rename_WhenTheTitleChanges_WritesTheHistoryAndRaisesTaskUpdated \
    TaskItemTests.MoveTo_AnotherColumn_WritesTheColumnsAndRaisesTaskMoved \
    TaskItemTests.Archive_Always_RecordsWhenAndWhoWritesTheHistoryAndRaisesTaskArchived || return 1
  named_tests_pass Kanabol.Application.Tests \
    TaskChangeCommandHandlerTests.Rename_ByAMember_ChangesTheTitleAndWritesWhoAndWhen \
    TaskChangeCommandHandlerTests.ChangeDescription_ByAMember_ChangesItAndWritesTheHistory \
    TaskChangeCommandHandlerTests.SetPriority_ByAMember_ChangesItAndWritesTheHistory \
    TaskChangeCommandHandlerTests.SetDueDate_ByAMember_SetsAndClearsItAndWritesTheHistory \
    TaskChangeCommandHandlerTests.Assign_ToAnActiveMember_AssignsThemAndWritesTheHistory \
    TaskChangeCommandHandlerTests.Archive_ByAMember_ArchivesItWritesTheHistoryAndKeepsTheRow \
    TaskChangeCommandHandlerTests.Restore_OfAnArchivedTask_BringsItBackAndWritesTheHistory \
    MoveTaskCommandHandlerTests.Move_ToAnotherColumn_WritesTheColumnsInTheHistoryAndAReorderDoesNot
}

assigning_a_non_member_is_an_error() {
  named_tests_pass Kanabol.Application.Tests \
    TaskChangeCommandHandlerTests.Assign_ToSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeIdsAndChangesNothing \
    TaskChangeCommandHandlerTests.Assign_ToADeactivatedMember_ReturnsAValidationErrorOnAssigneeIds \
    CreateTaskCommandHandlerTests.Create_ForSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeIds || return 1
  named_tests_pass Kanabol.Api.Tests TaskChangesEndpointTests.PutAssignees_ToSomeoneOutsideTheProject_Returns400OnAssigneeIds
}

archived_ones_are_hidden_by_default() {
  named_tests_pass Kanabol.Application.Tests \
    GetProjectTasksQueryHandlerTests.List_ByDefault_LeavesTheArchivedOnesOut \
    GetProjectTasksQueryHandlerTests.List_WithIncludeArchivedAndTasksArchive_GivesThemToo \
    TaskQueryHandlerTests.ByKey_OfAnArchivedTask_ReturnsNotFound \
    TaskQueryHandlerTests.ById_OfAnArchivedTask_ReturnsNotFoundUnlessAskedForWithTasksArchive
}

two_creations_at_once_get_two_numbers() {
  named_tests_pass Kanabol.Application.Tests \
    CreateTaskCommandHandlerTests.Create_TwoAtOnce_GetDifferentNumbersAndNoConflict \
    CreateTaskCommandHandlerTests.Create_TenAtOnce_GetTenNumbersAndTenPlacesWithoutAConflict \
    CreateTaskCommandHandlerTests.Create_WhileAnotherOneHasTakenItsNumber_WaitsForItsCommitThenTakesTheNextOne \
    CreateTaskCommandHandlerTests.Create_WhileAMemberIsAdded_StillSucceeds
}

task_9() {
  echo "12.9 The group's tests: history, assignees, archived tasks, creations at once"
  check "Docker is running" docker_is_running
  check "every change writes its history" every_change_writes_its_history
  check "assigning someone outside the project is an error" assigning_a_non_member_is_an_error
  check "archived tasks don't show by default" archived_ones_are_hidden_by_default
  check "two and ten creations at once get different numbers and no 409" two_creations_at_once_get_two_numbers
  check "the real API starts in Production, and the first admin gets past the forced change" admin_is_ready
  check "and on it, ten creations at once get ten numbers and no 409" ten_at_once_get_ten_numbers PAQ
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 12.7: the task panel, from its specs, its routes, its texts and its production build.

web=frontend
app=$web/src/app
tasks=$app/features/tasks

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

frontend_lint_is_clean() {
  (cd "$web" && npx ng lint) >"$scratch/lint.log" 2>&1 || {
    plain "$scratch/lint.log" | tail -n 20
    return 1
  }
}

frontend_formatting_is_clean() {
  (cd "$web" && npx prettier --check .)
}

# docs/SPEC.md §16: nothing a user wrote reaches the page as HTML, anywhere in the app. Comments may
# name what the code doesn't do.
no_html_from_text() {
  files=$(git ls-files "$app" | grep -E '\.(ts|html)$' | grep -v '\.spec\.ts$')
  # shellcheck disable=SC2086
  found=$(grep -nE 'innerHTML|outerHTML|insertAdjacentHTML|bypassSecurityTrust|document\.write' $files |
    grep -vE '^[^:]+:[0-9]+:\s*(\*|//|/\*|<!--)')
  [ -z "$found" ] || {
    echo "$found"
    return 1
  }
}

no_colors_by_hand() {
  lacks '#[0-9a-fA-F]{3,8}\b' "$tasks"/*.scss "$tasks"/*.ts "$app/shared/ui/markdown.ts" "$app/shared/ui/deadline-chip.ts" \
    "$app/shared/ui/task-filter-bar.ts"
}

no_deep_selectors() {
  lacks '::ng-deep|\.mat-mdc-|\.mdc-' "$tasks"/*.scss "$tasks"/*.ts "$app/shared/ui/markdown.ts"
}

# /tasks/KAN-12 loads with its first visit, and the panel has an outlet of its own next to the page.
the_panel_has_its_routes() {
  node -e "
    const fs = require('fs');
    const routes = fs.readFileSync('$app/app.routes.ts', 'utf8');
    if (!/path: 'tasks',[^}]*loadChildren: \\(\\) =>\\s*import\\('.\\/features\\/tasks\\/tasks.routes'\\)/.test(routes)) {
      console.log('no lazy route for /tasks'); process.exit(1);
    }
    if (!/path: 'task\\/:key',\\s*outlet: TASK_PANEL_OUTLET,\\s*loadComponent/.test(routes)) {
      console.log('no lazy panel route in its outlet'); process.exit(1);
    }
    const shell = fs.readFileSync('$app/core/layout/shell.ts', 'utf8');
    if (!shell.includes('<router-outlet name=\"panel\" />')) { console.log('the shell has no panel outlet'); process.exit(1); }
    const outlet = fs.readFileSync('$app/shared/task-panel.ts', 'utf8');
    if (!outlet.includes(\"TASK_PANEL_OUTLET = 'panel'\")) { console.log('the outlet is not named panel'); process.exit(1); }
  "
}

# docs/UI_UX_SPEC.md §6.0.5: 920px, from the right, the window's height less 12px on each side.
the_panel_is_920_from_the_right() {
  in_file "width: '920px'," "$tasks/task-panel.ts" &&
    in_file "height: 'calc(100vh - 24px)'," "$tasks/task-panel.ts" &&
    in_file "position: { top: '12px', right: '12px' }," "$tasks/task-panel.ts" &&
    in_file 'grid-template-columns: minmax(0, 1fr) 300px;' "$tasks/task-panel.scss"
}

every_task_error_has_a_message() {
  codes=$(grep -o '"Task\.[A-Za-z]*"' backend/src/Kanabol.Application/Features/Tasks/TaskErrors.cs | tr -d '"' | sort -u)
  [ -n "$codes" ] || return 1
  for code in $codes; do
    in_file "'$code':" "$app/core/api/error-interceptor.ts" || {
      echo "no message for $code in error-interceptor.ts"
      return 1
    }
  done
}

task_7() {
  echo "12.7 The task panel"
  for file in models.ts tasks-api.ts task-fields.ts task-panel-store.ts task-panel.ts \
    task-panel.html task-panel.scss task-panel-route.ts task-link-page.ts tasks.routes.ts task-testing.ts; do
    check "features/tasks/$file is tracked" tracked "$tasks/$file"
  done
  for file in shared/task-panel.ts shared/task-changes.ts shared/ui/markdown.ts shared/ui/markdown-parser.ts shared/ui/deadline-chip.ts; do
    check "$file is tracked" tracked "$app/$file"
  done
  check "the specs of the panel pass" specs_pass "src/app/features/tasks/task-*.spec.ts" 31
  check "markdown is read and rendered without HTML" specs_pass "src/app/shared/ui/markdown*.spec.ts" 21
  check "the deadline chip reads as the UI spec has it" specs_pass src/app/shared/ui/deadline-chip.spec.ts 4
  check "the API client sends a PATCH" specs_pass src/app/core/api/api-client.spec.ts 5
  check "the shell still opens its pages" specs_pass src/app/core/layout/layout.spec.ts 15
  check "the projects pages still pass" specs_pass "src/app/features/projects/*.spec.ts" 63
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "nothing in the app puts text into the page as HTML" no_html_from_text
  check "the panel's pages write no colors by hand" no_colors_by_hand
  check "nor reach into Material's classes" no_deep_selectors
  check "/tasks/KAN-12 and the panel's outlet are routes" the_panel_has_its_routes
  check "the panel is 920px, from the right, in two columns" the_panel_is_920_from_the_right
  check "every refusal of a task has a Romanian message" every_task_error_has_a_message
  check "archiving asks first" messages_have 'Arhivezi task-ul'
  check "the description says it takes markdown" messages_have 'Markdown de bază'
  check "a task without a description says so" messages_have 'Fără descriere.'
  check "the build opens tasks by key" bundle_has '/tasks/by-key/'
  check "and its links can't reach back to the page" bundle_has 'noopener noreferrer'
}

# ---- 12.8: the List view of a project, with the board's filters in the address.

# /projects/:id/list is the tasks feature's page, loaded the first time it opens.
the_list_is_the_tasks_page() {
  node -e "
    const routes = require('fs').readFileSync('$app/features/projects/projects.routes.ts', 'utf8');
    if (!/path: 'list',\\s*loadComponent: \\(\\) =>\\s*import\\('..\\/tasks\\/task-list-page'\\)/.test(routes)) {
      console.log('the list route does not load the tasks list page'); process.exit(1);
    }
    if (/path: 'list', component: ProjectPlaceholder/.test(routes)) { console.log('the list is still a placeholder'); process.exit(1); }
  "
}

# The filters live in shared, so the board of group 13 takes the same ones.
the_filters_are_shared() {
  for file in shared/models/task-filters.ts shared/ui/task-filter-bar.ts; do
    tracked "$app/$file" || {
      echo "$file is not tracked"
      return 1
    }
  done
  if grep -rn "features/" "$app/shared/models/task-filters.ts" "$app/shared/ui/task-filter-bar.ts"; then
    echo "the shared filters reach into a feature"
    return 1
  fi
}

# docs/UI_UX_SPEC.md §7.3: key, title, column, priority, deadline, assignee, last change, in that order.
the_columns_are_the_spec_ones() {
  node -e "
    const page = require('fs').readFileSync('$tasks/task-list-page.ts', 'utf8');
    const found = page.match(/protected readonly columns = \\[([^\\]]*)\\]/);
    const columns = found ? [...found[1].matchAll(/'([a-zA-Z]+)'/g)].map((m) => m[1]).join(',') : '';
    if (columns !== 'key,title,column,priority,dueDate,assignee,updated') { console.log('the columns are ' + columns); process.exit(1); }
  "
}

task_8() {
  echo "12.8 The List view of a project"
  for file in task-list-page.ts task-list-page.html task-list-page.scss task-list-store.ts task-sheet.ts \
    task-sheet.html task-sheet.scss; do
    check "features/tasks/$file is tracked" tracked "$tasks/$file"
  done
  check "the specs of the list pass" specs_pass src/app/features/tasks/task-list-page.spec.ts 18
  check "the filters read and write the address" specs_pass src/app/shared/models/task-filters.spec.ts 6
  check "the filter bar says what changed" specs_pass src/app/shared/ui/task-filter-bar.spec.ts 6
  check "the projects pages still pass, with the list in them" specs_pass "src/app/features/projects/*.spec.ts" 63
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "/projects/:id/list is the tasks list, loaded on its first visit" the_list_is_the_tasks_page
  check "the filters are shared, for the board" the_filters_are_shared
  check "the table has the UI spec's columns, in its order" the_columns_are_the_spec_ones
  check "the pages write no colors by hand" no_colors_by_hand
  check "nor reach into Material's classes" no_deep_selectors
  check "the list says when nothing matches" messages_have 'Niciun task nu se potrivește'
  check "and offers the first task" messages_have 'Creează primul task'
  check "a new task explains where it starts" messages_have 'Primește următoarea cheie a proiectului'
  check "archived tasks are offered" messages_have 'Și cele arhivate'
}

if [ "$#" -eq 0 ]; then
  set -- 2 3 4 5 6 7 8 9
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
