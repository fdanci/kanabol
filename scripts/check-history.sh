#!/bin/sh
# Acceptance checks for a task's history (groups 16 and 16b in the project's Tasks.md; docs/SPEC.md §4.4
# "Istoricul unui task", §2, §10, §13.3; docs/UI_UX_SPEC.md §7.5). The tests and specs hold the rules, so
# these checks run them, and add what they can't see: the real API process on a real PostgreSQL, its
# answers, and the frontend's production build.
#
# Usage: scripts/check-history.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  GET /api/tasks/{id}/history: the tests, then the real API in Production: a Page from the newest, 50
#      by default, who changed it, the names of the assignees and the columns, an archived task, an
#      outsider. Docker, PostgreSQL.
#   2  the "Istoric" tab of the task's panel: specs, lint, formatting, the production build. Only the
#      packages that scripts/setup.sh installs.
#   3  the panel's priority select, with the priority's chip in its trigger: specs, lint, formatting, the
#      production build. Only the packages.
#   4  Storage:AllowedExtensions as one value with commas: the tests, then the real API in Production on
#      the default list, on compose's Storage__AllowedExtensions=pdf,png, which replaces the whole list,
#      and refusing to start on a list by index. Docker, PostgreSQL.
#   5  the group's flows end to end: the API's tests (every change writes its entry and nothing else does,
#      an archived task, the names, the pages, compose's list), then every change on the real API, with
#      the dates to the microsecond. Docker, PostgreSQL.
#   6  a description's texts on demand and their diff (group 16b): the tests and the flows, then the real
#      API in Production: the list without the texts, the entry with them, 404 History.NotFound on another
#      task's entry, an outsider, an archived task; then the diff's block: specs, lint, formatting, the
#      production build, the diff package only in the task panel's chunk, never in the initial bundle.
#      Docker, PostgreSQL and the packages.
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

# A column "Temporar", right after the first, gets a task, which moves back to the first column; then the
# column goes. The moves name the first column, and nothing for the one deleted. Since group 22 a task
# starts in Backlog, and goes back only from a column before În lucru.
a_deleted_column_has_no_name() {
  team_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$his_id/board-columns" '{"name":"Temporar"}')" || return 1
  temporary=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$his_id/board-columns/$temporary/position" '{"position":1}')" || return 1
  a_new_task 'Mutat' || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$temporary\"}")" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$first_column\"}")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$his_id/board-columns/$temporary")" || return 1
  status_is 200 "$(history_as "$ioana_token" "$new_task")" || return 1
  is "ColumnId:$temporary:$first_column|ColumnId:$first_column:$temporary" "$(json "b.items.map(e => [e.field, e.oldValue, e.newValue].join(':')).join('|')")" || return 1
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

# ---- 16.3: the panel's priority select shows its chip.

the_priority_spec_names_the_rule() {
  spec_names "$tasks_web/task-panel.spec.ts" \
    'shows the priority as its chip in the select, not the name of its icon' \
    'changes the priority and the column where they are'
}

# docs/UI_UX_SPEC.md §7.5: mat-select-trigger with kb-chip, as the assignees have their own trigger.
the_priority_select_has_a_trigger_of_its_own() {
  node -e "
    const html = require('fs').readFileSync(process.argv[1], 'utf8');
    const start = html.indexOf('#prioritySelect');
    const end = html.indexOf('</mat-select>', start);
    const select = html.slice(start, end);
    const trigger = select.slice(select.indexOf('<mat-select-trigger>'), select.indexOf('</mat-select-trigger>'));
    if (start < 0 || !trigger.includes('<kb-chip [kind]=\"\$any(chosen.toLowerCase())\">') || !trigger.includes('priorityLabel(chosen)')) {
      console.log('the priority select has no mat-select-trigger with its kb-chip');
      process.exit(1);
    }" "$(native "$tasks_web/task-panel.html")" &&
    in_file 'protected readonly chosenPriority = linkedSignal(' "$tasks_web/task-panel.ts"
}

task_3() {
  echo "16.3 The panel's priority select shows the priority's chip, not its icon's name"
  check "the specs name the rule" the_priority_spec_names_the_rule
  check "the specs of the panel pass" specs_pass 'src/app/features/tasks/task-panel.spec.ts' 24
  check "the priority select has a mat-select-trigger with kb-chip" the_priority_select_has_a_trigger_of_its_own
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "frontend/CLAUDE.md says how it works" in_file 'the priority select shows its `kb-chip`' frontend/CLAUDE.md
}

