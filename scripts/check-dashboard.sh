#!/bin/sh
# Acceptance checks for the main panel and "Task-urile mele" (group 17a in the project's Tasks.md;
# docs/SPEC.md §4.4 "Panoul principal și task-urile mele", §6.1, §14.7; docs/UI_UX_SPEC.md §7.2). The tests
# and specs hold the rules, so these checks run them, and add what they can't see: the real API process on
# a real PostgreSQL and its answers, and the frontend's production build.
#
# Usage: scripts/check-dashboard.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  GET /api/me/summary and GET /api/me/tasks: the tests, then the real API in Production on tasks in
#      several projects, one archived, one finished, one overdue, one in an archived project and one
#      someone else's: the four numbers, weekStart missing, malformed or in the future answering 400 on
#      it, the caller's own tasks by deadline with their project, open=true, the filters, includeArchived
#      without tasks.archive refused, nothing for projects.view_all or an outsider. Docker, PostgreSQL.
#   2  GET /api/projects with mine=true and completedTaskCount: the tests, then the real API in Production
#      on the same projects: only those the caller is a member of with mine=true, also for projects.view_all,
#      archived ones only with includeArchived, the finished tasks counted without the archived one, mine
#      not a boolean answering 400 on it. Docker, PostgreSQL.
#   3  the main panel and "Task-urile mele": specs, lint, formatting, the production build without warnings
#      but the budget's, the texts of docs/UI_UX_SPEC.md §7.2, no other feature imported by the panel, the
#      List's table and filter bar on /my-tasks with Proiect, no assignee and "Și cele terminate", and none
#      of either page in the initial bundle, which index.html loads. Only the packages that scripts/setup.sh
#      installs.
#   4  the group's flows end to end: the API's tests (a task finished, archived, restored and reopened, a
#      deadline moved, assignees changed, a project archived and restored, a member taken out, nothing
#      for projects.view_all), then the same changes on the real API in Production, each changing Dan's
#      four numbers, his own tasks and his project cards as the rules say. Docker, PostgreSQL.
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database and
# drops it. Docker runs the tests' own PostgreSQL.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_dashboard_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5293
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

# The checks' deadlines: one long past, one far away.
past=2026-01-15T12:00:00Z
future=2099-01-15T12:00:00Z

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

# Starts the API on this run's database, with any other settings given as NAME=value, its output in the
# log given, and leaves its process id in started_pid. It doesn't wait.
launch_api() { # launch_api <log> <environment> [NAME=value...]
  log=$1
  environment=$2
  shift 2
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
  ) >"$log" 2>&1 &
  started_pid=$!
}

