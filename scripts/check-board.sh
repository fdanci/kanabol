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
# The API may have stopped for a build since the tokens were taken: it comes back first, and the tokens,
# signed with the same secret, still hold.
admin_is_ready() {
  api_is_up || return 1
  [ -n "${admin_token:-}" ] && return 0
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
  status_is 200 "$(as "$first_token" POST /api/auth/change-password "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

dan_is_ready() {
  admin_is_ready || return 1
  [ -n "${dan_token:-}" ] && return 0
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

# The UPDATE comes after the transaction begins, and the save and the commit after it. Since group 20 the
# transaction runs inside the execution strategy (docs/SPEC.md §5.3), which hands it its own token.
the_counter_moves_in_the_transaction() {
  node -e "
    const s = require('fs').readFileSync('$numbers', 'utf8');
    const at = (text, from = 0) => { const i = s.indexOf(text, from); if (i < 0) { console.log('missing: ' + text); process.exit(1); } return i; };
    const strategy = at('CreateExecutionStrategy().ExecuteAsync(');
    const begin = at('BeginTransactionAsync(', strategy);
    const update = at('UPDATE projects SET next_task_number = next_task_number + 1', begin);
    at('AND archived_at IS NULL', update);
    at('RETURNING next_task_number - 1', update);
    const save = at('SaveChangesAsync(', update);
    const commit = at('transaction.CommitAsync(', save);
    if (s.indexOf('SaveChangesAsync(', commit) < 0) { console.log('no save after the commit, which sends the held events'); process.exit(1); }
  "
}

no_retry_left() {
  lacks 'MaxRetries|ReloadAsync|DbUpdateConcurrencyException|IsUniqueViolation|ChangedMeanwhile|for \(' "$create_handler" &&
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

# ---- 13.2: the columns of a board, and Gata, the done one, completing its tasks.

json() { # json <JavaScript expression on b, the last answer>: prints its value
  node -e "const b = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(cygpath -m "$scratch/body" 2>/dev/null || echo "$scratch/body")" "$1"
}

columns_of() { # columns_of <project id>: the names from the left, as the API gives them
  status_is 200 "$(as "$admin_token" GET "/api/projects/$1/board-columns")" >/dev/null || return 1
  json "b.map(c => c.name + (c.kind === 'Done' ? '*' : '')).join(',')"
}

creates_the_board_project() {
  dan_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Board","key":"BRD"}')" || return 1
  brd_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$brd_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$brd_id")" || return 1
  todo_id=$(json "b.columns[0].id")
  doing_id=$(json "b.columns[1].id")
  done_id=$(json "b.columns[3].id")
  [ -n "$todo_id" ] && [ -n "$done_id" ]
}

# Since group 22 a new column is of the project's own, right before Gata, which stays last.
a_column_is_added_at_the_end() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$brd_id/board-columns" '{"name":" Review "}')" || return 1
  review_id=$(body_field id)
  location=$(location_header)
  is "/api/projects/$brd_id/board-columns/$review_id" "$location" || return 1
  is 3 "$(json "b.position")" || return 1
  is Custom "$(json "b.kind")" || return 1
  status_is 200 "$(as "$admin_token" GET "$location")" || return 1
  is 'Backlog,În lucru,În pauză,Review,Gata*' "$(columns_of "$brd_id")"
}

a_done_column_completes_its_tasks() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$brd_id/tasks" '{"title":"Pe board","priority":"Low"}')" || return 1
  task_id=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_id/position" "{\"columnId\":\"$done_id\"}")" || return 1
  is true "$(json "b.completedAt !== null")" || return 1
  # Out of Gata a task goes to În lucru or after it, never back before it (group 22).
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task_id/position" "{\"columnId\":\"$doing_id\"}")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/tasks/$task_id")" || return 1
  is null "$(json "b.completedAt")"
}

# The project's own move between Backlog and Gata only, and the fixed ones never (group 22).
a_column_moves_and_the_others_close_up() {
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$brd_id/board-columns/$review_id/position" '{"position":1}')" || return 1
  is 'Backlog,Review,În lucru,În pauză,Gata*' "$(json "b.map(c => c.name + (c.kind === 'Done' ? '*' : '')).join(',')")" || return 1
  is '0,1,2,3,4' "$(json "b.map(c => c.position).join(',')")" || return 1
  status_is 400 "$(as "$admin_token" PATCH "/api/projects/$brd_id/board-columns/$review_id/position" '{"position":0}')" || return 1
  code_is 409 Column.Fixed "$(as "$admin_token" PATCH "/api/projects/$brd_id/board-columns/$done_id/position" '{"position":0}')"
}

only_an_empty_column_is_deleted() {
  code_is 409 Column.Fixed "$(as "$admin_token" DELETE "/api/projects/$brd_id/board-columns/$todo_id")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$brd_id/board-columns/$review_id")" || return 1
  is 'Backlog,În lucru,În pauză,Gata*' "$(columns_of "$brd_id")" || return 1
  is '0|1|2|3' "$(in_db "SELECT string_agg(position::text, '|' ORDER BY position) FROM board_columns WHERE project_id = '$brd_id'")"
}