# ---- 16.4: Storage:AllowedExtensions, one value with commas.

upload() { # upload <token> <task id> <name it goes as>: a small PDF under that name; prints the status
  [ -f "$scratch/brief.pdf" ] || printf '%%PDF-1.7 un brief de proba\n' >"$scratch/brief.pdf"
  send POST "/api/tasks/$2/attachments" -H "Authorization: Bearer $1" \
    -F "file=@$(native "$scratch/brief.pdf");filename=$3"
}

refused_on_file() { # refused_on_file <status>: a 400 whose only error is on file
  status_is 400 "$1" || return 1
  is file "$(json "Object.keys(b.errors).join()")"
}

storage_options_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~StorageOptionsTests|FullyQualifiedName~LocalFileStorageTests' 15
}

the_default_list_is_read_from_appsettings() {
  named_tests_pass Kanabol.Api.Tests ApiFactoryTests.Storage_ByDefault_AllowsTheExtensionsOfTheSpec
}

# docs/SPEC.md §10: the default, one value with commas, written out here so a change to it shows.
appsettings_holds_one_value_with_commas() {
  is 'string:pdf,png,jpg,jpeg,gif,txt,md,csv,xlsx,docx,zip' "$(node -e "
    const settings = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'));
    console.log(typeof settings.Storage.AllowedExtensions + ':' + settings.Storage.AllowedExtensions);" \
    "$(native backend/src/Kanabol.Api/appsettings.json)")"
}

# docs/SPEC.md §13.3 writes the comment compose holds, word for word.
compose_says_how_to_override_the_whole_list() {
  comment='# The allowed extensions have a default in appsettings.json; override the whole list here as Storage__AllowedExtensions=pdf,png (one value, commas; group 16)'
  in_file "$comment" docs/SPEC.md && in_file "$comment" compose.yaml
}

# The value compose's comment gives, as the environment variable it names.
compose_override() {
  sed -n 's/.*override the whole list here as \(Storage__AllowedExtensions=[^ ]*\) .*/\1/p' compose.yaml | head -n 1
}

# The API on the default list: png goes in, exe doesn't.
the_default_list_holds_on_the_real_api() {
  a_new_task 'Fisiere' || return 1
  status_is 201 "$(upload "$dan_token" "$new_task" poza.png)" || return 1
  refused_on_file "$(upload "$dan_token" "$new_task" unealta.exe)"
}

# The API with compose's override, Storage__AllowedExtensions=pdf,png: pdf and png go in, txt and zip,
# which the default list has, don't.
compose_override_replaces_the_whole_list() {
  a_new_task 'Fisiere cu lista din compose' || return 1
  override=$(compose_override)
  is 'Storage__AllowedExtensions=pdf,png' "$override" || return 1
  start_api Production "$override" || return 1
  status_is 201 "$(upload "$dan_token" "$new_task" brief.pdf)" || return 1
  status_is 201 "$(upload "$dan_token" "$new_task" poza.png)" || return 1
  refused_on_file "$(upload "$dan_token" "$new_task" note.txt)" || return 1
  refused_on_file "$(upload "$dan_token" "$new_task" arhiva.zip)" || return 1
  is 'pdf, png' "$(json "b.errors.file[0].replace(/.*These can: /, '').replace(/\.$/, '')")"
}

# Another override, as one value: exe goes in, and nothing of the default list stays.
another_list_replaces_it_too() {
  a_new_task 'Fisiere cu alta lista' || return 1
  start_api Production 'Storage__AllowedExtensions=exe, .TXT' || return 1
  status_is 201 "$(upload "$dan_token" "$new_task" unealta.exe)" || return 1
  status_is 201 "$(upload "$dan_token" "$new_task" note.txt)" || return 1
  refused_on_file "$(upload "$dan_token" "$new_task" brief.pdf)" || return 1
  start_api Production
}

