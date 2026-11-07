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