# The API in Production unless asked otherwise, with any other settings given as NAME=value. A start in
# another environment, or with settings, stops the one running first; the next plain start goes back.
start_api() { # start_api <environment> [NAME=value...]
  wanted="$*"
  [ -n "$api_pid" ] && [ "$api_environment" = "$wanted" ] && return 0
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  launch_api "$api_log" "$@"
  api_pid=$started_pid
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

# Without the overdue check in the background, which would add notifications of its own.
api_is_up() {
  start_api Production Alerts__OverdueCheckEnabled=false
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
  printf '10.84.%s.%s' "$((used / 256))" "$((used % 256))"
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

# The first admin past the forced password change. The API may have stopped since the token was taken:
# it comes back first, and the token, signed with the same secret, still holds.
admin_is_ready() {
  [ -n "${api_pid:-}" ] || api_is_up || return 1
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


# ---- The run's people and tasks, made through the real API.

# A task of the admin's in a project, its id in new_task.
a_task_in() { # a_task_in <project id> <title> <JSON fields to add>
  status_is 201 "$(as "$admin_token" POST "/api/projects/$1/tasks" "{\"title\":\"$2\",$3}")" || return 1
  new_task=$(body_field id)
}

# Vera sees every project through projects.view_all, and is a member of none.
viewer_is_ready() {
  [ -n "${vera_token:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Vede tot","permissions":["projects.view_all"]}')" || return 1
  viewer_role=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"vera@example.com\",\"displayName\":\"Vera Vede\",\"roleId\":\"$viewer_role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  status_is 200 "$(login vera@example.com "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$member_password\"}")" || return 1
  vera_token=$(body_field accessToken)
}

# PAN and MEU, owned by the admin, with Dan as a member; ARH, with Dan too, archived once its task is in.
# PAN-1 Dan's, High, overdue; PAN-2 Dan's, Low, due later; PAN-3 Dan's, archived; PAN-4 Dan's, finished
# now; PAN-5 the admin's, overdue; PAN-6 the admin's, finished, then archived. MEU-1 Dan's and the admin's, Critical, without a deadline. ARH-1 Dan's,
# overdue, in the archived project. Eva is in no project, and Vera sees them all.
team_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready eva eva@example.com 'Eva Din Afara' || return 1
  viewer_is_ready || return 1
  [ -n "${pan_id:-}" ] && return 0
  for project in 'Panou:PAN' 'Meu:MEU' 'Arhiva:ARH'; do
    status_is 201 "$(as "$admin_token" POST /api/projects "{\"name\":\"${project%%:*}\",\"key\":\"${project#*:}\"}")" || return 1
    id=$(body_field id)
    status_is 201 "$(as "$admin_token" POST "/api/projects/$id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
    eval "$(printf '%s' "${project#*:}" | tr 'A-Z' 'a-z')_id=$id"
  done
  a_task_in "$pan_id" 'Login SSO' "\"priority\":\"High\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$past\"" &&
    a_task_in "$pan_id" 'Pagina de start' "\"priority\":\"Low\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$future\"" &&
    a_task_in "$pan_id" 'Arhivat' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$past\"" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$new_task")" || return 1
  a_task_in "$pan_id" 'Logo nou' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  pan_done=$(in_db "SELECT id FROM board_columns WHERE project_id = '$pan_id' AND kind = 'Done'")
  status_is 200 "$(as "$dan_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$pan_done\"}")" || return 1
  a_task_in "$pan_id" 'Al adminului' "\"priority\":\"Medium\",\"assigneeIds\":[\"$admin_id\"],\"dueDate\":\"$past\"" &&
    a_task_in "$pan_id" 'Gata si arhivat' "\"priority\":\"Medium\",\"assigneeIds\":[\"$admin_id\"]" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$pan_done\"}")" &&
    status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$new_task")" &&
    a_task_in "$meu_id" 'Formular de contact' "\"priority\":\"Critical\",\"assigneeIds\":[\"$dan_id\",\"$admin_id\"]" &&
    a_task_in "$arh_id" 'Din arhiva' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$past\"" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$arh_id")"
}

# The Monday of this week at midnight in UTC, as a browser in UTC sends it, and the same Monday at
# midnight in a browser two hours ahead, with its offset.
monday=$(date -u -d "$(date -u +%F) -$(($(date -u +%u) - 1)) days" +%F)
week_start="${monday}T00:00:00Z"
week_start_ahead="${monday}T00:00:00%2B02:00"

summary_of() { # summary_of <token> <query>: openTasks overdueTasks completedThisWeek activeProjects
  status_is 200 "$(as "$1" GET "/api/me/summary?$2")" || return 1
  json "[b.openTasks, b.overdueTasks, b.completedThisWeek, b.activeProjects].join(' ')"
}

my_task_keys() { # my_task_keys <token> <query>: the keys of the page, in order
  status_is 200 "$(as "$1" GET "/api/me/tasks?$2")" || return 1
  json "b.items.map(t => t.key).join(' ')"
}

field_is_refused() { # field_is_refused <field> <status of the request just sent>
  code_is 400 Validation "$2" || return 1
  is true "$(json "Object.keys(b.errors).includes('$1')")"
}

# ---- 17a.1: GET /api/me/summary and GET /api/me/tasks.

summary_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetMySummaryQueryHandlerTests' 10
}

my_tasks_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetMyTasksQueryHandlerTests' 26
}

# The project's list now reads its rows through the same TaskListRows.
project_list_tests_still_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetProjectTasksQueryHandlerTests' 39
}

endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~MeEndpointTests' 17 &&
    named_tests_pass Kanabol.Api.Tests EndpointPermissionsTests.Me_NeedsNoPermissionOnTheEndpoint
}

the_numbers_are_dans_own() {
  team_is_ready || return 1
  is '3 1 1 2' "$(summary_of "$dan_token" "weekStart=$week_start")"
}

# The admin holds projects.view_all, and counts only the projects they are a member of, ARH archived.
the_owner_counts_their_own() {
  team_is_ready || return 1
  is '2 1 0 2' "$(summary_of "$admin_token" "weekStart=$week_start")"
}