# A list by index, as before group 16, would be dropped without a word: the API stops at once, and says why.
a_list_by_index_refuses_to_start() {
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  log="$scratch/by-index.log"
  (
    cd "$api_dir" || exit 1
    exec env ASPNETCORE_ENVIRONMENT=Production \
      ConnectionStrings__Default="$(connection_to "$check_db")" \
      Jwt__Secret=only-for-these-checks-and-long-enough-to-pass \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword=$admin_password \
      Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 \
      Storage__AllowedExtensions__0=exe \
      dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$log" 2>&1 &
  pid=$!
  waited=0
  while kill -0 "$pid" 2>/dev/null; do
    waited=$((waited + 1))
    if [ "$waited" -gt 120 ]; then
      kill "$pid"
      echo "the API was still running after 60 seconds"
      return 1
    fi
    sleep 0.5
  done
  wait "$pid" && {
    echo "the API stopped without an error"
    return 1
  }
  grep -q 'Storage__AllowedExtensions=pdf,png, not a list by index' "$log" || {
    tail -n 20 "$log"
    return 1
  }
  if curl -s -o /dev/null "$API_URL/health"; then
    echo "something answers on $API_URL"
    return 1
  fi
}

check_comments_follows_the_new_form() {
  lacks 'Storage__AllowedExtensions__' scripts/check-comments.sh &&
    in_file 'start_api Production Storage__AllowedExtensions=exe,txt' scripts/check-comments.sh &&
    in_file 'console.log(settings.Storage.AllowedExtensions);' scripts/check-comments.sh
}

task_4() {
  echo "16.4 Storage:AllowedExtensions is one value with commas, and an override replaces the whole list"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the tests of the options pass: one value, the whole list replaced, none or by index refused" storage_options_tests_pass
  check "the API reads the default list from appsettings.json" the_default_list_is_read_from_appsettings
  check "appsettings.json holds the spec's list as one value with commas" appsettings_holds_one_value_with_commas
  check "compose says how to override the whole list, as docs/SPEC.md §13.3 writes it" compose_says_how_to_override_the_whole_list
  check "the real API on the default list takes png and refuses exe" the_default_list_holds_on_the_real_api
  check "compose's Storage__AllowedExtensions=pdf,png replaces the whole list" compose_override_replaces_the_whole_list
  check "another value replaces it too, with or without the dot, in any case" another_list_replaces_it_too
  check "a list by index stops the API at its start, saying why" a_list_by_index_refuses_to_start
  check "the API starts again on the default list" api_is_up
  check "check-comments.sh 2 overrides the list in the new form" check_comments_follows_the_new_form
  check "backend/CLAUDE.md says how the list is set" in_file '| `Storage__AllowedExtensions` ' backend/CLAUDE.md
}

# ---- 16.5: the group's flows end to end.

flow_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    TaskHistoryFlowTests.EveryChange_ThroughItsEndpoint_WritesItsEntry_AndTheHistoryGivesThemFromTheNewest \
    TaskHistoryFlowTests.AValueThatStays_AReorderInTheColumn_AComment_AndAFile_WriteNothing \
    TaskHistoryFlowTests.AnArchivedTasksHistory_Is404WithoutTasksArchive_EvenToWhoeverSeesEveryProject \
    TaskHistoryFlowTests.AnAssigneeTakenOutOfTheProject_AndADeletedColumn_KeepTheirPlaceInIt \
    TaskHistoryFlowTests.FiftyOneChanges_ComeFiftyToTheFirstPage_FromTheNewest_AndTheFirstChangeOnTheSecond \
    TaskHistoryFlowTests.TheListOfExtensionsComposeGives_ReplacesTheWholeDefaultList
}

microsecond_tests_pass() {
  named_tests_pass Kanabol.Domain.Tests \
    TaskItemTests.Archive_AtAMomentFinerThanAMicrosecond_WritesItToTheMicrosecond \
    TaskItemTests.SetDueDate_AtAMomentFinerThanAMicrosecond_WritesItToTheMicrosecond
}

entries_in_db() { # entries_in_db <task id>: how many rows its history has
  in_db "SELECT count(*) FROM task_history_entries WHERE task_id = '$1'"
}

