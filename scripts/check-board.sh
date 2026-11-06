#!/bin/sh
# Acceptance checks for the kanban board (group 13 in the project's Tasks.md).
# The tests and specs hold the rules, so these checks run them, and add what they can't see: the real
# API on a real PostgreSQL (through check-tasks.sh), the routes, the texts and the production build.
#
# Usage: scripts/check-board.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Task 1 needs Docker running and, for the real API that
# check-tasks.sh 3 starts, a PostgreSQL whose user may create databases, with its connection string in
# KANABOL_CHECK_DB or else in the API's user secrets (ConnectionStrings:Default).

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_board_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5289
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=

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

in_file() { # in_file <text> <file>
  grep -qF -- "$1" "$2"
}

lacks() { # lacks <extended regex> <file...>
  ! grep -nE -- "$@" || return 1
}

docker_is_running() {
  docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
  "$docker_cli" info --format '{{.ServerVersion}}'
}

solution_builds() {
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
}

formatting_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
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

test_exists() { # test_exists <project> <name>: the test is there, so its filter can't run nothing
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

# ---- The real API, in Production, on a database of this run.

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

cleanup() {
  if [ -n "$api_pid" ]; then
    kill "$api_pid" 2>/dev/null
    wait "$api_pid" 2>/dev/null
  fi
  if [ -n "$admin_connection" ]; then
    sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null
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
  printf '10.81.%s.%s' "$((used / 256))" "$((used % 256))"
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

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
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

location_header() {
  tr -d '\r' <"$scratch/headers" | sed -n 's/^[Ll]ocation: //p'
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

# The first admin past the forced password change, and Dan, a member without permissions.
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

# ---- 13.1: the task number from one UPDATE … RETURNING, in the transaction of the creation.

numbers=backend/src/Kanabol.Infrastructure/Persistence/TaskNumbers.cs
create_handler=backend/src/Kanabol.Application/Features/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs

# The UPDATE comes after the transaction begins, and the save and the commit after it.
the_counter_moves_in_the_transaction() {
  node -e "
    const s = require('fs').readFileSync('$numbers', 'utf8');
    const at = (text) => { const i = s.indexOf(text); if (i < 0) { console.log('missing: ' + text); process.exit(1); } return i; };
    const begin = at('BeginTransactionAsync(ct)');
    const update = at('UPDATE projects SET next_task_number = next_task_number + 1');
    at('WHERE id = {projectId} AND archived_at IS NULL');
    at('RETURNING next_task_number - 1');
    const save = s.indexOf('SaveChangesAsync(ct)', update);
    const commit = at('transaction.CommitAsync(ct)');
    if (!(begin < update && update < save && save < commit)) { console.log('not begin, UPDATE, save, commit, in that order'); process.exit(1); }
    if (s.indexOf('SaveChangesAsync(ct)', commit) < 0) { console.log('no save after the commit, which sends the held events'); process.exit(1); }
  "
}

no_retry_left() {
  lacks 'MaxRetries|ReloadAsync|DbUpdateException|ChangedMeanwhile|for \(' "$create_handler" &&
    lacks 'TakeTaskNumber|NextTaskNumber\+\+' backend/src/Kanabol.Domain/Projects/Project.cs
}

the_events_wait_for_the_commit() {
  in_file 'context?.Database.CurrentTransaction is not null' \
    backend/src/Kanabol.Infrastructure/Persistence/Interceptors/DomainEventsInterceptor.cs
}

creation_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    CreateTaskCommandHandlerTests.Create_TwoAtOnce_GetDifferentNumbersAndNoConflict \
    CreateTaskCommandHandlerTests.Create_TenAtOnce_GetTenNumbersAndTenPlacesWithoutAConflict \
    CreateTaskCommandHandlerTests.Create_WhileAnotherOneHasTakenItsNumber_WaitsForItsCommitThenTakesTheNextOne \
    CreateTaskCommandHandlerTests.Create_WhileAMemberIsAdded_StillSucceeds \
    CreateTaskCommandHandlerTests.Create_WhenTheProjectIsArchivedMeanwhile_ReturnsArchived \
    CreateTaskCommandHandlerTests.Create_WhenTheProjectIsRenamedMeanwhile_StillSucceedsAndKeepsTheNewName \
    CreateTaskCommandHandlerTests.AddMember_WhileATaskIsCreated_Returns409ChangedMeanwhile
}

ten_posts_at_once_pass() {
  named_tests_pass Kanabol.Api.Tests TasksEndpointTests.Post_TenAtOnce_Return201WithTenDifferentKeys
}

check_tasks_3_passes() {
  sh scripts/check-tasks.sh 3 >"$scratch/check-tasks-3.log" 2>&1 || {
    grep -E 'FAIL|failed' -A 6 "$scratch/check-tasks-3.log" | head -n 30
    return 1
  }
}

task_1() {
  echo "13.1 The task number from one UPDATE … RETURNING, without a retry"
  check "Docker is running" docker_is_running
  check "the abstraction is tracked" tracked backend/src/Kanabol.Application/Abstractions/ITaskNumbers.cs
  check "its implementation is tracked" tracked "$numbers"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the counter moves in one UPDATE … RETURNING, inside the transaction that saves the task" the_counter_moves_in_the_transaction
  check "no retry is left, and the entity no longer moves the counter" no_retry_left
  check "the domain events of a save inside a transaction wait for its commit" the_events_wait_for_the_commit
  check "the domain tests of the creation pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskCreationTests' 10
  check "the creations at once, the waits and the side effect on the members pass" creation_tests_pass
  check "every creation test passes, and the notification after one" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~CreateTaskCommandHandlerTests|FullyQualifiedName~TaskAssignedNotificationTests' 36
  check "ten POSTs at once get ten 201s with ten keys" ten_posts_at_once_pass
  check "check-tasks.sh 3 passes: on the real API ten creations at once get ten numbers" check_tasks_3_passes
}

# ---- 13.2: the columns of a board, and a done column completing its tasks.

json() { # json <JavaScript expression on b, the last answer>: prints its value
  node -e "const b = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(cygpath -m "$scratch/body" 2>/dev/null || echo "$scratch/body")" "$1"
}

columns_of() { # columns_of <project id>: the names from the left, as the API gives them
  status_is 200 "$(as "$admin_token" GET "/api/projects/$1/board-columns")" >/dev/null || return 1
  json "b.map(c => c.name + (c.isDone ? '*' : '')).join(',')"
}

creates_the_board_project() {
  dan_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Board","key":"BRD"}')" || return 1
  brd_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$brd_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$brd_id")" || return 1
  todo_id=$(json "b.columns[0].id")
  doing_id=$(json "b.columns[1].id")
  done_id=$(json "b.columns[2].id")
  [ -n "$todo_id" ] && [ -n "$done_id" ]
}

a_column_is_added_at_the_end() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$brd_id/board-columns" '{"name":" Review ","isDone":false}')" || return 1
  review_id=$(body_field id)
  location=$(location_header)
  is "/api/projects/$brd_id/board-columns/$review_id" "$location" || return 1
  is 3 "$(json "b.position")" || return 1
  status_is 200 "$(as "$admin_token" GET "$location")" || return 1
  is 'De făcut,În lucru,Gata*,Review' "$(columns_of "$brd_id")"
}

a_done_column_completes_its_tasks() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$brd_id/tasks" '{"title":"Pe board","priority":"Low"}')" || return 1
  task_id=$(body_field id)
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$brd_id/board-columns/$todo_id" '{"name":"De făcut","isDone":true}')" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/tasks/$task_id")" || return 1
  is true "$(json "b.completedAt !== null")" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$brd_id/board-columns/$todo_id" '{"name":"De făcut","isDone":false}')" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/tasks/$task_id")" || return 1
  is null "$(json "b.completedAt")"
}