# A week's start with an offset once answered 500: Npgsql writes only UTC.
a_week_start_with_an_offset_is_taken() {
  team_is_ready || return 1
  is '3 1 1 2' "$(summary_of "$dan_token" "weekStart=$week_start_ahead")"
}

week_start_is_required_and_past() {
  team_is_ready || return 1
  field_is_refused weekStart "$(as "$dan_token" GET /api/me/summary)" &&
    field_is_refused weekStart "$(as "$dan_token" GET '/api/me/summary?weekStart=luni')" &&
    field_is_refused weekStart "$(as "$dan_token" GET "/api/me/summary?weekStart=$future")"
}

nobody_else_counts_for_the_viewer_or_an_outsider() {
  team_is_ready || return 1
  is '0 0 0 0' "$(summary_of "$vera_token" "weekStart=$week_start")" &&
    is '0 0 0 0' "$(summary_of "$eva_token" "weekStart=$week_start")"
}

# By deadline from the soonest, those without one last, then by key from the newest: PAN-4 before MEU-1.
my_tasks_go_by_deadline() {
  team_is_ready || return 1
  is 'PAN-1 PAN-2 PAN-4 MEU-1' "$(my_task_keys "$dan_token" '')" &&
    is '1 50 4' "$(json "[b.page, b.pageSize, b.totalCount].join(' ')")"
}

each_row_has_its_project() {
  team_is_ready || return 1
  my_task_keys "$dan_token" 'search=formular' >/dev/null || return 1
  is "$meu_id MEU Meu Critical Backlog" "$(json "[b.items[0].projectId, b.items[0].projectKey, b.items[0].projectName, b.items[0].priority, b.items[0].columnName].join(' ')")"
}

the_filters_hold_across_projects() {
  team_is_ready || return 1
  is 'PAN-1 PAN-2 MEU-1' "$(my_task_keys "$dan_token" 'open=true&pageSize=8')" &&
    is 'PAN-1 MEU-1' "$(my_task_keys "$dan_token" 'priority=Critical&priority=High')" &&
    is 'PAN-1' "$(my_task_keys "$dan_token" 'overdue=true')" &&
    is 'PAN-4' "$(my_task_keys "$dan_token" 'search=logo')" &&
    is 'MEU-1 PAN-1 PAN-2 PAN-4' "$(my_task_keys "$dan_token" 'sort=key:asc')" &&
    is 'MEU-1' "$(my_task_keys "$dan_token" 'page=2&pageSize=2&open=true')"
}

archived_ones_need_tasks_archive() {
  team_is_ready || return 1
  code_is 403 Auth.PermissionRequired "$(as "$dan_token" GET '/api/me/tasks?includeArchived=true')"
}

wrong_queries_are_refused() {
  team_is_ready || return 1
  field_is_refused open "$(as "$dan_token" GET '/api/me/tasks?open=maybe')" &&
    field_is_refused sort "$(as "$dan_token" GET '/api/me/tasks?sort=deadline:asc')" &&
    field_is_refused pageSize "$(as "$dan_token" GET '/api/me/tasks?pageSize=101')"
}

nobody_else_has_tasks_for_the_viewer_or_an_outsider() {
  team_is_ready || return 1
  is '' "$(my_task_keys "$vera_token" '')" &&
    is '' "$(my_task_keys "$eva_token" '')"
}

task_1() {
  echo "17a.1 GET /api/me/summary and GET /api/me/tasks"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the summary's tests pass: several projects, archived, finished, overdue, weekStart, view_all" summary_tests_pass
  check "the tests of the caller's own tasks pass: filters, open, sorts, project, view_all" my_tasks_tests_pass
  check "the project's list, on the same rows, still passes its tests" project_list_tests_still_pass
  check "the endpoints' tests pass, with no permission on them" endpoint_tests_pass
  check "the real API in Production counts Dan's own: 3 open, 1 overdue, 1 finished this week, 2 projects" the_numbers_are_dans_own
  check "the admin, with projects.view_all, counts only their own tasks and projects" the_owner_counts_their_own
  check "a week's start with an offset is taken" a_week_start_with_an_offset_is_taken
  check "weekStart missing, malformed or in the future answers 400 on it" week_start_is_required_and_past
  check "the viewer with projects.view_all and an outsider count nothing" nobody_else_counts_for_the_viewer_or_an_outsider
  check "Dan's own tasks come by deadline, without one last, then by key from the newest" my_tasks_go_by_deadline
  check "each row has its project's id, key and name" each_row_has_its_project
  check "open, priority, overdue, search, sort and pages hold across projects" the_filters_hold_across_projects
  check "includeArchived without tasks.archive answers 403 Auth.PermissionRequired" archived_ones_need_tasks_archive
  check "open, sort and pageSize out of their values answer 400 on them" wrong_queries_are_refused
  check "the viewer and an outsider have no tasks of their own" nobody_else_has_tasks_for_the_viewer_or_an_outsider
  check "backend/CLAUDE.md lists both endpoints" in_file '`GET /api/me/summary?weekStart=' backend/CLAUDE.md
}