# Each method of TaskItem that changes it, through its endpoint on the real API, as the admin, who holds
# tasks.archive: one entry each, from the newest, and the restore undoes the very moment archived.
every_change_on_the_real_api_writes_its_entry() {
  a_new_task 'Fiecare schimbare' || return 1
  task=$new_task
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$task/title" '{"title":"Fiecare schimbare, redenumit"}')" || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$task/description" '{"description":"Cu **Azure AD**"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$task/priority" '{"priority":"Critical"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$task/due-date" '{"dueDate":"2027-01-10T21:59:59.999Z"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$task/assignees" "{\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task/position" "{\"columnId\":\"$second_column\"}")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$task")" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$task/restore")" || return 1
  status_is 200 "$(history_as "$admin_token" "$task")" || return 1
  is 'ArchivedAt|ArchivedAt|ColumnId|Assignees|DueDate|Priority|Description|Title' "$(json "b.items.map(e => e.field).join('|')")" || return 1
  is "8|8" "$(json "b.totalCount")|$(entries_in_db "$task")" || return 1
  is "$first_column>$second_column|>$dan_id|>2027-01-10T21:59:59.9990000+00:00|Medium>Critical|>|Fiecare schimbare>Fiecare schimbare, redenumit" \
    "$(json "b.items.slice(2).map(e => (e.oldValue ?? '') + '>' + (e.newValue ?? '')).join('|')")" || return 1
  is "true:true" "$(json "[b.items[0].oldValue === b.items[1].newValue, b.items[0].newValue === null && b.items[1].oldValue === null].join(':')")" || return 1
  is "Dan Ionescu:Dan Ionescu" "$(json "[b.items[7].changedBy.displayName, b.items[3].names['$dan_id']].join(':')")"
}

# A title that stays, a comment and a file change nothing in the task, and write nothing.
nothing_else_writes_an_entry() {
  a_new_task 'Nimic de scris' || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$new_task/title" '{"title":"Nimic de scris"}')" || return 1
  status_is 201 "$(as "$dan_token" POST "/api/tasks/$new_task/comments" '{"body":"Un comentariu"}')" || return 1
  status_is 201 "$(upload "$dan_token" "$new_task" brief.pdf)" || return 1
  status_is 200 "$(history_as "$dan_token" "$new_task")" || return 1
  is "0|0" "$(json "b.totalCount")|$(entries_in_db "$new_task")"
}

task_5() {
  echo "16.5 The group's flows: every change written, nothing else, archived tasks, the names, the pages, the list of compose"
  check "Docker is running" docker_is_running
  check "the flows are tracked" tracked backend/tests/Kanabol.Api.Tests/Tasks/TaskHistoryFlowTests.cs
  check "the flows pass: every change, nothing else, 404 when archived, the names, 50 a page, compose's list" flow_tests_pass
  check "every test of the history in the API passes" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~TaskHistory' 14
  check "the history writes dates to the microsecond, as PostgreSQL keeps them" microsecond_tests_pass
  check "every change on the real API writes its entry, the restore the moment archived" every_change_on_the_real_api_writes_its_entry
  check "a title that stays, a comment and a file write nothing" nothing_else_writes_an_entry
}

# ---- 16b.6: a description's texts on demand, and their diff.

entry_as() { # entry_as <token> <task id> <entry id>: prints the status, the entry in the last answer
  as "$1" GET "/api/tasks/$2/history/$3"
}

entry_app=backend/src/Kanabol.Application/Features/Tasks/Queries/GetTaskHistoryEntry

entry_handler_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    TaskHistoryQueryHandlerTests.Get_AChangeOfTheDescription_ComesWithoutItsTexts_AndTheOtherFieldsWithTheirs \
    TaskHistoryQueryHandlerTests.GetEntry_AChangeOfTheDescription_GivesItWhole_WithBothTexts \
    TaskHistoryQueryHandlerTests.GetEntry_OfAnotherField_GivesItAsTheListDoes_WithTheNames \
    TaskHistoryQueryHandlerTests.GetEntry_OfAnotherTask_ReturnsHistoryNotFound \
    TaskHistoryQueryHandlerTests.GetEntry_ThatDoesntExist_ReturnsHistoryNotFound \
    TaskHistoryQueryHandlerTests.GetEntry_ByAnOutsider_ReturnsMemberRequired \
    TaskHistoryQueryHandlerTests.GetEntry_OfAnArchivedTask_IsTaskNotFound_ButForTasksArchive
}

entry_endpoint_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    TaskHistoryEndpointTests.Get_AChangeOfTheDescription_ComesWithNullTexts \
    TaskHistoryEndpointTests.GetEntry_Returns200WithTheEntryAndItsTexts \
    TaskHistoryEndpointTests.GetEntry_OfAnotherTasksEntry_Returns404HistoryNotFound \
    TaskHistoryEndpointTests.GetEntry_OfAnArchivedTask_Returns404TaskNotFound_ButToTasksArchive \
    TaskHistoryEndpointTests.GetEntry_ForSomeoneOutsideTheProject_Returns403MemberRequired \
    TaskHistoryEndpointTests.GetEntry_WithoutASession_Returns401
}

