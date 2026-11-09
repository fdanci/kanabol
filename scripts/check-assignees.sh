#!/bin/sh
# Acceptance checks for several assignees on a task (group 13b in the project's Tasks.md).
# The tests and specs hold the rules, so these checks run them, and add what they can't see: the
# migration on a database that already has assignees, the real API process in Production on a real
# PostgreSQL, with its logs and its answers, and the frontend's production build and texts.
#
# Usage: scripts/check-assignees.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The backend tasks need Docker running and a PostgreSQL whose user
# may create databases, with its connection string in KANABOL_CHECK_DB or else in the API's user
# secrets (ConnectionStrings:Default); each run works in its own databases and drops them. The frontend
# tasks need only the packages that scripts/setup.sh installs.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_assignees_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5287
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
migrations=backend/src/Kanabol.Infrastructure/Persistence/Migrations

admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'
initial_password='o parola initiala pentru colegi'
member_password='parola noua aleasa de coleg'

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

lacks() { # lacks <extended regex> <file...>
  ! grep -nE -- "$@" || return 1
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

ef() { # ef <arguments...>: runs dotnet ef from backend/, against the built projects
  stop_api
  (cd backend && dotnet ef "$@" -p src/Kanabol.Infrastructure -s src/Kanabol.Api --no-build)
}

# ---- A database of this run, and the real API, in Production, on it.

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
// The names have ă and Î, which the console would turn into a and I.
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
  ) >>"$api_log" 2>&1 &
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
  [ -n "${api_pid:-}" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
}