# ---- 17a.2: GET /api/projects with mine=true and completedTaskCount.

project_list_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetProjectsQueryHandlerTests' 22
}

project_list_endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ProjectListEndpointTests' 8
}

project_counts() { # project_counts <token> <query>: key:open:finished of each project, by name
  status_is 200 "$(as "$1" GET "/api/projects?$2")" || return 1
  json "b.items.map(p => p.key + ':' + p.openTaskCount + ':' + p.completedTaskCount).join(' ')"
}

# PAN: PAN-1, PAN-2 and PAN-5 open, PAN-4 finished, PAN-3 archived open and PAN-6 archived finished.
the_finished_tasks_are_counted_without_the_archived() {
  team_is_ready || return 1
  is 'MEU:1:0 PAN:3:1' "$(project_counts "$dan_token" 'mine=true')"
}

mine_leaves_out_what_view_all_shows() {
  team_is_ready || return 1
  is 'MEU:1:0 PAN:3:1' "$(project_counts "$vera_token" '')" &&
    is '' "$(project_counts "$vera_token" 'mine=true')" &&
    is 'MEU:1:0 PAN:3:1' "$(project_counts "$admin_token" 'mine=true')"
}

mine_takes_archived_ones_only_when_asked() {
  team_is_ready || return 1
  is 'ARH:1:0 MEU:1:0 PAN:3:1' "$(project_counts "$dan_token" 'mine=true&includeArchived=true')"
}

an_outsider_has_no_projects_of_their_own() {
  team_is_ready || return 1
  is '' "$(project_counts "$eva_token" 'mine=true')"
}

mine_has_to_be_a_boolean() {
  team_is_ready || return 1
  field_is_refused mine "$(as "$dan_token" GET '/api/projects?mine=maybe')"
}

task_2() {
  echo "17a.2 GET /api/projects with mine=true and completedTaskCount"
  check "Docker is running" docker_is_running
  check "the project list's tests pass: mine, also for view_all and manage, archived, the finished count" project_list_tests_pass
  check "the endpoint's tests pass" project_list_endpoint_tests_pass
  check "the real API in Production counts the finished tasks, not the archived one" the_finished_tasks_are_counted_without_the_archived
  check "mine=true leaves out what projects.view_all shows" mine_leaves_out_what_view_all_shows
  check "mine=true gives archived projects only with includeArchived" mine_takes_archived_ones_only_when_asked
  check "an outsider has no projects with mine=true" an_outsider_has_no_projects_of_their_own
  check "mine that isn't a boolean answers 400 on it" mine_has_to_be_a_boolean
  check "backend/CLAUDE.md says what mine and completedTaskCount do" in_file '`mine=true`' backend/CLAUDE.md
}

# ---- 17a.3: the main panel and "Task-urile mele".

web=frontend
app=$web/src/app
dashboard_web=$app/features/dashboard