diff_flow_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    TaskHistoryFlowTests.ADescriptionsTexts_StayOutOfThePages_AndComeWholeWithTheirEntry \
    TaskHistoryFlowTests.AnEntry_IsForWhoeverSeesTheTask_AndOnlyUnderItsOwnTask
}

# Dan writes a description, the admin rewrites it, then renames the task: the list gives the two
# description entries with both values null, and the database still holds every text.
the_list_leaves_the_descriptions_texts_out() {
  a_new_task 'Descriere' || return 1
  described_task=$new_task
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$described_task/description" '{"description":"Autentificare prin **Azure AD**.\n\nPași:\n\n- login\n- logout"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$described_task/description" '{"description":"Autentificare prin **Entra ID**.\n\nPași:\n\n- login\n- refresh\n- logout"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$described_task/title" '{"title":"Descriere, redenumit"}')" || return 1
  status_is 200 "$(history_as "$ioana_token" "$described_task")" || return 1
  is 'Title:Descriere:Descriere, redenumit|Description:null:null|Description:null:null' \
    "$(json "b.items.map(e => [e.field, e.oldValue, e.newValue].map(String).join(':')).join('|')")" || return 1
  if grep -q 'Azure\|Entra' "$scratch/body"; then
    echo "the page holds a description's text"
    return 1
  fi
  described_entry=$(json "b.items[1].id")
  first_entry=$(json "b.items[2].id")
  is 2 "$(in_db "SELECT count(*) FROM task_history_entries WHERE task_id = '$described_task' AND field = 'Description' AND new_value LIKE 'Autentificare prin **%'")"
}

# The entry gives both texts as they were saved, lines and diacritics kept, with who changed it.
the_entry_gives_both_texts() {
  [ -n "${described_entry:-}" ] || the_list_leaves_the_descriptions_texts_out || return 1
  status_is 200 "$(entry_as "$ioana_token" "$described_task" "$described_entry")" || return 1
  is "Description|$described_entry|$admin_id|0" "$(json "[b.field, b.id, b.changedBy.userId, Object.keys(b.names).length].join('|')")" || return 1
  is 'Autentificare prin **Azure AD**.//Pași://- login/- logout' "$(json "b.oldValue.split('\n').join('/')")" || return 1
  is 'Autentificare prin **Entra ID**.//Pași://- login/- refresh/- logout' "$(json "b.newValue.split('\n').join('/')")" || return 1
  status_is 200 "$(entry_as "$dan_token" "$described_task" "$first_entry")" || return 1
  is "null|$dan_id" "$(json "[String(b.oldValue), b.changedBy.userId].join('|')")"
}

# Another task's entry, under this task, is 404 History.NotFound, as is one that doesn't exist.
another_tasks_entry_is_404() {
  [ -n "${described_entry:-}" ] || the_list_leaves_the_descriptions_texts_out || return 1
  a_new_task 'Alt task cu descriere' || return 1
  status_is 200 "$(as "$dan_token" PUT "/api/tasks/$new_task/description" '{"description":"Al altui task"}')" || return 1
  status_is 200 "$(history_as "$dan_token" "$new_task")" || return 1
  others=$(json "b.items[0].id")
  code_is 404 History.NotFound "$(entry_as "$dan_token" "$described_task" "$others")" || return 1
  code_is 404 History.NotFound "$(entry_as "$dan_token" "$described_task" 0193a000-0000-7000-8000-000000000000)" || return 1
  status_is 200 "$(entry_as "$dan_token" "$new_task" "$others")" || return 1
  is 'Al altui task' "$(json "b.newValue")"
}

an_outsider_reads_no_entry() {
  [ -n "${described_entry:-}" ] || the_list_leaves_the_descriptions_texts_out || return 1
  code_is 403 Project.MemberRequired "$(entry_as "$eva_token" "$described_task" "$described_entry")"
}