a_member_reads_them_but_only_an_owner_changes_them() {
  status_is 200 "$(as "$dan_token" GET "/api/projects/$brd_id/board-columns")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" POST "/api/projects/$brd_id/board-columns" '{"name":"A lui Dan"}')" || return 1
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
    BoardColumnCommandHandlerTests.Add_TwoAtOnce_TheSecondGets409AndThePlacesStayInOrder \
    BoardColumnCommandHandlerTests.Delete_WhileATaskIsMovedIntoIt_ReturnsNotEmpty \
    BoardColumnCommandHandlerTests.MoveTask_IntoAColumnDeletedMeanwhile_ReturnsAValidationErrorOnColumnId \
    MoveTaskCommandHandlerTests.Move_IntoTheDoneColumn_CompletesItAndOutClearsThat
}

task_2() {
  echo "13.2 The columns of a board, and a done column completing its tasks"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked "$api_dir/Controllers/BoardColumnsController.cs"
  check "the errors are tracked" tracked backend/src/Kanabol.Application/Features/Projects/ColumnErrors.cs
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the aggregate's column tests pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~ProjectColumnTests|FullyQualifiedName~TaskItemTests.MoveTo' 30
  check "the handlers' column tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~BoardColumnCommandHandlerTests' 30
  check "Gata completing a task, a move and a delete racing, and two additions at once" column_tests_pass
  check "the endpoints' tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~BoardColumnsEndpointTests|FullyQualifiedName~EndpointPermissionsTests' 14
  check "backend/CLAUDE.md lists the endpoints" the_endpoints_are_written_down
  check "the real API starts in Production, with a member without permissions" dan_is_ready
  check "a project is created, with Dan as a member" creates_the_board_project
  check "a column of the project's own comes right before Gata, with its Location" a_column_is_added_at_the_end
  check "a task that enters Gata is completed, and open again once it leaves" a_done_column_completes_its_tasks
  check "it moves after Backlog, the others closing up, and Gata never moves" a_column_moves_and_the_others_close_up
  check "a fixed column stays, an empty one of the project's own goes, and the places stay 0, 1, 2" only_an_empty_column_is_deleted
  check "a member reads the columns, but only an owner changes them" a_member_reads_them_but_only_an_owner_changes_them
  check "the addition is logged" log_has_entry "Column {ColumnId} added to project {ProjectId}" "\"ColumnId\":\"$review_id\""
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- The frontend: specs, lint, the production build and the texts.

web=frontend
app=$web/src/app
board=$app/features/board

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

messages_lack() { # messages_lack <text>: no text of the app has it
  extracted || return 1
  if in_file "$1" "$scratch/locale/messages.xlf"; then
    echo "a text still says $1"
    return 1
  fi
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

no_colors_by_hand() {
  lacks '#[0-9a-fA-F]{3,8}\b' "$board"/*.ts "$board"/*.scss "$board"/*.html
}

no_deep_selectors() {
  lacks '::ng-deep|\.mat-mdc-|\.mdc-' "$board"/*.ts "$board"/*.scss
}

# ---- 13.3: the board, with drag and drop, moving at once and back when refused.

the_board_loads_with_its_first_visit() {
  grep -qF "path: 'board'," "$app/features/projects/projects.routes.ts" &&
    grep -qF "import('../board/board-page').then((page) => page.BoardPage)" "$app/features/projects/projects.routes.ts" || {
    echo "/projects/:id/board doesn't load the board feature"
    return 1
  }
}

the_board_is_cdk_drag_and_drop() {
  in_file 'cdkDropListGroup' "$board/board-page.html" &&
    in_file 'cdkDropList' "$board/board-column.ts" &&
    in_file 'cdkDrag' "$board/board-column.ts" &&
    in_file '(cdkDropListDropped)' "$board/board-column.ts"
}

the_store_reads_through_a_resource() {
  in_file 'resource({' "$board/board-store.ts" &&
    in_file "params: () => this.projectId()," "$board/board-store.ts" &&
    in_file '/position' "$board/board-api.ts"
}

# docs/SPEC.md §6.1: the board reaches the panel and its changes through shared/, not the tasks feature.
the_board_imports_no_other_feature() {
  lacks "from '\.\./(tasks|projects|users|roles)/" "$board"/*.ts
}

creates_the_cards_project() {
  dan_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Carduri","key":"CRD"}')" || return 1
  crd_id=$(body_field id)
  for title in 'Primul card' 'Al doilea card' 'Arhivat'; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$crd_id/tasks" "{\"title\":\"$title\",\"priority\":\"Low\"}")" || return 1
  done
  archived_id=$(body_field id)
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$archived_id")"
}

the_board_answers_every_open_task() {
  status_is 200 "$(as "$admin_token" GET "/api/projects/$crd_id/board")" || return 1
  is 'CRD|true|true|4' "$(json "[b.projectKey, b.canMove, b.canManage, b.columns.length].join('|')")" || return 1
  is 'CRD-1:0,CRD-2:1' "$(json "b.tasks.map(t => t.key + ':' + t.position).join(',')")" || return 1
  code_is 403 Project.MemberRequired "$(as "$dan_token" GET "/api/projects/$crd_id/board")"
}

task_3() {
  echo "13.3 The board, with drag and drop, moving at once and back when refused"
  for file in board-page.ts board-page.html board-page.scss board-column.ts task-card.ts board-store.ts \
    board-api.ts board-moves.ts models.ts; do
    check "features/board/$file is tracked" tracked "$board/$file"
  done
  check "TaskChanges moved to shared/" tracked "$app/shared/task-changes.ts"
  check "the board endpoint is tracked" tracked backend/src/Kanabol.Application/Features/Tasks/Queries/GetBoard/GetBoardQueryHandler.cs
  check "the solution builds without warnings" solution_builds
  check "the board endpoint's tests pass" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetBoardQueryHandlerTests' 8
  check "and over HTTP" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~BoardEndpointTests|FullyQualifiedName~EndpointPermissionsTests' 9
  check "the board page's specs pass, a refused move among them" specs_pass src/app/features/board/board-page.spec.ts 14
  check "the projects pages still pass, with the board in them" specs_pass "src/app/features/projects/*.spec.ts" 64
  check "the tasks pages still pass, with TaskChanges in shared/" specs_pass "src/app/features/tasks/*.spec.ts" 49
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "/projects/:id/board is the board, loaded on its first visit" the_board_loads_with_its_first_visit
  check "the cards move through the CDK's drag and drop" the_board_is_cdk_drag_and_drop
  check "the store reads through resource() and moves through PATCH position" the_store_reads_through_a_resource
  check "the board imports no other feature" the_board_imports_no_other_feature
  check "the board writes no colors by hand" no_colors_by_hand
  check "nor reaches into Material's classes" no_deep_selectors
  check "the production build has the board" bundle_has 'kb-board-column'
  check "the board says when it can't be read" messages_have 'Nu am putut încărca board-ul.'
  check "a new card asks for its title" messages_have 'Titlul task-ului'
  check "the real API starts in Production, with a member without permissions" dan_is_ready
  check "a project with two open tasks and an archived one is created" creates_the_cards_project
  check "its board has the columns and the open tasks in their places, and not for an outsider" the_board_answers_every_open_task
  check "the log has no unhandled exception" no_errors_in_the_log
}

# ---- 13.4: moving cards from the keyboard, with what it says, and the filters in the address.

the_keys_are_the_ui_spec_ones() {
  for key in "event.key === ' '" ArrowLeft ArrowRight ArrowUp ArrowDown "event.key === 'Escape'"; do
    in_file "$key" "$board/board-page.ts" || {
      echo "the board doesn't answer $key"
      return 1
    }
  done
}

it_says_each_step() {
  in_file 'LiveAnnouncer' "$board/board-page.ts" && in_file 'this.announcer.announce(' "$board/board-page.ts"
}

the_filters_are_the_shared_ones() {
  in_file 'kb-task-filter-bar' "$board/board-page.html" &&
    in_file 'filtersFromQuery(this.query())' "$board/board-page.ts" &&
    in_file 'filtersToQuery(' "$board/board-page.ts"
}

task_4() {
  echo "13.4 Moving cards from the keyboard, and the filters in the address"
  check "the filters are tracked" tracked "$board/board-filters.ts"
  check "the board page's specs pass, the keyboard and the filters among them" specs_pass src/app/features/board/board-page.spec.ts 27
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "Space, the arrows and Esc move a card, as UI spec 7.4 has them" the_keys_are_the_ui_spec_ones
  check "each step is said through aria-live" it_says_each_step
  check "the filters are the List view's, read from and written to the address" the_filters_are_the_shared_ones
  check "a picked-up card says how to move it" messages_have 'Mută-l cu săgețile, lasă-l cu Space, renunță cu Esc.'
  check "a card let go says where it landed" messages_have 'Mutat în «'
  check "a card put back says so" messages_have 'Mutarea a fost anulată.'
  check "the board says when no card matches" messages_have 'Niciun task nu se potrivește filtrelor.'
}

# ---- 13.5: configuring the columns from the board.

the_columns_use_their_endpoints() {
  for path in '/board-columns`, { name }' '/board-columns/${columnId}`, {' '/board-columns/${columnId}/position`' 'delete(`/projects/${projectId}/board-columns/${columnId}`)'; do
    in_file "$path" "$board/board-api.ts" || {
      echo "board-api.ts doesn't call $path"
      return 1
    }
  done
}

the_menu_is_for_owners_only() {
  in_file '@if (canManage()) {' "$board/board-column.ts" &&
    in_file '[canManage]="store.canManage()"' "$board/board-page.html" &&
    in_file '@if (store.canManage()) {' "$board/board-page.html"
}

only_an_empty_column_is_offered_for_deletion() {
  in_file 'this.store.columns().length > 1 && this.store.cardsIn(column.id).length === 0' "$board/board-page.ts" &&
    in_file 'this.confirmation.ask({' "$board/board-page.ts"
}

task_5() {
  echo "13.5 Configuring the columns from the board"
  check "the board page's specs pass, the columns among them" specs_pass src/app/features/board/board-page.spec.ts 36
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the columns go through their endpoints" the_columns_use_their_endpoints
  check "the ⋮ and the new column are for owners and projects.manage" the_menu_is_for_owners_only
  check "only an empty column, not the last, is offered for deletion, after a question" only_an_empty_column_is_offered_for_deletion
  check "the board writes no colors by hand" no_colors_by_hand
  check "nor reaches into Material's classes" no_deep_selectors
  check "the menu renames" messages_have 'Redenumește'
  check "and no longer marks a column done: Gata is the done one (group 22)" messages_lack 'Marchează ca terminată'
  check "and asks before deleting" messages_have 'Ștergi coloana «'
  check "a column holding archived tasks says why it stays" messages_have 'Coloana mai are task-uri, poate și arhivate.'
}

# ---- 13.6: the specs of BoardStore, moving and putting back.

store_specs_name_the_rollback() {
  for name in 'puts a refused card back where it was, as it was' 'puts back only the refused card when another moved meanwhile' \
    'moves it at once, before the API answers'; do
    in_file "$name" "$board/board-store.spec.ts" || {
      echo "no spec '$name' in board-store.spec.ts"
      return 1
    }
  done
}

task_6() {
  echo "13.6 The specs of BoardStore: moving, and putting back when refused"
  check "the store's specs are tracked" tracked "$board/board-store.spec.ts"
  check "the moves' specs are tracked" tracked "$board/board-moves.spec.ts"
  check "the store's specs name the move and the rollbacks" store_specs_name_the_rollback
  check "the store's specs pass" specs_pass src/app/features/board/board-store.spec.ts 19
  check "the moves' specs pass" specs_pass src/app/features/board/board-moves.spec.ts 11
  check "every spec of the board passes" specs_pass "src/app/features/board/*.spec.ts" 66
  check "the lint is clean" frontend_lint_is_clean
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