a_column_moves_and_the_others_close_up() {
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$brd_id/board-columns/$done_id/position" '{"position":0}')" || return 1
  is 'Gata*,De făcut,În lucru,Review' "$(json "b.map(c => c.name + (c.isDone ? '*' : '')).join(',')")" || return 1
  is '0,1,2,3' "$(json "b.map(c => c.position).join(',')")"
}

only_an_empty_column_is_deleted() {
  code_is 409 Column.NotEmpty "$(as "$admin_token" DELETE "/api/projects/$brd_id/board-columns/$todo_id")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$brd_id/board-columns/$review_id")" || return 1
  is 'Gata*,De făcut,În lucru' "$(columns_of "$brd_id")" || return 1
  is '0|1|2' "$(in_db "SELECT string_agg(position::text, '|' ORDER BY position) FROM board_columns WHERE project_id = '$brd_id'")"
}

a_member_reads_them_but_only_an_owner_changes_them() {
  status_is 200 "$(as "$dan_token" GET "/api/projects/$brd_id/board-columns")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" POST "/api/projects/$brd_id/board-columns" '{"name":"A lui Dan","isDone":false}')" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" DELETE "/api/projects/$brd_id/board-columns/$doing_id")"
}

the_endpoints_are_written_down() {
  for endpoint in 'GET /api/projects/{id}/board-columns' 'POST /api/projects/{id}/board-columns' \
    'PUT /api/projects/{id}/board-columns/{columnId}' 'PATCH /api/projects/{id}/board-columns/{columnId}/position' \
    'DELETE /api/projects/{id}/board-columns/{columnId}'; do
    in_file "$endpoint" backend/CLAUDE.md || {
      echo "backend/CLAUDE.md doesn't list $endpoint"
      return 1
    }
  done
}

