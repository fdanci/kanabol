#!/bin/sh
# Acceptance checks for a task's history (group 16 in the project's Tasks.md; docs/SPEC.md §4.4 "Istoricul
# unui task", §10, §13.3; docs/UI_UX_SPEC.md §7.5). The tests and specs hold the rules, so these checks
# run them, and add what they can't see: the real API process on a real PostgreSQL, its answers, and the
# frontend's production build.
#
# Usage: scripts/check-history.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  GET /api/tasks/{id}/history: the tests, then the real API in Production: a Page from the newest, 50
#      by default, who changed it, the names of the assignees and the columns, an archived task, an
#      outsider. Docker, PostgreSQL.
#   2  the "Istoric" tab of the task's panel: specs, lint, formatting, the production build. Only the
#      packages that scripts/setup.sh installs.
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database and
# drops it. Docker runs the tests' own PostgreSQL.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_history_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5298
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
api_environment=
admin_connection=

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

native() { # native <path>: as Windows programs (node, dotnet) read it
  cygpath -m "$1" 2>/dev/null || echo "$1"
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

# ---- A database of this run, and the real API on it.

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

# The API in Production unless asked otherwise, with any other settings given as NAME=value. A start in
# another environment, or with settings, stops the one running first; the next plain start goes back.
start_api() { # start_api <environment> [NAME=value...]
  wanted="$*"
  [ -n "$api_pid" ] && [ "$api_environment" = "$wanted" ] && return 0
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  environment=$1
  shift
  (
    cd "$api_dir" || exit 1
    # Seq's address leads nowhere, so a developer's own Seq gets nothing of these checks.
    exec env ASPNETCORE_ENVIRONMENT="$environment" \
      ConnectionStrings__Default="$(connection_to "$check_db")" \
      Jwt__Secret=only-for-these-checks-and-long-enough-to-pass \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword=$admin_password \
      Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 \
      "$@" dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$api_log" 2>&1 &
  api_pid=$!
  api_environment=$wanted

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

api_is_up() {
  start_api Production
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

# ---- Requests to the real API.

# An address no other request of this run has used, so the limit per address stays out of the way.
# The count is in a file, because every caller runs in a subshell, where a new value would be lost.
unused_address() {
  printf x >>"$scratch/used-addresses"
  used=$(wc -c <"$scratch/used-addresses")
  printf '10.83.%s.%s' "$((used / 256))" "$((used % 256))"
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  rm -f "$scratch/body"
  curl -sS --max-time 30 -o "$scratch/body" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

as() { # as <token> <method> <path> [JSON body]
  token=$1
  method=$2
  path=$3
  if [ "$#" -ge 4 ]; then
    # From a file: Windows hands curl its arguments in the ANSI code page, where ă turns into a.
    printf '%s' "$4" >"$scratch/request.json"
    send "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
      --data-binary "@$(native "$scratch/request.json")"
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
    "$(native "$scratch/body")" "$1"
}

status_is() { # status_is <expected status> <actual status>: shows the answer when it differs
  is "$1" "$2" || {
    cat "$scratch/body" 2>/dev/null
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

# The first admin, whose first token still has to change the password: it goes in first_token.
first_admin_token() {
  api_is_up || return 1
  [ -n "${first_token:-}" ] && return 0
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
}

# The first admin past the forced password change. The API may have stopped since the token was taken:
# it comes back first, and the token, signed with the same secret, still holds.
admin_is_ready() {
  api_is_up || return 1
  [ -n "${admin_token:-}" ] && return 0
  first_admin_token || return 1
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

# Project CMT, owned by the admin, with Dan and Ioana as members; Eva is from outside. The admin creates
# task CMT-1, "Login SSO", for Dan.

# The colleague's display name, as the API keeps it.
display_name_of() { # display_name_of <user id>
  in_db "SELECT display_name FROM users WHERE id = '$1'"
}

# Project HIS, owned by the admin, with Dan and Ioana as members; Eva is from outside.
team_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready ioana ioana@example.com 'Ioana Marin' || return 1
  colleague_is_ready eva eva@example.com 'Eva Din Afara' || return 1
  [ -n "${his_id:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Istoric","key":"HIS"}')" || return 1
  his_id=$(body_field id)
  for member in "$dan_id" "$ioana_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$his_id/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  first_column=$(in_db "SELECT id FROM board_columns WHERE project_id = '$his_id' AND position = 0")
  second_column=$(in_db "SELECT id FROM board_columns WHERE project_id = '$his_id' AND position = 1")
}

# A task of its own for each check, created by the admin, its id in new_task.
a_new_task() { # a_new_task <title> [JSON fields to add]
  team_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$his_id/tasks" "{\"title\":\"$1\",\"priority\":\"Medium\"${2:+,$2}}")" || return 1
  new_task=$(body_field id)
}

history_as() { # history_as <token> <task id> [query]: prints the status, the page in the last answer
  as "$1" GET "/api/tasks/$2/history${3:+?$3}"
}

# ---- 16.1: GET /api/tasks/{id}/history.

history_app=backend/src/Kanabol.Application/Features/Tasks/Queries/GetTaskHistory

history_handler_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~TaskHistoryQueryHandlerTests' 19
}

history_endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~TaskHistoryEndpointTests' 8
}

a_new_task_has_an_empty_page_of_fifty() {
  a_new_task 'Nou' || return 1
  status_is 200 "$(history_as "$ioana_token" "$new_task")" || return 1
  is '1|50|0|0' "$(json "[b.page, b.pageSize, b.totalCount, b.items.length].join('|')")"
}

# Dan renames it, then the admin raises its priority: the admin's change first, each with who made it.
changes_come_from_the_newest_with_who_made_them() {
  a_new_task 'Login SSO' || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$new_task/title" '{"title":"Login cu SSO"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$new_task/priority" '{"priority":"High"}')" || return 1
  status_is 200 "$(history_as "$ioana_token" "$new_task")" || return 1
  is "Priority:Medium:High:$admin_id:$(display_name_of "$admin_id")|Title:Login SSO:Login cu SSO:$dan_id:Dan Ionescu" \
    "$(json "b.items.map(e => [e.field, e.oldValue, e.newValue, e.changedBy.userId, e.changedBy.displayName].join(':')).join('|')")" || return 1
  is true "$(json "b.items[0].changedAt > b.items[1].changedAt && Object.keys(b.items[0].names).length === 0")"
}

# The admin gives the task to Dan and Ioana, then to Dan alone, and takes Ioana out of the project: both
# entries still name her.
assignees_are_named_even_after_they_left() {
  a_new_task 'Asignati' || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$new_task/assignees" "{\"assigneeIds\":[\"$dan_id\",\"$ioana_id\"]}")" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$new_task/assignees" "{\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$his_id/members/$ioana_id")" || return 1
  printf yes >"$scratch/ioana-removed"
  status_is 200 "$(history_as "$dan_token" "$new_task")" || return 1
  is 'Assignees:Dan Ionescu,Ioana Marin|Assignees:Dan Ionescu,Ioana Marin' \
    "$(json "b.items.map(e => e.field + ':' + [...new Set((e.oldValue + ',' + e.newValue).split(',').filter(id => id))].map(id => e.names[id]).sort().join()).join('|')")" || return 1
  is '' "$(json "b.items[1].oldValue")"
}

# Ioana comes back, for the checks after this one.
ioana_is_a_member_again() {
  [ -f "$scratch/ioana-removed" ] || return 0
  status_is 201 "$(as "$admin_token" POST "/api/projects/$his_id/members" "{\"userId\":\"$ioana_id\",\"role\":\"Member\"}")" &&
    rm "$scratch/ioana-removed"
}

# A column "Temporar" holds a task, which moves to the first column; then the column goes. The move names
# the first column, and nothing for the one deleted.
a_deleted_column_has_no_name() {
  team_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$his_id/board-columns" '{"name":"Temporar","isDone":false}')" || return 1
  temporary=$(body_field id)
  a_new_task 'Mutat' "\"columnId\":\"$temporary\"" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$first_column\"}")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$his_id/board-columns/$temporary")" || return 1
  status_is 200 "$(history_as "$ioana_token" "$new_task")" || return 1
  is "ColumnId:$temporary:$first_column" "$(json "b.items.map(e => [e.field, e.oldValue, e.newValue].join(':')).join('|')")" || return 1
  is "true:null:$(in_db "SELECT name FROM board_columns WHERE id = '$first_column'")" \
    "$(json "[('$temporary' in b.items[0].names), String(b.items[0].names['$temporary']), b.items[0].names['$first_column']].join(':')")"
}

# Radu, a member, renames a task and is deactivated: the entry still names him.
a_deactivated_author_is_still_named() {
  team_is_ready || return 1
  colleague_is_ready radu radu@example.com 'Radu Vlad' || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$his_id/members" "{\"userId\":\"$radu_id\",\"role\":\"Member\"}")" || return 1
  a_new_task 'Al lui Radu' || return 1
  status_is 200 "$(as "$radu_token" PUT "/api/tasks/$new_task/title" '{"title":"Redenumit de Radu"}')" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/users/$radu_id/deactivate")" || return 1
  status_is 200 "$(history_as "$dan_token" "$new_task")" || return 1
  is "$radu_id:Radu Vlad" "$(json "b.items[0].changedBy.userId + ':' + b.items[0].changedBy.displayName")"
}

# 52 renames: 50 on the first page, from the newest, and the last two on the second.
the_pages_hold_fifty_from_the_newest() {
  a_new_task 'Titlul 0' || return 1
  for number in $(seq 1 52); do
    status_is 200 "$(as "$admin_token" PUT "/api/tasks/$new_task/title" "{\"title\":\"Titlul $number\"}")" || return 1
  done
  status_is 200 "$(history_as "$dan_token" "$new_task")" || return 1
  is '1|50|52|50|Titlul 52|Titlul 3' "$(json "[b.page, b.pageSize, b.totalCount, b.items.length, b.items[0].newValue, b.items[49].newValue].join('|')")" || return 1
  status_is 200 "$(history_as "$dan_token" "$new_task" page=2)" || return 1
  is '2|Titlul 2,Titlul 1' "$(json "b.page + '|' + b.items.map(e => e.newValue).join()")" || return 1
  status_is 200 "$(history_as "$dan_token" "$new_task" 'page=3&pageSize=20')" || return 1
  is '3|20|Titlul 12' "$(json "[b.page, b.pageSize, b.items[0].newValue].join('|')")"
}

a_page_out_of_range_is_400_on_it() {
  a_new_task 'Pagini' || return 1
  status_is 400 "$(history_as "$dan_token" "$new_task" pageSize=101)" || return 1
  is pageSize "$(json "Object.keys(b.errors).join()")" || return 1
  status_is 400 "$(history_as "$dan_token" "$new_task" page=0)" || return 1
  is page "$(json "Object.keys(b.errors).join()")"
}

# Archived by the admin: Dan, a member, gets 404 Task.NotFound; the admin, with tasks.archive, reads it.
an_archived_tasks_history_is_only_for_tasks_archive() {
  a_new_task 'Arhivat' || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$new_task")" || return 1
  code_is 404 Task.NotFound "$(history_as "$dan_token" "$new_task")" || return 1
  status_is 200 "$(history_as "$admin_token" "$new_task")" || return 1
  is 'ArchivedAt::set' "$(json "b.items.map(e => [e.field, e.oldValue ?? '', e.newValue ? 'set' : ''].join(':')).join('|')")"
}

an_outsider_reads_nothing() {
  a_new_task 'Intern' || return 1
  code_is 403 Project.MemberRequired "$(history_as "$eva_token" "$new_task")"
}

a_missing_task_is_404() {
  team_is_ready || return 1
  code_is 404 Task.NotFound "$(history_as "$dan_token" 0193a000-0000-7000-8000-000000000000)"
}

task_1() {
  echo "16.1 GET /api/tasks/{id}/history: a Page from the newest, who changed it, and the names in its values"
  check "Docker is running" docker_is_running
  check "the query is tracked" tracked "$history_app/GetTaskHistoryQuery.cs"
  check "its handler is tracked" tracked "$history_app/GetTaskHistoryQueryHandler.cs"
  check "its entry is tracked" tracked "$history_app/TaskHistoryEntryDto.cs"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the handler's tests pass: the order, the pages, who sees it, the names" history_handler_tests_pass
  check "the endpoint's tests pass" history_endpoint_tests_pass
  check "a new task has an empty page of 50" a_new_task_has_an_empty_page_of_fifty
  check "the changes come from the newest, each with who made it" changes_come_from_the_newest_with_who_made_them
  check "the assignees are named, even one taken out of the project" assignees_are_named_even_after_they_left
  check "the one taken out comes back, for the checks after" ioana_is_a_member_again
  check "a move names the columns, nothing for a column deleted since" a_deleted_column_has_no_name
  check "someone deactivated since is still named" a_deactivated_author_is_still_named
  check "52 entries come 50 to a page, from the newest" the_pages_hold_fifty_from_the_newest
  check "a page out of range is 400 on page or pageSize" a_page_out_of_range_is_400_on_it
  check "an archived task's history is 404 Task.NotFound, but for tasks.archive" an_archived_tasks_history_is_only_for_tasks_archive
  check "an outsider reads nothing, 403 Project.MemberRequired" an_outsider_reads_nothing
  check "a task that doesn't exist is 404 Task.NotFound" a_missing_task_is_404
  check "backend/CLAUDE.md says how the history works" in_file '`GET /api/tasks/{id}/history' backend/CLAUDE.md
}
# ---- The frontend.

web=frontend
app=$web/src/app
tasks_web=$app/features/tasks

plain() { # Without the colors the tools print
  sed 's/\x1b\[[0-9;]*m//g' "$1"
}

lacks() { # lacks <extended regex> <file...>
  ! grep -nE -- "$@" || return 1
}

specs_pass() { # specs_pass <spec file or glob, from frontend/> <minimum passed>
  log="$scratch/specs-$(printf '%s' "$1" | tr '/*{},' '______').log"
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
  touch "$scratch/built"
}

css_has() { # css_has <text>: compares without whitespace, which the minifier keeps in places
  built || return 1
  tr -d ' \n\r' <"$(ls "$scratch"/dist/browser/styles-*.css | head -n 1)" | grep -qF -- "$1" || {
    echo "not in the built CSS: $1"
    return 1
  }
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


# ---- 16.2: the "Istoric" tab of the task's panel.

app=$web/src/app
tasks_web=$app/features/tasks

the_timeline_specs_name_the_rules() {
  spec_names "$tasks_web/task-timeline.spec.ts" \
    'shows each change from the newest, as who changed what, and when' \
    'writes the description, an archiving and a restoring as sentences of their own' \
    'loads 50 more with "Încarcă mai multe", only while there are more' \
    'keeps each change once when the next page repeats one, after a change came meanwhile' \
    'reads the history again, from the newest, after the panel saves a change' \
    'keeps the changes it shows while it reads them again' \
    'says when nothing has changed yet' &&
    spec_names "$tasks_web/task-history.spec.ts" \
      'writes a title and a priority as old → new, the priority by its label' \
      'writes a deadline as its day in ro-RO, and "fără deadline" for none' \
      'writes a move as Stare with the names of the columns, "coloană ștearsă" for one gone' \
      'names the assignees from the entry, "Neasignat" for nobody' \
      'says only that the description changed, without its texts' \
      'tells an archiving from a restoring' &&
    spec_names "$tasks_web/task-panel.spec.ts" \
      'reads the history when its tab opens, and again after a change saved in the panel' &&
    spec_names "$tasks_web/tasks-api.spec.ts" \
      'reads a page of a task’s history, 50 at a time'
}

# Lazy content: the tab makes the timeline when it opens, and the panel's saves move its revision.
the_panel_reads_the_history_when_its_tab_opens() {
  in_file '<ng-template matTabContent>' "$tasks_web/task-panel.html" &&
    in_file '<kb-task-timeline [taskId]="task.id" [revision]="store.savedChanges()" />' "$tasks_web/task-panel.html" &&
    in_file 'this.savedChanges.update((count) => count + 1);' "$tasks_web/task-panel-store.ts" &&
    lacks 'vine într-o versiune următoare' "$tasks_web/task-panel.html"
}

# docs/SPEC.md §9: the history isn't heard, only read.
the_timeline_listens_to_no_hub() {
  lacks 'Realtime|signalr|core/realtime' "$tasks_web/task-timeline.ts" "$tasks_web/task-timeline-store.ts" "$tasks_web/task-history.ts"
}

# docs/UI_UX_SPEC.md §7.5, the texts on each field.
the_timeline_has_the_ui_specs_words() {
  for words in 'a schimbat <em>' 'a schimbat descrierea' 'a arhivat task-ul' 'l-a restaurat' 'Încarcă mai multe'; do
    in_file "$words" "$tasks_web/task-timeline.ts" || {
      echo "no '$words' in task-timeline.ts"
      return 1
    }
  done
  for words in 'coloană ștearsă' 'fără deadline' 'Neasignat' 'Prioritate' 'Stare' 'Asignați' 'Titlu' 'Deadline'; do
    in_file "\`$words\`" "$tasks_web/task-history.ts" || {
      echo "no \$localize\`$words\` in task-history.ts"
      return 1
    }
  done
}

the_timeline_uses_tokens_only() {
  lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-|innerHTML|bypassSecurityTrust' \
    "$tasks_web/task-timeline.ts" "$tasks_web/task-timeline-store.ts" "$tasks_web/task-history.ts" &&
    in_file 'background: var(--kb-accent-600);' "$tasks_web/task-timeline.ts"
}

task_2() {
  echo "16.2 The \"Istoric\" tab: a timeline from the newest, 50 at a time, read when it opens and after each save"
  for file in task-timeline.ts task-timeline-store.ts task-history.ts tasks-api.spec.ts; do
    check "features/tasks/$file is tracked" tracked "$tasks_web/$file"
  done
  check "the specs name the rules of the timeline" the_timeline_specs_name_the_rules
  check "the specs of the timeline and its texts pass" specs_pass 'src/app/features/tasks/{task-timeline,task-history,tasks-api}.spec.ts' 20
  check "every spec of the tasks feature passes" specs_pass 'src/app/features/tasks/*.spec.ts' 109
  check "the panel reads the history when its tab opens, and after each save" the_panel_reads_the_history_when_its_tab_opens
  check "the timeline listens to no hub" the_timeline_listens_to_no_hub
  check "the timeline has the UI spec's words for each field" the_timeline_has_the_ui_specs_words
  check "the timeline uses the theme's tokens and nothing unsafe" the_timeline_uses_tokens_only
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "frontend/CLAUDE.md says how it works" in_file '`kb-task-timeline`' frontend/CLAUDE.md
}

if [ "$#" -eq 0 ]; then
  set -- 1 2
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