cleanup() {
  stop_api
  if [ -n "$admin_connection" ]; then
    sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null
    sql "$admin_connection" "DROP DATABASE IF EXISTS ${check_db}_old WITH (FORCE)" >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ---- 13b.1: the task_assignees table, the migration that fills it, and TaskItem.Assign with a list.

domain=backend/src/Kanabol.Domain/Tasks

assignees_sql_is_right() {
  ef migrations script AddTaskNumber AddTaskAssignees -o "$scratch/add-task-assignees.sql" || return 1
  sed 's/\r$//' "$scratch/add-task-assignees.sql" >"$scratch/migration.sql"
  for line in \
    'CREATE TABLE task_assignees (' \
    'task_id uuid NOT NULL,' \
    'user_id uuid NOT NULL,' \
    'assigned_at timestamp with time zone NOT NULL,' \
    'assigned_by_id uuid,' \
    'CONSTRAINT pk_task_assignees PRIMARY KEY (task_id, user_id),' \
    'CONSTRAINT fk_task_assignees_task_items_task_id FOREIGN KEY (task_id) REFERENCES task_items (id) ON DELETE CASCADE,' \
    'CONSTRAINT fk_task_assignees_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT' \
    'CONSTRAINT fk_task_assignees_users_assigned_by_id FOREIGN KEY (assigned_by_id) REFERENCES users (id) ON DELETE RESTRICT' \
    'CREATE INDEX ix_task_assignees_user_id ON task_assignees (user_id);' \
    'ALTER TABLE task_items DROP COLUMN assignee_id;'; do
    in_file "$line" "$scratch/migration.sql" || {
      echo "not in the migration: $line"
      return 1
    }
  done
}

# The rows move before the column goes.
the_rows_move_before_the_column_goes() {
  node -e "
    const s = require('fs').readFileSync(process.argv[1], 'utf8');
    const at = (text) => { const i = s.indexOf(text); if (i < 0) { console.log('missing: ' + text); process.exit(1); } return i; };
    const create = at('CREATE TABLE task_assignees (');
    const copy = at('INSERT INTO task_assignees (task_id, user_id, assigned_at, assigned_by_id)');
    const drop = at('ALTER TABLE task_items DROP COLUMN assignee_id;');
    if (!(create < copy && copy < drop)) { console.log('not create, copy, drop, in that order'); process.exit(1); }
  " "$(cygpath -m "$scratch/migration.sql" 2>/dev/null || echo "$scratch/migration.sql")"
}

no_model_change_is_pending() {
  ef migrations has-pending-model-changes
}

# A database from before several assignees: each task keeps its assignee, assigned when the task was
# last changed, or made, by nobody known; the history of AssigneeId becomes that of Assignees.
old_assignees_are_kept() {
  need_database || return 1
  old_db="${check_db}_old"
  sql "$admin_connection" "CREATE DATABASE $old_db" || return 1
  ef database update AddTaskNumber --connection "$(connection_to "$old_db")" >"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  old=$(connection_to "$old_db")
  role=00000000-0000-7000-8000-0000000000a1
  ana=00000000-0000-7000-8000-0000000000b1
  dan=00000000-0000-7000-8000-0000000000b2
  project=00000000-0000-7000-8000-000000000001
  column=00000000-0000-7000-8000-0000000000c1
  changed=00000000-0000-7000-8000-0000000000d1
  never_changed=00000000-0000-7000-8000-0000000000d2
  nobody=00000000-0000-7000-8000-0000000000d3
  archived=00000000-0000-7000-8000-0000000000d4
  sql "$old" "INSERT INTO roles (id, name, is_system, holders_version, created_at) VALUES ('$role', 'Membru', false, 0, now())" || return 1
  sql "$old" "INSERT INTO users (id, email, display_name, password_hash, role_id, is_active, failed_login_attempts, must_change_password, created_at) VALUES
    ('$ana', 'ana@example.com', 'Ana', 'x', '$role', true, 0, false, now()),
    ('$dan', 'dan@example.com', 'Dan', 'x', '$role', false, 0, false, now())" || return 1
  sql "$old" "INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES ('$project', 'Vechi', 'OLD', 5, now())" || return 1
  sql "$old" "INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES ('$column', '$project', 'De făcut', 0, false, now())" || return 1
  sql "$old" "INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, assignee_id, created_at, updated_at, archived_at) VALUES
    ('$changed', '$project', '$column', 1, 'Schimbat', 'Low', 0, '$ana', '2026-11-01T08:00:00Z', '2026-11-03T09:30:00Z', NULL),
    ('$never_changed', '$project', '$column', 2, 'Neschimbat', 'Low', 1, '$dan', '2026-11-02T10:00:00Z', NULL, NULL),
    ('$nobody', '$project', '$column', 3, 'Al nimanui', 'Low', 2, NULL, '2026-11-02T11:00:00Z', NULL, NULL),
    ('$archived', '$project', '$column', 4, 'Arhivat', 'Low', 3, '$dan', '2026-11-02T12:00:00Z', '2026-11-04T12:00:00Z', '2026-11-04T12:00:00Z')" || return 1
  sql "$old" "INSERT INTO task_history_entries (id, task_id, field, old_value, new_value, changed_by_id, changed_at) VALUES
    (gen_random_uuid(), '$changed', 'AssigneeId', NULL, '$dan', '$ana', '2026-11-02T09:00:00Z'),
    (gen_random_uuid(), '$changed', 'AssigneeId', '$dan', '$ana', '$ana', '2026-11-03T09:30:00Z'),
    (gen_random_uuid(), '$nobody', 'AssigneeId', '$ana', NULL, '$ana', '2026-11-02T11:30:00Z'),
    (gen_random_uuid(), '$nobody', 'Title', 'Vechi', 'Al nimanui', '$ana', '2026-11-02T11:40:00Z')" || return 1
  ef database update --connection "$old" >>"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  is "Schimbat|$ana|2026-11-03 09:30:00+00|True
Neschimbat|$dan|2026-11-02 10:00:00+00|True
Arhivat|$dan|2026-11-04 12:00:00+00|True" "$(sql "$old" "SELECT t.title, a.user_id, to_char(a.assigned_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS+00'), a.assigned_by_id IS NULL
    FROM task_assignees a JOIN task_items t ON t.id = a.task_id ORDER BY t.number" | tr -d '\r')" || return 1
  is '' "$(sql "$old" "SELECT column_name FROM information_schema.columns WHERE table_name = 'task_items' AND column_name = 'assignee_id'" | tr -d '\r')" || return 1
  is "Schimbat|Assignees||$dan
Al nimanui|Assignees|$ana|
Al nimanui|Title|Vechi|Al nimanui
Schimbat|Assignees|$dan|$ana" "$(sql "$old" "SELECT t.title, h.field, coalesce(h.old_value, '<null>'), coalesce(h.new_value, '<null>')
    FROM task_history_entries h JOIN task_items t ON t.id = h.task_id ORDER BY h.changed_at" | tr -d '\r')"
}

# And back, for whoever has to undo it: a task keeps one of its assignees, the first one assigned.
the_migration_goes_back() {
  [ -n "${old:-}" ] || old_assignees_are_kept >/dev/null || return 1
  ana=00000000-0000-7000-8000-0000000000b1
  dan=00000000-0000-7000-8000-0000000000b2
  sql "$old" "INSERT INTO task_assignees (task_id, user_id, assigned_at) VALUES ('00000000-0000-7000-8000-0000000000d1', '$dan', '2026-11-05T00:00:00Z')" || return 1
  ef database update AddTaskNumber --connection "$old" >>"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  is "Schimbat|$ana
Neschimbat|$dan
Al nimanui|
Arhivat|$dan" "$(sql "$old" "SELECT title, assignee_id FROM task_items ORDER BY number" | tr -d '\r')" || return 1
  is '' "$(sql "$old" "SELECT table_name FROM information_schema.tables WHERE table_name = 'task_assignees'" | tr -d '\r')" || return 1
  is 'AssigneeId|AssigneeId|Title|AssigneeId' "$(sql "$old" "SELECT string_agg(field, '|' ORDER BY changed_at) FROM task_history_entries" | tr -d '\r')"
}

the_single_assignee_is_gone_from_the_domain() {
  lacks 'AssigneeId\b' "$domain"/*.cs "$domain"/Events/*.cs
}

assignment_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    TaskChangeCommandHandlerTests.Assign_ToTwoActiveMembers_AssignsBothInOneHistoryEntry \
    TaskChangeCommandHandlerTests.Assign_ToSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeIdsAndChangesNothing \
    TaskChangeCommandHandlerTests.Assign_ToADeactivatedMember_ReturnsAValidationErrorOnAssigneeIds \
    TaskChangeCommandHandlerTests.Assign_TheSamePersonTwice_ReturnsAValidationErrorOnAssigneeIds \
    TaskChangeCommandHandlerTests.Assign_KeepingAnAssigneeDeactivatedSince_KeepsThemAndAddsTheOthers \
    TaskChangeCommandHandlerTests.Assign_OfOneTaskAtOnce_GivesTheSecondChangedMeanwhileAndKeepsTheFirst \
    TaskChangeCommandHandlerTests.Assign_OfTheSamePersonByTwoAtOnce_GivesTheSecondChangedMeanwhile \
    CreateTaskCommandHandlerTests.Create_ForTwoActiveMembers_AssignsThemBothWithoutHistory \
    CreateTaskCommandHandlerTests.Create_ForSomeoneOutsideTheProject_ReturnsAValidationErrorOnAssigneeIds \
    CreateTaskCommandHandlerTests.Create_ForADeactivatedMember_ReturnsAValidationErrorOnAssigneeIds \
    CreateTaskCommandHandlerTests.Create_ForTheSamePersonTwice_ReturnsAValidationErrorOnAssigneeIds
}

task_1() {
  echo "13b.1 The task_assignees table, its migration, and Assign with the whole list"
  check "Docker is running" docker_is_running
  check "the entity is tracked" tracked "$domain/TaskAssignee.cs"
  check "its configuration is tracked" tracked backend/src/Kanabol.Infrastructure/Persistence/Configurations/TaskAssigneeConfiguration.cs
  check "the migration is tracked" tracked_glob "$migrations/*_AddTaskAssignees.cs"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the model has no change left without a migration" no_model_change_is_pending
  check "the migration makes the table, its keys and index, and drops assignee_id" assignees_sql_is_right
  check "it copies the assignees before it drops the column" the_rows_move_before_the_column_goes
  check "an old database keeps its assignees, and its history becomes Assignees" old_assignees_are_kept
  check "and goes back with one assignee per task" the_migration_goes_back
  check "the domain has no single assignee left" the_single_assignee_is_gone_from_the_domain
  check "the entity's tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskItemTests|FullyQualifiedName~TaskCreationTests' 45
  check "the active members, the repeated ids, a deactivated assignee and the races pass" assignment_tests_pass
  check "every test of a change and a creation passes" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~TaskChangeCommandHandlerTests|FullyQualifiedName~CreateTaskCommandHandlerTests|FullyQualifiedName~TestDataPersistenceTests' 60
}

# ---- Requests to the real API.

# An address no other request of this run has used, so the limit per address stays out of the way.
# The count is in a file, because every caller runs in a subshell, where a new value would be lost.
unused_address() {
  printf x >>"$scratch/used-addresses"
  used=$(wc -c <"$scratch/used-addresses")
  printf '10.82.%s.%s' "$((used / 256))" "$((used % 256))"
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  # An answer that never came leaves no older one behind to be read as this one.
  rm -f "$scratch/body" "$scratch/headers"
  curl -sS --max-time 30 -o "$scratch/body" -D "$scratch/headers" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

as() { # as <token> <method> <path> [JSON body]
  token=$1
  method=$2
  path=$3
  if [ "$#" -ge 4 ]; then
    # From a file: Windows hands curl its arguments in the ANSI code page, where ă turns into a.
    printf '%s' "$4" >"$scratch/request.json"
    send "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
      --data-binary "@$(cygpath -m "$scratch/request.json" 2>/dev/null || echo "$scratch/request.json")"
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
    "$(cygpath -m "$scratch/body" 2>/dev/null || echo "$scratch/body")" "$1"
}

status_is() { # status_is <expected status> <actual status>: shows the answer when it differs
  is "$1" "$2" || {
    cat "$scratch/body"
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

# The first admin past the forced password change. The API may have stopped for a build since the
# token was taken: it comes back first, and the token, signed with the same secret, still holds.
admin_is_ready() {
  api_is_up || return 1
  [ -n "${admin_token:-}" ] && return 0
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
  status_is 200 "$(as "$first_token" POST /api/auth/change-password "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  [ -n "$admin_token" ] && [ -n "$admin_id" ]
}

# A colleague without permissions, past their forced password change: their id goes in <name>_id and
# their token in <name>_token.
colleague_is_ready() { # colleague_is_ready <name> <email> <display name>
  admin_is_ready || return 1
  eval "[ -n \"\${$1_token:-}\" ]" && return 0
  if [ -z "${plain_role:-}" ]; then
    status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Fara permisiuni","permissions":[]}')" || return 1
    plain_role=$(body_field id)
  fi
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"$2\",\"displayName\":\"$3\",\"roleId\":\"$plain_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  eval "$1_id=$(body_field id)"
  status_is 200 "$(login "$2" "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$member_password\"}")" || return 1
  eval "$1_token=$(body_field accessToken)"
}

# Dan and Ioana, members of ASG; Radu, a member deactivated later; Eva, from outside.
team_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready ioana ioana@example.com 'ioana Pop' || return 1
  colleague_is_ready radu radu@example.com 'Radu Ene' || return 1
  colleague_is_ready eva eva@example.com 'Eva Din Afara' || return 1
  [ -n "${asg_id:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Asignati","key":"ASG"}')" || return 1
  asg_id=$(body_field id)
  for member in "$dan_id" "$ioana_id" "$radu_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$asg_id/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
}

names_of_assignees() { # in the last answer, a task: the names, in the order the API gives them
  json "b.assignees.map(a => a.displayName).join(',')"
}

keys_listed() { # in the last answer, a page: the keys, in order
  json "b.items.map(t => t.key).join(' ')"
}

# ---- 13b.2: the API, with assigneeIds in, assignees out, the filter and the sort.

a_task_is_created_for_two() {
  team_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$asg_id/tasks" "{\"title\":\"Pentru doi\",\"priority\":\"High\",\"assigneeIds\":[\"$ioana_id\",\"$dan_id\"]}")" || return 1
  shared_task=$(body_field id)
  is 'Dan Ionescu,ioana Pop' "$(names_of_assignees)" || return 1
  is false "$(json "'assignee' in b")" || return 1
  is "$dan_id|True
$ioana_id|True" "$(in_db "SELECT user_id, assigned_by_id = '$admin_id' FROM task_assignees WHERE task_id = '$shared_task' ORDER BY user_id = '$ioana_id', user_id")"
}

every_view_lists_them() {
  status_is 200 "$(as "$dan_token" GET "/api/tasks/$shared_task")" || return 1
  is 'Dan Ionescu,ioana Pop' "$(names_of_assignees)" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$asg_id/board")" || return 1
  is 'Dan Ionescu,ioana Pop' "$(json "b.tasks.find(t => t.id === '$shared_task').assignees.map(a => a.displayName).join(',')")" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$asg_id/tasks")" || return 1
  is 'Dan Ionescu,ioana Pop' "$(json "b.items.find(t => t.id === '$shared_task').assignees.map(a => a.displayName).join(',')")"
}

the_whole_list_replaces_the_old_one() {
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignees" "{\"assigneeIds\":[\"$ioana_id\",\"$radu_id\"]}")" || return 1
  is 'ioana Pop,Radu Ene' "$(names_of_assignees)" || return 1
  old_ids=$(printf '%s\n%s\n' "$dan_id" "$ioana_id" | LC_ALL=C sort | paste -sd, -)
  new_ids=$(printf '%s\n%s\n' "$ioana_id" "$radu_id" | LC_ALL=C sort | paste -sd, -)
  is "Assignees|$old_ids|$new_ids|$dan_id" "$(in_db "SELECT field, old_value, new_value, changed_by_id FROM task_history_entries WHERE task_id = '$shared_task'")"
}

nobody_is_an_empty_list() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$asg_id/tasks" "{\"title\":\"Al nimanui\",\"priority\":\"Low\",\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  lonely_task=$(body_field id)
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$lonely_task/assignees" '{"assigneeIds":[]}')" || return 1
  is 0 "$(json "b.assignees.length")" || return 1
  is "Assignees|$dan_id|" "$(in_db "SELECT field, old_value, new_value FROM task_history_entries WHERE task_id = '$lonely_task'")"
}

only_active_members_each_once() {
  code_is 400 Validation "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignees" "{\"assigneeIds\":[\"$dan_id\",\"$eva_id\"]}")" || return 1
  in_file '"assigneeIds":' "$scratch/body" || return 1
  code_is 400 Validation "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignees" "{\"assigneeIds\":[\"$dan_id\",\"$dan_id\"]}")" || return 1
  in_file '"assigneeIds":' "$scratch/body" || return 1
  code_is 400 Validation "$(as "$admin_token" POST "/api/projects/$asg_id/tasks" "{\"title\":\"Cu cineva din afara\",\"priority\":\"Low\",\"assigneeIds\":[\"$eva_id\"]}")" || return 1
  in_file '"assigneeIds":' "$scratch/body" || return 1
  # upper() of display_name follows its ro-x-icu collation, as the API's sorts do (docs/SPEC.md §5.3).
  is 'ioana Pop,Radu Ene' "$(in_db "SELECT string_agg(u.display_name, ',' ORDER BY upper(u.display_name)) FROM task_assignees a JOIN users u ON u.id = a.user_id WHERE a.task_id = '$shared_task'")"
}

a_deactivated_assignee_stays_until_taken_off() {
  status_is 200 "$(as "$admin_token" POST "/api/users/$radu_id/deactivate")" || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignees" "{\"assigneeIds\":[\"$radu_id\",\"$ioana_id\",\"$dan_id\"]}")" || return 1
  is 'Dan Ionescu,ioana Pop,Radu Ene' "$(names_of_assignees)" || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignees" "{\"assigneeIds\":[\"$ioana_id\",\"$dan_id\"]}")" || return 1
  code_is 400 Validation "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignees" "{\"assigneeIds\":[\"$radu_id\",\"$dan_id\"]}")"
}

the_filter_finds_a_shared_task() {
  status_is 200 "$(as "$dan_token" GET "/api/projects/$asg_id/tasks?assigneeId=$ioana_id")" || return 1
  is 'ASG-1' "$(keys_listed)" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$asg_id/tasks?assigneeId=$dan_id")" || return 1
  is 'ASG-1' "$(keys_listed)"
}

the_sort_goes_by_the_first_name() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$asg_id/tasks" "{\"title\":\"A Ioanei\",\"priority\":\"Low\",\"assigneeIds\":[\"$ioana_id\"]}")" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$asg_id/tasks?sort=assignee:asc")" || return 1
  is 'ASG-1 ASG-3 ASG-2' "$(keys_listed)" || return 1
  status_is 200 "$(as "$dan_token" GET "/api/projects/$asg_id/tasks?sort=assignee:desc")" || return 1
  is 'ASG-3 ASG-1 ASG-2' "$(keys_listed)"
}

the_old_endpoint_is_gone() {
  status_is 404 "$(as "$dan_token" PUT "/api/tasks/$shared_task/assignee" "{\"assigneeId\":\"$dan_id\"}")"
}

two_changes_at_once_give_one_409() {
  printf '{"assigneeIds":["%s"]}' "$dan_id" >"$scratch/first.json"
  printf '{"assigneeIds":["%s"]}' "$ioana_id" >"$scratch/second.json"
  first=$(cygpath -m "$scratch/first.json" 2>/dev/null || echo "$scratch/first.json")
  second=$(cygpath -m "$scratch/second.json" 2>/dev/null || echo "$scratch/second.json")
  # Both at once, which on Windows needs curl -Z: two curls started with & run one after the other.
  curl -sS -Z --parallel-immediate --max-time 30 -o /dev/null -w '%{http_code}\n' \
    -X PUT -H "Authorization: Bearer $dan_token" -H 'Content-Type: application/json' --data-binary "@$first" "$API_URL/api/tasks/$shared_task/assignees" \
    --next -o /dev/null -w '%{http_code}\n' \
    -X PUT -H "Authorization: Bearer $ioana_token" -H 'Content-Type: application/json' --data-binary "@$second" "$API_URL/api/tasks/$shared_task/assignees" \
    >"$scratch/statuses" || return 1
  statuses=$(tr -d '\r' <"$scratch/statuses" | sort | paste -sd' ' -)
  case "$statuses" in
    '200 409' | '200 200') ;;
    *)
      echo "got $statuses"
      return 1
      ;;
  esac
  # Whichever won, the task has exactly its list, and one entry per change that went through.
  is 1 "$(in_db "SELECT count(*) FROM task_assignees WHERE task_id = '$shared_task'")"
}

the_endpoints_are_written_down() {
  for text in 'PUT /api/tasks/{id}/title' '`…/assignees`' '`assigneeIds`' 'task_assignees' 'first name in alphabetical order'; do
    in_file "$text" backend/CLAUDE.md || {
      echo "backend/CLAUDE.md doesn't say $text"
      return 1
    }
  done
  lacks '…/assignee`|optionally `columnId`, `assigneeId`' backend/CLAUDE.md
}

api_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    TaskChangesEndpointTests.PutAssignees_ToTwoMembersThenNobody_Returns200WithTheWholeListEachTime \
    TaskChangesEndpointTests.PutAssignees_WithoutAListOrWithAnIdTwice_Returns400OnAssigneeIds \
    TaskChangesEndpointTests.PutAssignees_ToSomeoneOutsideTheProject_Returns400OnAssigneeIds \
    TaskChangesEndpointTests.PutAssignee_TheOldEndpoint_IsGone \
    TaskChangesEndpointTests.GetList_WithFiltersAndSort_ReturnsAPageOfTheMatchingTasks
}

query_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    GetProjectTasksQueryHandlerTests.List_OfAnAssignee_FindsTheTasksTheyShareWithOthers \
    GetProjectTasksQueryHandlerTests.List_SortedByAssignee_GoesByTheFirstNameAlphabetically \
    GetProjectTasksQueryHandlerTests.List_OfATaskWithSeveralAssignees_GivesThemAllByName \
    GetBoardQueryHandlerTests.Board_OfATaskWithSeveralAssignees_GivesThemAllByName \
    TaskQueryHandlerTests.ById_OfATask_ReturnsItWithItsAssigneesByNameAndColumn
}

task_2() {
  echo "13b.2 The API: assigneeIds in, assignees out, the filter and the sort"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the filter, the sort and every view's list of assignees pass" query_tests_pass
  check "the endpoint's tests pass, and the old one is gone" api_tests_pass
  check "every test of the tasks' handlers passes" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~Features.Tasks' 145
  check "backend/CLAUDE.md lists the endpoint, its list and the rules" the_endpoints_are_written_down
  check "the real API starts in Production, with three members and one from outside" team_is_ready
  check "a task created for two lists both by name, and no single assignee" a_task_is_created_for_two
  check "the task, the board and the list all give both" every_view_lists_them
  check "the whole list replaces the old one, in one history entry" the_whole_list_replaces_the_old_one
  check "an empty list leaves the task to nobody" nobody_is_an_empty_list
  check "someone from outside or an id twice is a 400 on assigneeIds, changing nothing" only_active_members_each_once
  check "a deactivated assignee stays until taken off, and can't come back" a_deactivated_assignee_stays_until_taken_off
  check "the filter finds the task each of its assignees shares" the_filter_finds_a_shared_task
  check "the sort goes by each task's first name, the unassigned last" the_sort_goes_by_the_first_name
  check "the old endpoint with a single assignee is gone" the_old_endpoint_is_gone
  check "two changes at once leave one list" two_changes_at_once_give_one_409
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 13b.3: TaskAssigned for each assignee newly added, and nobody else.

notification_handler=backend/src/Kanabol.Application/Features/Notifications/TaskAssignedEventHandler.cs

# NTF, with Dan, Ioana and Eva as members, and no notification before.
creates_the_notified_project() {
  team_is_ready || return 1
  [ -n "${ntf_id:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Notificari","key":"NTF"}')" || return 1
  ntf_id=$(body_field id)
  for member in "$dan_id" "$ioana_id" "$eva_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$ntf_id/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  status_is 201 "$(as "$admin_token" POST "/api/projects/$ntf_id/tasks" '{"title":"Login SSO","priority":"Medium"}')" || return 1
  ntf_task=$(body_field id)
  [ -n "$ntf_task" ]
}

ntf_notifications_of() { # ntf_notifications_of <user id>: type, task and message of each, oldest first
  in_db "SELECT type, task_id, message FROM notifications WHERE user_id = '$1' AND task_id IN (SELECT id FROM task_items WHERE project_id = '$ntf_id') ORDER BY created_at, id"
}

ntf_count() {
  in_db "SELECT count(*) FROM notifications WHERE task_id IN (SELECT id FROM task_items WHERE project_id = '$ntf_id')"
}

two_added_both_hear_of_it() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$ntf_task/assignees" "{\"assigneeIds\":[\"$dan_id\",\"$ioana_id\"]}")" || return 1
  is "TaskAssigned|$ntf_task|Administrator assigned you NTF-1: Login SSO" "$(ntf_notifications_of "$dan_id")" || return 1
  is "TaskAssigned|$ntf_task|Administrator assigned you NTF-1: Login SSO" "$(ntf_notifications_of "$ioana_id")" || return 1
  is 2 "$(ntf_count)"
}

only_the_new_one_hears_of_it() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$ntf_task/assignees" "{\"assigneeIds\":[\"$ioana_id\",\"$eva_id\",\"$dan_id\"]}")" || return 1
  is "TaskAssigned|$ntf_task|Administrator assigned you NTF-1: Login SSO" "$(ntf_notifications_of "$eva_id")" || return 1
  is 3 "$(ntf_count)"
}

taking_someone_off_tells_nobody() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$ntf_task/assignees" "{\"assigneeIds\":[\"$eva_id\"]}")" || return 1
  is 3 "$(ntf_count)"
}

whoever_makes_the_change_is_not_told() {
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$ntf_task/assignees" "{\"assigneeIds\":[\"$eva_id\",\"$dan_id\",\"$admin_id\"]}")" || return 1
  is 1 "$(ntf_notifications_of "$dan_id" | wc -l | tr -d ' ')" || return 1
  is "TaskAssigned|$ntf_task|Dan Ionescu assigned you NTF-1: Login SSO" "$(ntf_notifications_of "$admin_id")" || return 1
  is 4 "$(ntf_count)"
}

a_creation_tells_those_it_is_for() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$ntf_id/tasks" "{\"title\":\"Raport\",\"priority\":\"Low\",\"assigneeIds\":[\"$admin_id\",\"$ioana_id\",\"$dan_id\"]}")" || return 1
  report=$(body_field id)
  is "TaskAssigned|$report|Administrator assigned you NTF-2: Raport" "$(ntf_notifications_of "$ioana_id" | tail -n 1)" || return 1
  is "TaskAssigned|$report|Administrator assigned you NTF-2: Raport" "$(ntf_notifications_of "$dan_id" | tail -n 1)" || return 1
  is 6 "$(ntf_count)"
}

the_handler_reads_the_added_ones() {
  in_file 'domainEvent.AddedIds' "$notification_handler" &&
    lacks 'AssigneeId is not|PreviousAssigneeId' "$notification_handler"
}

notification_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    TaskAssignedNotificationTests.Assign_ToTwoOthers_NotifiesEachWithTheMessageOfOne \
    TaskAssignedNotificationTests.Assign_AddingSomeoneToThoseAlreadyThere_NotifiesOnlyTheNewOne \
    TaskAssignedNotificationTests.Assign_ByOneWhoAddsThemselvesWithOthers_NotifiesOnlyTheOthers \
    TaskAssignedNotificationTests.Create_ForTwoOthersAndThemselves_NotifiesTheTwo \
    TaskAssignedNotificationTests.Assign_TakingSomeoneOffAndKeepingTheRest_NotifiesNobody \
    TaskAssignedNotificationTests.Assign_ToTwo_LogsEachNotification
}

the_rule_is_written_down() {
  in_file 'each assignee newly added' backend/CLAUDE.md
}

task_3() {
  echo "13b.3 TaskAssigned for each assignee newly added, and nobody else"
  check "Docker is running" docker_is_running
  check "the handler is tracked" tracked "$notification_handler"
  check "the solution builds without warnings" solution_builds
  check "the handler reads who was added, not a single assignee" the_handler_reads_the_added_ones
  check "the tests of those added, those already there and the one who made the change pass" notification_tests_pass
  check "every test of the notification passes" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~TaskAssignedNotificationTests' 14
  check "backend/CLAUDE.md says who hears of it" the_rule_is_written_down
  check "the real API starts, with a project of three members" creates_the_notified_project
  check "two assigned at once both hear of it, with the message unchanged" two_added_both_hear_of_it
  check "of three, only the one newly added hears of it" only_the_new_one_hears_of_it
  check "taking people off tells nobody" taking_someone_off_tells_nobody
  check "whoever makes the change isn't told, the others are" whoever_makes_the_change_is_not_told
  check "a creation tells those it is for, but not its creator" a_creation_tells_those_it_is_for
  check "each notification is logged" log_has_entry "User {AssigneeUserId} notified of being assigned task {TaskId}, notification {NotificationId}" "\"AssigneeUserId\":\"$eva_id\""
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- The frontend: specs, lint, the production build and the texts.

web=frontend
app=$web/src/app
stack=$app/shared/ui/avatar-stack.ts

plain() { # Without the colors the tools print
  sed 's/\x1b\[[0-9;]*m//g' "$1"
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

spec_names() { # spec_names <spec file> <name...>: each is an it() of that file
  file=$1
  shift
  for name in "$@"; do
    in_file "it('$name'" "$file" || {
      echo "no spec '$name' in $file"
      return 1
    }
  done
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

# ---- 13b.4: kb-avatar-stack on the card and in the List, and the filters among the assignees.

stack_follows_the_ui_spec() {
  in_file 'export const STACK_MAX = 3;' "$stack" &&
    in_file "selector: 'kb-avatar-stack'" "$stack" &&
    in_file '[matTooltip]="names()"' "$stack" &&
    in_file "'[attr.aria-label]'" "$stack" &&
    lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-' "$stack"
}

the_card_and_the_list_use_it() {
  in_file '<kb-avatar-stack [people]="task().assignees" />' "$app/features/board/task-card.ts" &&
    in_file '<kb-avatar-stack [people]="task.assignees" />' "$app/features/tasks/task-table.html" &&
    lacks '\bassignee\??\.' "$app/features/board/"*.ts "$app/features/tasks/task-list-page."* "$app/features/tasks/task-table."* \
      "$app/features/tasks/task-list-store.ts"
}

the_filter_looks_among_the_assignees() {
  in_file 'task.assignees.some((person) => person.userId === assignee)' "$app/features/board/board-filters.ts"
}

stack_specs_name_the_rules() {
  spec_names "$app/shared/ui/avatar-stack.spec.ts" \
    'shows everyone up to three, without +N' \
    'shows the first three of more, and how many are left' \
    'names everyone once for a screen reader, the avatars and +N staying quiet' \
    'puts every name in the tooltip' &&
    spec_names "$app/features/board/board-page.spec.ts" 'finds a card for each of those it is assigned to'
}

the_texts_are_there() {
  messages_have '>Asignați<' && messages_have '>Neasignat<'
}

the_shared_piece_is_written_down() {
  in_file '`kb-avatar-stack`' frontend/CLAUDE.md && in_file 'among its assignees' frontend/CLAUDE.md
}

task_4() {
  echo "13b.4 kb-avatar-stack on the card and in the List, and the filters among the assignees"
  check "the stack is tracked" tracked "$stack"
  check "its specs are tracked" tracked "$app/shared/ui/avatar-stack.spec.ts"
  check "it shows at most three and +N, the names in the tooltip and the aria-label, with tokens only" stack_follows_the_ui_spec
  check "the card and the List show it, and nothing reads a single assignee" the_card_and_the_list_use_it
  check "the board's filter finds a card among its assignees" the_filter_looks_among_the_assignees
  check "the specs name the stack's rules and the shared card" stack_specs_name_the_rules
  check "the stack's specs pass" specs_pass src/app/shared/ui/avatar-stack.spec.ts 5
  check "every spec of the board passes" specs_pass "src/app/features/board/*.spec.ts" 67
  check "the List's specs pass" specs_pass src/app/features/tasks/task-list-page.spec.ts 15
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build has the stack" bundle_has 'kb-avatar-stack'
  check "the texts are there, Asignați and Neasignat" the_texts_are_there
  check "frontend/CLAUDE.md names the stack and the filter" the_shared_piece_is_written_down
}

# ---- 13b.5: the multiple select with avatars in the panel and the sheet, saved once, and the history.

tasks_ui=$app/features/tasks

both_selects_are_multiple() {
  node -e "
    const fs = require('fs');
    for (const file of process.argv.slice(1)) {
      const s = fs.readFileSync(file, 'utf8');
      const select = s.slice(s.lastIndexOf('<mat-select', s.indexOf('-assignee\"')), s.indexOf('</mat-select>', s.indexOf('-assignee\"')));
      for (const text of ['multiple', '<mat-select-trigger>', '<kb-avatar [name]=\"person.displayName\" />', '[placeholder]=\"unassigned\"']) {
        if (!select.includes(text)) { console.log(file + ': the assignees select lacks ' + text); process.exit(1); }
      }
    }
  " "$tasks_ui/task-panel.html" "$tasks_ui/task-sheet.html"
}

the_panel_saves_when_the_list_closes() {
  in_file '(openedChange)="assigneesOpened($event, assigneeSelect)"' "$tasks_ui/task-panel.html" &&
    in_file '(selectionChange)="chosenAssignees.set($event.value)"' "$tasks_ui/task-panel.html" &&
    in_file 'this.chosenAssignees.set(before);' "$tasks_ui/task-panel.ts"
}

the_api_sends_the_whole_list() {
  in_file "/tasks/\${id}/assignees\`, { assigneeIds }" "$tasks_ui/tasks-api.ts" &&
    in_file "case 'assigneeIds':" "$tasks_ui/task-fields.ts" &&
    lacks "/assignee\`" "$tasks_ui/tasks-api.ts" &&
    lacks 'assigneeId[^s]|\.assignee\b' "$tasks_ui"/task-panel* "$tasks_ui"/task-sheet* "$tasks_ui/models.ts" "$tasks_ui/task-testing.ts"
}

panel_specs_name_the_rules() {
  spec_names "$tasks_ui/task-panel.spec.ts" \
    'saves the assignees once, when their list closes, and shows them with their names' \
    'sends nothing when the list closes as it was' \
    'puts the assignees back when the API refuses them, and says why' \
    'lets a deactivated assignee be taken off, but not given the task again' \
    'shows the assignees with their names to whoever only reads the task' &&
    spec_names "$tasks_ui/task-panel-store.spec.ts" 'sends the whole list of assignees and keeps what the API saved' &&
    spec_names "$tasks_ui/task-history.spec.ts" \
      'names the people before and after, by name whatever the case' \
      'says Neasignat for nobody, on either side'
}

the_texts_of_the_panel_are_there() {
  # task-history.ts waits for the timeline of group 16, so its texts aren't in the app yet.
  messages_have 'Pot fi asignați doar membri activi ai proiectului, fiecare o dată.' && messages_have '>Asignați<'
}

the_panel_is_written_down() {
  in_file 'saved once, when the list closes' frontend/CLAUDE.md && in_file '`assigneesChange`' frontend/CLAUDE.md
}

task_5() {
  echo "13b.5 The multiple select with avatars in the panel and the sheet, saved once, and the history's names"
  check "the history's names are tracked" tracked "$tasks_ui/task-history.ts"
  check "the panel and the sheet pick the assignees from a multiple select with avatars" both_selects_are_multiple
  check "the panel saves when the list closes, and puts it back when refused" the_panel_saves_when_the_list_closes
  check "the API gets the whole list, and nothing reads a single assignee" the_api_sends_the_whole_list
  check "the specs name the save on close, the refusal, a deactivated assignee and the history" panel_specs_name_the_rules
  check "the panel's specs pass" specs_pass src/app/features/tasks/task-panel.spec.ts 20
  check "every spec of the tasks feature passes, the sheet's among them" specs_pass "src/app/features/tasks/*.spec.ts" 57
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build sends the list to …/assignees" bundle_has '/assignees'
  check "the texts are there" the_texts_of_the_panel_are_there
  check "frontend/CLAUDE.md says how the panel saves them" the_panel_is_written_down
}

# ---- 13b.6: the group's tests, and the other checks on the new API and schema.

group_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    TaskAssigneesEndpointTests.PutAssignees_AddingASecondMember_GivesBothOneHistoryEntryAndNotifiesOnlyTheNewOne \
    TaskAssigneesEndpointTests.PutAssignees_ToANonMemberADeactivatedMemberOrAnIdTwice_Returns400OnAssigneeIdsAndChangesNothing \
    TaskAssigneesEndpointTests.GetList_ByEitherOfTwoAssignees_FindsTheirTask \
    TaskAssigneesEndpointTests.GetBoardAndGetTask_OfATaskWithTwoAssignees_ListThemBoth
}

migration_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    AddTaskAssigneesMigrationTests.Migrate_FromOneAssigneePerTask_KeepsEachAssigneeAndTurnsTheHistoryIntoAssignees \
    AddTaskAssigneesMigrationTests.Migrate_BackFromSeveralAssignees_KeepsTheFirstOneAssignedOfEachTask
}

# check-tasks.sh and check-board.sh call the new API, and check-backend.sh probes the new schema.
the_other_checks_follow() {
  lacks '/assignee"|\\"assigneeId\\":\\"|"assigneeId":null|assignee_id FROM task_items|"assignee":' scripts/check-tasks.sh scripts/check-board.sh &&
    lacks 'fk_task_items_users_assignee_id|AssigneeId' scripts/check-backend.sh &&
    in_file 'Has("CREATE TABLE task_assignees (");' scripts/check-backend.sh &&
    in_file '"roles", "task_assignees", "task_history_entries"' scripts/check-backend.sh &&
    in_file 'PUT "/api/tasks/$edt_task/assignees"' scripts/check-tasks.sh
}

check_backend_8_and_9_pass() {
  stop_api
  sh scripts/check-backend.sh 8 9 >"$scratch/check-backend-8-9.log" 2>&1 || {
    grep -E 'FAIL|failed' -A 6 "$scratch/check-backend-8-9.log" | head -n 30
    return 1
  }
}

task_6() {
  echo "13b.6 The group's tests, and check-tasks, check-board and check-backend on the new API and schema"
  check "Docker is running" docker_is_running
  check "the endpoint's tests of the group are tracked" tracked backend/tests/Kanabol.Api.Tests/Tasks/TaskAssigneesEndpointTests.cs
  check "the migration's tests are tracked" tracked backend/tests/Kanabol.Application.Tests/Database/AddTaskAssigneesMigrationTests.cs
  check "two members in the DTO, one history entry, a notification only for the new one, the 400s and the filter pass" group_tests_pass
  check "the migration keeps the old assignee, and goes back, on a database of its own" migration_tests_pass
  check "check-tasks.sh and check-board.sh use the new API, check-backend.sh the new schema" the_other_checks_follow
  check "check-backend.sh 8 and 9 pass: the schema, the entities and the migrated tables on a real PostgreSQL" check_backend_8_and_9_pass
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 5 6
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