column_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    BoardColumnCommandHandlerTests.Update_ToADoneColumnAndBack_CompletesItsOpenTasksThenOpensThem \
    BoardColumnCommandHandlerTests.Add_TwoAtOnce_TheSecondGets409AndThePlacesStayInOrder \
    BoardColumnCommandHandlerTests.Delete_WhileATaskIsMovedIntoIt_ReturnsNotEmpty \
    BoardColumnCommandHandlerTests.MoveTask_IntoAColumnDeletedMeanwhile_ReturnsAValidationErrorOnColumnId \
    BoardColumnCommandHandlerTests.CreateTask_InAColumnDeletedMeanwhile_ReturnsAValidationErrorOnColumnIdAndTakesNoNumber \
    MoveTaskCommandHandlerTests.Move_IntoTheDoneColumn_CompletesItAndOutClearsThat
}

task_2() {
  echo "13.2 The columns of a board, and a done column completing its tasks"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked "$api_dir/Controllers/BoardColumnsController.cs"
  check "the errors are tracked" tracked backend/src/Kanabol.Application/Features/Projects/ColumnErrors.cs
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the aggregate's column tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~ProjectColumnTests|FullyQualifiedName~TaskItemTests.MoveTo' 20
  check "the handlers' column tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~BoardColumnCommandHandlerTests' 26
  check "a done column, the races with moves and creations, and two additions at once" column_tests_pass
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~BoardColumnsEndpointTests|FullyQualifiedName~EndpointPermissionsTests' 14
  check "backend/CLAUDE.md lists the endpoints" the_endpoints_are_written_down
  check "the real API starts in Production, with a member without permissions" dan_is_ready
  check "a project is created, with Dan as a member" creates_the_board_project
  check "a column is added at the right end, with its Location" a_column_is_added_at_the_end
  check "a column turned done completes its task, and turned back opens it" a_done_column_completes_its_tasks
  check "a column moves to the left end, the others closing up" a_column_moves_and_the_others_close_up
  check "a column with a task stays, an empty one goes, and the places stay 0, 1, 2" only_an_empty_column_is_deleted
  check "a member reads the columns, but only an owner changes them" a_member_reads_them_but_only_an_owner_changes_them
  check "the addition is logged" log_has_entry "Column {ColumnId} added to project {ProjectId}" "\"ColumnId\":\"$review_id\""
  check "the log has no unhandled exception" no_errors_in_the_log
}

if [ "$#" -eq 0 ]; then
  set -- 1 2
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
