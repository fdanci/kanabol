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
  pan_done=$(in_db "SELECT id FROM board_columns WHERE project_id = '$pan_id' AND is_done")
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
  is "$meu_id MEU Meu Critical De făcut" "$(json "[b.items[0].projectId, b.items[0].projectKey, b.items[0].projectName, b.items[0].priority, b.items[0].columnName].join(' ')")"
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

if [ "$#" -eq 0 ]; then
  set -- 1 2
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