plain() { # plain <file>: without the terminal's colors
  sed 's/\x1b\[[0-9;]*m//g' "$1"
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

spec_names() { # spec_names <spec file> <name...>: each is there, so a filter can't run nothing
  file=$1
  shift
  for name in "$@"; do
    in_file "$name" "$file" || {
      echo "no spec '$name' in $file"
      return 1
    }
  done
}

built() {
  [ -f "$scratch/built" ] && return 0
  (cd "$web" && npx ng build --output-path "$(native "$scratch/dist")") >"$scratch/build.log" 2>&1 || {
    plain "$scratch/build.log" | tail -n 25
    return 1
  }
  touch "$scratch/built"
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

# The build's own warnings: a // comment in a component's inline CSS breaks the rule after it.
the_build_warns_only_of_the_budget() {
  built || return 1
  warnings=$(plain "$scratch/build.log" | grep -F 'WARNING' | grep -vF 'exceeded maximum budget')
  [ -z "$warnings" ] || {
    echo "$warnings"
    return 1
  }
}

the_panel_specs_name_the_rules() {
  spec_names "$dashboard_web/dashboard-page.spec.ts" \
    'is titled "Panou principal", without a breadcrumb' \
    "reads the summary from Monday at midnight in the person's time" \
    'shows the four tiles with their numbers and fixed captions' \
    'leads the tiles to /my-tasks, /my-tasks?overdue=1 and /projects, and "Finalizate" nowhere' \
    'lists the first 8 open tasks by deadline, with key, title, project, deadline and priority' \
    "opens a task's panel over the main panel from its row" \
    'says "Niciun task deschis." without open tasks' \
    'shows the last 5 notifications with their lines' \
    'marks a notification read, tells the bell and opens its task' \
    'says "Nicio notificare." without notifications' \
    'shows my projects as cards: name and key, owners, "3 din 12 gata" and the open tasks' \
    'says "Nu ești în niciun proiect încă." without projects' \
    'leads "Vezi toate" to /my-tasks, /notifications and /projects' \
    'shows a skeleton in each section while it reads' \
    'says each section could not be read, and tries that one again' \
    'reads the tiles and the tasks again after a save from the task panel, and nothing else' \
    'keeps the tasks it shows while it reads them again' &&
    spec_names "$dashboard_web/dashboard-api.spec.ts" \
      'reads the summary from the week start it is given' \
      'reads the first open tasks, as many as asked for' &&
    spec_names "$dashboard_web/week-start.spec.ts" \
      'goes back to Monday at midnight, where the person is' \
      'takes a Sunday back six days' &&
    spec_names "$app/shared/ui/stat-tile.spec.ts" \
      'is a link with its query when it leads somewhere, and a plain card when not' \
      'shows a skeleton in place of the number until it comes'
}

# docs/UI_UX_SPEC.md §7.2, word for word where it gives the words.
the_panel_texts_follow_the_ui_spec() {
  for words in 'label="Task-urile mele deschise"' 'label="Întârziate"' 'label="Finalizate săptămâna asta"' 'label="Proiecte active"' \
    'caption="asignate mie"' 'caption="după deadline"' 'caption="de luni încoace"' 'caption="în care sunt membru"' \
    '>Task-urile mele<' '>Notificări recente<' '>Proiectele mele<' 'routerLink="/my-tasks"' 'routerLink="/notifications"' 'routerLink="/projects"' \
    'din {{ total(project) }} gata' 'Reîncearcă'; do
    in_file "$words" "$dashboard_web/dashboard-page.html" || {
      echo "no '$words' in dashboard-page.html"
      return 1
    }
  done
  for words in 'Panou principal' 'Niciun task deschis.' 'Nicio notificare.' 'Nu ești în niciun proiect încă.' \
    'Nu am putut încărca sumarul.' 'Nu am putut încărca task-urile.' 'Nu am putut încărca notificările.' 'Nu am putut încărca proiectele.'; do
    in_file "$words" "$dashboard_web/dashboard-page.ts" || {
      echo "no '$words' in dashboard-page.ts"
      return 1
    }
  done
  in_file 'export const DASHBOARD_TASKS = 8;' "$dashboard_web/dashboard-store.ts" &&
    in_file 'export const DASHBOARD_NOTIFICATIONS = 5;' "$dashboard_web/dashboard-store.ts" &&
    in_file "set('open', true).set('pageSize', count)" "$dashboard_web/dashboard-api.ts" &&
    in_file "set('mine', true)" "$dashboard_web/dashboard-api.ts" &&
    in_file 'width: 280px;' "$dashboard_web/dashboard-page.scss" &&
    in_file 'height: 4px;' "$dashboard_web/dashboard-page.scss" &&
    in_file 'background: var(--kb-sky-300);' "$dashboard_web/dashboard-page.scss" &&
    in_file 'grid-template-columns: repeat(12, minmax(0, 1fr));' "$dashboard_web/dashboard-page.scss" &&
    in_file 'font-size: 28px;' "$app/shared/ui/stat-tile.ts" &&
    in_file 'color: var(--kb-navy-900);' "$app/shared/ui/stat-tile.ts" &&
    lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-|innerHTML|bypassSecurityTrust' \
      "$dashboard_web"/*.ts "$dashboard_web"/*.scss "$app/shared/ui/stat-tile.ts"
}

# docs/SPEC.md §6.1: a feature imports no other feature; what they share is in shared/ or core/.
the_panel_imports_no_other_feature() {
  lacks "from '\.\./(auth|users|roles|projects|board|tasks|notifications|reports)/" "$dashboard_web"/*.ts
}

the_panel_is_the_home_route() {
  in_file "import('./features/dashboard/dashboard.routes')" "$app/app.routes.ts" &&
    lacks "placeholder\(''" "$app/app.routes.ts"
}

# The initial bundle is what index.html loads: its scripts and the chunks it preloads.
initial_files() {
  grep -oE '<script src="[^"]*"|<link rel="modulepreload" href="[^"]*"' "$scratch/dist/browser/index.html" |
    sed 's/.*"\([^"]*\)"$/\1/'
}

# The pages' own pieces, which the minifier keeps: their selectors and their texts.
panel_markers='kb-dashboard-page|kb-stat-tile|Nu ești în niciun proiect încă|Nu am putut încărca sumarul|kb-my-tasks-page|kb-task-table|Niciun task pentru filtrele alese'

# Tasks.md group 17a: the initial bundle doesn't take the panel. The route adds a few bytes to main, and
# the bundler may split the shared chunks another way; none of the panel's code may land there.
the_panel_stays_out_of_the_initial_bundle() {
  built || return 1
  initial=$(initial_files)
  [ -n "$initial" ] || {
    echo "index.html loads nothing"
    return 1
  }
  for file in $initial; do
    if grep -qE "$panel_markers" "$scratch/dist/browser/$file"; then
      echo "$file, in the initial bundle, holds the panel: $(grep -oE "$panel_markers" "$scratch/dist/browser/$file" | sort -u | tr '\n' ' ')"
      return 1
    fi
  done
  holder=$(grep -lE 'kb-dashboard-page' "$scratch"/dist/browser/*.js | sed 's|.*/||')
  [ "$(printf '%s\n' $holder | wc -l)" -eq 1 ] || {
    echo "the panel is in: ${holder:-no file}"
    return 1
  }
  printf '%s\n' $initial | grep -qxF "$holder" && {
    echo "$holder is initial"
    return 1
  }
  initial_total=$(plain "$scratch/build.log" | sed -n 's/.*Initial total *| *\([0-9.]* kB\).*/\1/p')
  echo "initial total $initial_total, the panel in $holder" >&2
}

tasks_web=$app/features/tasks

the_my_tasks_specs_name_the_rules() {
  spec_names "$tasks_web/my-tasks-page.spec.ts" \
    'is titled "Task-urile mele", under Acasă' \
    'reads the first page of 50 of my open tasks, by deadline from the soonest' \
    'with a "Proiect" column, and no assignee select' \
    'takes the filters from the address: q, priority, overdue=1, done=1 and archived=1' \
    'opens on the overdue ones from /my-tasks?overdue=1' \
    'puts "Și cele terminate" in the address and back, from the first page' \
    'offers "Și cele arhivate" only to tasks.archive, and asks for none without it' \
    'sorts by a column through the address, and leaves the deadline out of it' \
    'pages through the address, 50 by default' \
    'opens a task in its panel, keeping the filters' \
    'reads the page again when the panel saves a change' \
    'says "Niciun task deschis." without filters' \
    'says "Niciun task pentru filtrele alese." with them, and clears them, keeping the finished ones' \
    'says it could not read the tasks, and tries again' &&
    spec_names "$app/shared/ui/task-filter-bar.spec.ts" \
      'leaves out Ale mele and the assignee select when the tasks are all the user' \
      'offers "Și cele terminate" only when given, and says when it is pressed' &&
    spec_names "$app/shared/models/task-filters.spec.ts" \
      'reads and writes the sort against another default when given one' &&
    spec_names "$tasks_web/tasks-api.spec.ts" \
      'with only the filters asked for' \
      'sends the sort only when it is not by deadline, and no open for the finished ones too'
}

# docs/UI_UX_SPEC.md §7.2, word for word where it gives the words.
the_my_tasks_texts_follow_the_ui_spec() {
  for words in 'Task-urile mele' 'Niciun task deschis.' 'Niciun task pentru filtrele alese.' 'Nu am putut încărca task-urile.' \
    "'project'" 'const DEFAULT_PAGE_SIZE = 50;' "params().get('done') === '1'" 'open: !this.finished()' 'assignee: null'; do
    in_file "$words" "$tasks_web/my-tasks-page.ts" || {
      echo "no '$words' in my-tasks-page.ts"
      return 1
    }
  done
  for words in '[withAssignee]="false"' '[finished]="finished()"' '<kb-task-table' 'Șterge filtrele' 'Reîncearcă'; do
    in_file "$words" "$tasks_web/my-tasks-page.html" || {
      echo "no '$words' in my-tasks-page.html"
      return 1
    }
  done
  in_file "export const MY_TASKS_SORT: TaskSort = { field: 'dueDate', descending: false };" "$tasks_web/tasks-api.ts" &&
    in_file "'/me/tasks'" "$tasks_web/tasks-api.ts" &&
    in_file '>Proiect<' "$tasks_web/task-table.html" &&
    in_file 'Și cele terminate' "$app/shared/ui/task-filter-bar.ts" &&
    in_file '<kb-task-table' "$tasks_web/task-list-page.html" &&
    lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-|innerHTML|bypassSecurityTrust' \
      "$tasks_web"/my-tasks-page.ts "$tasks_web"/my-tasks-store.ts "$tasks_web"/my-tasks-page.scss "$tasks_web"/task-table.*
}

my_tasks_is_its_route() {
  in_file "import('./features/tasks/my-tasks-page')" "$app/app.routes.ts" &&
    lacks "placeholder\('my-tasks'" "$app/app.routes.ts"
}

task_3() {
  echo "17a.3 The main panel and \"Task-urile mele\""
  for file in models.ts dashboard-api.ts dashboard-store.ts dashboard-page.ts dashboard-page.html dashboard-page.scss \
    dashboard.routes.ts week-start.ts dashboard-testing.ts dashboard-api.spec.ts dashboard-page.spec.ts week-start.spec.ts; do
    check "features/dashboard/$file is tracked" tracked "$dashboard_web/$file"
  done
  check "shared/ui/stat-tile.ts and its spec are tracked" tracked "$app/shared/ui/stat-tile.ts"
  check "the specs name the rules: tiles, links, sections, empty texts, errors, the reread" the_panel_specs_name_the_rules
  check "the panel's specs pass" specs_pass 'src/app/features/dashboard/*.spec.ts' 25
  check "kb-stat-tile's specs pass" specs_pass 'src/app/shared/ui/stat-tile.spec.ts' 3
  check "the texts, sizes and colors follow docs/UI_UX_SPEC.md §7.2, nothing unsafe" the_panel_texts_follow_the_ui_spec
  check "the panel imports no other feature" the_panel_imports_no_other_feature
  check "/ is the panel, not a placeholder" the_panel_is_the_home_route
  for file in my-tasks-page.ts my-tasks-page.html my-tasks-page.scss my-tasks-store.ts my-tasks-page.spec.ts     task-table.ts task-table.html task-table.scss; do
    check "features/tasks/$file is tracked" tracked "$tasks_web/$file"
  done
  check "the specs of Task-urile mele name the rules: the filters, done=1, the sort, the panel, the texts" the_my_tasks_specs_name_the_rules
  check "the specs of Task-urile mele, the List on the same table, and TasksApi pass" specs_pass 'src/app/features/tasks/{my-tasks-page,task-list-page,tasks-api}.spec.ts' 36
  check "the specs of the filter bar and the filters pass" specs_pass 'src/app/shared/{ui/task-filter-bar,models/task-filters}.spec.ts' 15
  check "Task-urile mele follow docs/UI_UX_SPEC.md §7.2: Proiect, no assignee, Și cele terminate, the texts" the_my_tasks_texts_follow_the_ui_spec
  check "/my-tasks is the page, not a placeholder" my_tasks_is_its_route
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "the build warns only of the budget" the_build_warns_only_of_the_budget
  check "none of the panel or of Task-urile mele is in the initial bundle, the panel has a chunk of its own" the_panel_stays_out_of_the_initial_bundle
  [ -f "$scratch/build.log" ] && printf '        %s
' "$(plain "$scratch/build.log" | grep -F 'Initial total' | tr -s ' ')"
  check "frontend/CLAUDE.md says how the panel works" in_file '`DashboardStore`' frontend/CLAUDE.md
  check "frontend/CLAUDE.md says how Task-urile mele work" in_file '`MyTasksPage`' frontend/CLAUDE.md
}

# ---- 17a.4: the group's flows end to end, in the tests and on the real API.

flow_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~DashboardFlowTests' 7
}

dans_numbers_are() { # dans_numbers_are <open overdue finished projects>
  is "$1" "$(summary_of "$dan_token" "weekStart=$week_start")"
}

# PAN-7, Dan's, past its deadline: the change every later check of this task follows.
a_live_task_is_ready() {
  team_is_ready || return 1
  [ -n "${live_task:-}" ] && return 0
  dans_numbers_are '3 1 1 2' || return 1
  a_task_in "$pan_id" 'Panou viu' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$past\"" || return 1
  live_task=$new_task
  pan_first=$(in_db "SELECT id FROM board_columns WHERE project_id = '$pan_id' AND position = 0")
  [ -n "$pan_first" ]
}

a_new_task_counts_open_and_overdue() {
  a_live_task_is_ready || return 1
  dans_numbers_are '4 2 1 2' &&
    is 'PAN-7 PAN-1 PAN-2 MEU-1' "$(my_task_keys "$dan_token" 'open=true')"
}

finishing_it_counts_it_this_week() {
  a_live_task_is_ready || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$live_task/position" "{\"columnId\":\"$pan_done\"}")" || return 1
  dans_numbers_are '3 1 2 2' &&
    is 'MEU:1:0 PAN:3:2' "$(project_counts "$dan_token" 'mine=true')"
}

archiving_and_restoring_it_takes_it_out_and_back() {
  a_live_task_is_ready || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$live_task")" || return 1
  dans_numbers_are '3 1 1 2' &&
    is 'MEU:1:0 PAN:3:1' "$(project_counts "$dan_token" 'mine=true')" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$live_task/restore")" || return 1
  dans_numbers_are '3 1 2 2'
}

reopening_it_and_moving_its_deadline_count_again() {
  a_live_task_is_ready || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$live_task/position" "{\"columnId\":\"$pan_first\"}")" || return 1
  dans_numbers_are '4 2 1 2' || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$live_task/due-date" "{\"dueDate\":\"$future\"}")" || return 1
  dans_numbers_are '4 1 1 2'
}

taking_dan_off_it_leaves_his_numbers() {
  a_live_task_is_ready || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$live_task/assignees" "{\"assigneeIds\":[\"$admin_id\"]}")" || return 1
  dans_numbers_are '3 1 1 2' &&
    is 'PAN-1 PAN-2 PAN-4 MEU-1' "$(my_task_keys "$dan_token" '')"
}

# Dan stays on MEU-1, but a project he left is no longer his on the panel.
leaving_a_project_takes_its_tasks_off_the_panel() {
  a_live_task_is_ready || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$meu_id/members/$dan_id")" || return 1
  dans_numbers_are '2 1 1 1' &&
    is 'PAN-1 PAN-2 PAN-4' "$(my_task_keys "$dan_token" '')" &&
    is 'PAN:4:1' "$(project_counts "$dan_token" 'mine=true')"
}

an_archived_project_leaves_the_panel_until_restored() {
  a_live_task_is_ready || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$pan_id")" || return 1
  dans_numbers_are '0 0 0 0' &&
    is '' "$(my_task_keys "$dan_token" '')" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/projects/$pan_id/restore")" || return 1
  dans_numbers_are '2 1 1 1'
}

task_4() {
  echo "17a.4 The group's flows end to end: a change to a task changes the panel's numbers"
  check "Docker is running" docker_is_running
  check "the flows' tests pass: finished, archived, restored, reopened, a deadline, assignees, projects" flow_tests_pass
  check "the real API counts a new task of Dan's, past its deadline, as open and overdue" a_new_task_counts_open_and_overdue
  check "moved to the done column, it counts as finished this week, on the project's card too" finishing_it_counts_it_this_week
  check "archived it leaves the numbers, restored it comes back finished" archiving_and_restoring_it_takes_it_out_and_back
  check "moved back it's open and overdue again, and a later deadline takes it off the overdue ones" reopening_it_and_moving_its_deadline_count_again
  check "taken off it, Dan no longer counts it" taking_dan_off_it_leaves_his_numbers
  check "out of a project, Dan no longer counts its tasks or the project" leaving_a_project_takes_its_tasks_off_the_panel
  check "an archived project leaves the panel until it's restored" an_archived_project_leaves_the_panel_until_restored
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