# Archived by the admin: Dan gets 404 Task.NotFound, the admin, with tasks.archive, the texts.
an_archived_tasks_entry_is_only_for_tasks_archive() {
  [ -n "${described_entry:-}" ] || the_list_leaves_the_descriptions_texts_out || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$described_task")" || return 1
  code_is 404 Task.NotFound "$(entry_as "$dan_token" "$described_task" "$described_entry")" || return 1
  status_is 200 "$(entry_as "$admin_token" "$described_task" "$described_entry")" || return 1
  is true "$(json "b.newValue.startsWith('Autentificare prin **Entra ID**')")"
}

diff_specs_name_the_rules() {
  spec_names "$tasks_web/description-diff.spec.ts" \
    'marks a word taken out as deleted, and the words around it as they are' \
    'marks a word put in as added' \
    'shows a word replaced as the old one deleted, then the new one added' \
    'keeps the lines of the markdown source, a new line among them' \
    'shows a first description as added whole, and one taken away as deleted whole' \
    'keeps markdown and HTML in the texts as plain text' \
    'deletes the old text whole and adds the new one whole when comparing them would take too long' &&
    spec_names "$tasks_web/task-timeline.spec.ts" \
      'offers the difference only on a change of the description' \
      'opens the difference under its row with "Vezi diferența", reading the entry with its texts' \
      'hides the difference with "Ascunde", and opens it again without reading it again' \
      'shows a skeleton of two rows while it reads the entry' \
      'says it could not read the difference, and reads it again on Reîncearcă' \
      'keeps an open difference open when it reads the history again after a save' &&
    spec_names "$tasks_web/tasks-api.spec.ts" \
      'reads one entry of a task’s history, with its texts, saying itself that it is not there'
}

# docs/SPEC.md §2: jsdiff, as the package diff, 9 here, which the lock holds.
diff_is_a_dependency() {
  node -e "
    const fs = require('fs');
    const { dependencies } = JSON.parse(fs.readFileSync('frontend/package.json', 'utf8'));
    const lock = JSON.parse(fs.readFileSync('frontend/package-lock.json', 'utf8'));
    const locked = lock.packages['node_modules/diff'];
    if (!/^\^9\./.test(dependencies.diff ?? '') || !locked || !locked.version.startsWith('9.')) {
      console.log('diff: ' + dependencies.diff + ', locked ' + (locked && locked.version));
      process.exit(1);
    }"
}

# Only the diff's component imports it, so nothing else pulls it into another chunk.
only_the_diff_imports_it() {
  is "$tasks_web/description-diff.ts" "$(grep -rlE "from 'diff'|import\('diff'\)|require\('diff'\)" "$app" | grep -v '\.spec\.ts$' | sort | tr '\n' ' ' | sed 's/ $//')" &&
    in_file 'diffWordsWithSpace(oldText, newText, limit)' "$tasks_web/description-diff.ts"
}

# docs/UI_UX_SPEC.md §7.5 "Diferența descrierii": its words, the block of at most 240px, the colors.
the_diff_follows_the_ui_spec() {
  for words in 'Vezi diferența' 'Ascunde' 'Nu am putut încărca diferența.' 'Reîncearcă' '<kb-description-diff [before]="diff.before" [after]="diff.after" />'; do
    in_file "$words" "$tasks_web/task-timeline.ts" || {
      echo "no '$words' in task-timeline.ts"
      return 1
    }
  done
  in_file 'box-sizing: border-box;' "$tasks_web/task-timeline.ts" &&
    in_file 'max-height: 240px;' "$tasks_web/task-timeline.ts" &&
    in_file 'overflow: auto;' "$tasks_web/task-timeline.ts" &&
    in_file "store.toggleDiff(line.id)" "$tasks_web/task-timeline.ts" &&
    in_file "this.api.historyEntry(taskId, entryId)" "$tasks_web/task-timeline-store.ts" &&
    in_file 'font-size: 13px;' "$tasks_web/description-diff.ts" &&
    in_file 'white-space: pre-wrap;' "$tasks_web/description-diff.ts" &&
    in_file 'background: var(--kb-danger-bg);' "$tasks_web/description-diff.ts" &&
    in_file 'text-decoration: line-through;' "$tasks_web/description-diff.ts" &&
    in_file 'background: var(--kb-success-bg);' "$tasks_web/description-diff.ts" &&
    lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-|innerHTML|bypassSecurityTrust' \
      "$tasks_web/description-diff.ts" "$tasks_web/task-timeline.ts" "$tasks_web/task-timeline-store.ts"
}

# The initial bundle is what index.html loads: its scripts and the chunks it preloads.
initial_files() {
  grep -oE '<script src="[^"]*"|<link rel="modulepreload" href="[^"]*"' "$scratch/dist/browser/index.html" |
    sed 's/.*"\([^"]*\)"$/\1/'
}

# jsdiff's own option names, which the minifier keeps.
jsdiff_marker='maxEditLength|oneChangePerToken|useLongestToken'

# docs/SPEC.md §2: jsdiff only in the tasks' chunk, the task panel's, which holds the timeline; none of
# the initial files holds it or the diff's component.
jsdiff_comes_with_the_panel_only() {
  built || return 1
  initial=$(initial_files)
  [ -n "$initial" ] || {
    echo "index.html loads nothing"
    return 1
  }
  for file in $initial; do
    if grep -qE "$jsdiff_marker|kb-description-diff" "$scratch/dist/browser/$file"; then
      echo "$file, in the initial bundle, holds jsdiff or the diff's component"
      return 1
    fi
  done
  holders=$(grep -lE "$jsdiff_marker" "$scratch"/dist/browser/*.js | sed 's|.*/||' | tr '\n' ' ' | sed 's/ $//')
  [ -n "$holders" ] && [ "$(printf '%s\n' $holders | wc -l)" -eq 1 ] || {
    echo "jsdiff is in: ${holders:-no file}"
    return 1
  }
  grep -q 'kb-task-timeline' "$scratch/dist/browser/$holders" && grep -q 'kb-description-diff' "$scratch/dist/browser/$holders" || {
    echo "$holders, which holds jsdiff, isn't the task panel's chunk"
    return 1
  }
}

task_6() {
  echo "16b.6 A description's texts on demand, and their diff in the timeline"
  check "Docker is running" docker_is_running
  check "the entry's query is tracked" tracked "$entry_app/GetTaskHistoryEntryQuery.cs"
  check "its handler is tracked" tracked "$entry_app/GetTaskHistoryEntryQueryHandler.cs"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the handler's tests pass: the list without the texts, the entry with them, another task's, who sees it" entry_handler_tests_pass
  check "the endpoint's tests pass" entry_endpoint_tests_pass
  check "the flows pass: the texts out of the pages, whole in their entry, for whoever sees the task" diff_flow_tests_pass
  check "the list leaves a description's texts out, and the database keeps them" the_list_leaves_the_descriptions_texts_out
  check "the entry gives both texts as saved, lines and diacritics kept" the_entry_gives_both_texts
  check "another task's entry is 404 History.NotFound" another_tasks_entry_is_404
  check "an outsider reads no entry, 403 Project.MemberRequired" an_outsider_reads_no_entry
  check "an archived task's entry is 404 Task.NotFound, but for tasks.archive" an_archived_tasks_entry_is_only_for_tasks_archive
  check "backend/CLAUDE.md says how the entry works" in_file '`GET /api/tasks/{id}/history/{entryId}`' backend/CLAUDE.md
  for file in description-diff.ts description-diff.spec.ts; do
    check "features/tasks/$file is tracked" tracked "$tasks_web/$file"
  done
  check "the specs name the rules: a word deleted, one added, the lines kept, the link, the skeleton, the error" diff_specs_name_the_rules
  check "the specs of the diff, the timeline and the API pass" specs_pass 'src/app/features/tasks/{description-diff,task-timeline,tasks-api}.spec.ts' 27
  check "every spec of the tasks feature passes" specs_pass 'src/app/features/tasks/*.spec.ts' 127
  check "diff 9 is a dependency, in the lock" diff_is_a_dependency
  check "only the diff's component imports it, with diffWordsWithSpace" only_the_diff_imports_it
  check "the diff follows the UI spec: its words, at most 240px, the colors, nothing unsafe" the_diff_follows_the_ui_spec
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "jsdiff comes only in the task panel's chunk, never in the initial bundle" jsdiff_comes_with_the_panel_only
  check "frontend/CLAUDE.md says how it works" in_file '`kb-description-diff`' frontend/CLAUDE.md
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 5 6
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
