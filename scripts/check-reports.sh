#!/bin/sh
# Acceptance checks for the reports (group 18 in the project's Tasks.md; docs/SPEC.md §5.1.1 "Endpoint-urile
# rapoartelor", §6.1, §14.7; docs/UI_UX_SPEC.md §7.8, §7.3). The tests and specs hold the rules, so these
# checks run them, and add what they can't see: the real API process on a real PostgreSQL and its answers,
# and the frontend's production build.
#
# Usage: scripts/check-reports.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  the three queries, GET /api/reports/completed, cycle-time and deadlines: the tests, then the real API in
#      Production on work built through the API, its moments set in the database to October 2026: tasks
#      finished in several projects, one with two assignees, one archived after it was finished, one in a
#      project archived since, one created straight in the second column, one without a deadline, one
#      nobody's; the total, by user, by project, the periods by week and by month, from and to with an
#      offset across summer time, a project and a person; lead and cycle time as averages and medians, the
#      cycle from the first entry into În lucru (group 22), so from its creation for the task created straight
#      in the second column and for the one finished from Backlog, the time in each column with a
#      project only; the tasks overdue now whatever the interval, none in an archived project, those
#      finished by their deadline and after it, the rate, null without a deadline, and their periods; 400 on
#      from, to and granularity, 404 for a project that doesn't exist. Docker, PostgreSQL.
#   2  who reads them: the tests, then the real API in Production on the same work, each of the three for
#      reports.view in no project (every project, one, an archived one), an owner without it (their own
#      project, archived too, any person in it; none without a project, 400 on projectId; another's, 403),
#      projects.manage (any project), a plain member, projects.view_all and an outsider (403
#      Project.OwnerRequired), a project that doesn't exist (404), the permission's name only in the log,
#      and an owner deactivated since, whose token still holds (403). Docker, PostgreSQL.
#   3  the page, the project's tab and kb-chart: specs, lint, formatting, the production build without
#      warnings but the budget's, the texts of docs/UI_UX_SPEC.md §7.8, the filters in the address, the tab
#      loading the page with its project fixed (§7.3) and no placeholder left, durations as "2 z 4 h",
#      CSV with commas, quotes and a BOM, chart.js 4 with only the bar's controllers, the colors from the
#      tokens, no other feature imported, chart.js in a chunk of its own, loaded by @defer, and neither it
#      nor the page in the files index.html loads; prints the Initial total. Only the packages that
#      scripts/setup.sh installs.
#   4  the group's flows end to end: the API's tests (a task finished, moved back, finished again, archived
#      and restored, its deadline moved, an assignee taken off, an open one archived), then the same changes
#      on the real API in Production, in an interval around now, each changing the three reports' numbers
#      as the rules say, for reports.view and for the project's owner. Docker, PostgreSQL.
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database and
# drops it. Docker runs the tests' own PostgreSQL.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
# In the full gate, the build, tests and lint scripts/gate-prepare.sh did once (docs/SPEC.md §14.7).
[ -n "${KANABOL_GATE_CACHE:-}" ] && . scripts/gate-reuse.sh

failures=0
scratch=$(mktemp -d)
check_db="kanabol_reports_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5285
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

# A deadline long past, for a task still open.
past=2026-01-15T12:00:00Z

# Four weeks of October 2026, from Monday the 5th to Monday 2 November, in UTC.
october='from=2026-10-05T00:00:00Z&to=2026-11-02T00:00:00Z'

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
  printf '10.85.%s.%s' "$((used / 256))" "$((used % 256))"
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

field_is_refused() { # field_is_refused <field> <status of the request just sent>
  code_is 400 Validation "$2" || return 1
  is true "$(json "Object.keys(b.errors).includes('$1')")"
}

# The first admin past the forced password change. The API may have stopped since the token was taken:
# it comes back first, and the token, signed with the same secret, still holds. The Admin role has
# reports.view, as every permission.
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

# A colleague in the role given, or in one without permissions, past their forced password change: their
# id goes in <name>_id and their token in <name>_token.
colleague_is_ready() { # colleague_is_ready <name> <email> <display name> [role id]
  admin_is_ready || return 1
  eval "[ -n \"\${$1_token:-}\" ]" && return 0
  role=${4:-}
  if [ -z "$role" ]; then
    if [ -z "${plain_role:-}" ]; then
      status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Fara permisiuni","permissions":[]}')" || return 1
      plain_role=$(body_field id)
    fi
    role=$plain_role
  fi
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"$2\",\"displayName\":\"$3\",\"roleId\":\"$role\",\"initialPassword\":\"$initial_password\"}")" || return 1
  eval "$1_id=$(body_field id)"
  status_is 200 "$(login "$2" "$initial_password")" || return 1
  forced=$(body_field accessToken)
  status_is 200 "$(as "$forced" POST /api/auth/change-password "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$member_password\"}")" || return 1
  eval "$1_token=$(body_field accessToken)"
}

# ---- The run's people and work, made through the real API, its moments then set in the database.

# A task of the admin's in a project, its id in new_task.
a_task_in() { # a_task_in <project id> <title> <JSON fields to add>
  status_is 201 "$(as "$admin_token" POST "/api/projects/$1/tasks" "{\"title\":\"$2\",$3}")" || return 1
  new_task=$(body_field id)
}

moved() { # moved <task id> <column id>: by the admin, through the API, with an estimate, which only În lucru takes
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$1/position" "{\"columnId\":\"$2\",\"estimatedMinutes\":60}")"
}

# The API stamps everything with now: the task's creation, its entry into a column and its completion go
# back to the moments given, as if the work had been done then.
created_at() { # created_at <task id> <moment>
  in_db "UPDATE task_items SET created_at = '$2' WHERE id = '$1'" >/dev/null
}

entered_at() { # entered_at <task id> <column id> <moment>: the history's entry into that column, and the
  # completion with it when the task is completed there
  in_db "UPDATE task_history_entries SET changed_at = '$3' WHERE task_id = '$1' AND field = 'ColumnId' AND new_value = '$2'" >/dev/null &&
    in_db "UPDATE task_items SET completed_at = '$3' WHERE id = '$1' AND column_id = '$2' AND completed_at IS NOT NULL" >/dev/null
}

columns_of() { # columns_of <project id> <name prefix>: its columns' ids in <prefix>_todo, _doing, _paused and _done
  for place in 0:todo 1:doing 2:paused 3:done; do
    eval "$2_${place#*:}=$(in_db "SELECT id FROM board_columns WHERE project_id = '$1' AND position = ${place%%:*}")"
  done
}

# PAN and MEU are the admin's, with Dan, and Eva in MEU; ARH is the admin's, with Dan, archived at the end.
# PAN-1 Dan's and the admin's, created 1 Oct 09:00, În lucru 6 Oct 10:00, Gata 8 Oct 10:00, due 10 Oct;
# PAN-2 Dan's, created in În lucru 2 Oct 09:00, Gata 14 Oct 09:00, due 13 Oct 12:00 (late);
# PAN-3 Dan's and the admin's, without a deadline, created 15 Oct 10:00, În lucru 18 Oct 12:00, Gata 20 Oct
# 12:00, then archived; PAN-4 Dan's, open, long overdue. MEU-1 Eva's, created 20 Oct 10:00, În lucru 22 Oct
# 10:00, Gata 27 Oct 10:00, due 30 Oct (on time); MEU-2 nobody's, created 27 Oct 10:00, Gata 28 Oct 10:00.
# ARH-1 Dan's, created 10 Oct 10:00, În lucru 12 Oct 10:00, Gata 15 Oct 10:00; ARH-2 Dan's, open, long overdue.
work_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready eva eva@example.com 'Eva Marin' || return 1
  [ -n "${pan_id:-}" ] && return 0
  for project in 'Panou:PAN' 'Meu:MEU' 'Arhiva:ARH'; do
    status_is 201 "$(as "$admin_token" POST /api/projects "{\"name\":\"${project%%:*}\",\"key\":\"${project#*:}\"}")" || return 1
    id=$(body_field id)
    status_is 201 "$(as "$admin_token" POST "/api/projects/$id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
    prefix=$(printf '%s' "${project#*:}" | tr 'A-Z' 'a-z')
    eval "${prefix}_id=$id"
    columns_of "$id" "$prefix"
  done
  status_is 201 "$(as "$admin_token" POST "/api/projects/$meu_id/members" "{\"userId\":\"$eva_id\",\"role\":\"Member\"}")" || return 1

  a_task_in "$pan_id" 'Login SSO' "\"priority\":\"High\",\"assigneeIds\":[\"$dan_id\",\"$admin_id\"],\"dueDate\":\"2026-10-10T10:00:00Z\"" &&
    moved "$new_task" "$pan_doing" && moved "$new_task" "$pan_done" || return 1
  pan_1=$new_task
  created_at "$pan_1" 2026-10-01T09:00:00Z && entered_at "$pan_1" "$pan_doing" 2026-10-06T10:00:00Z &&
    entered_at "$pan_1" "$pan_done" 2026-10-08T10:00:00Z || return 1

  # Created straight in În lucru, as tasks could be before group 22: since then they start in Backlog, so
  # the database puts it there, without a move in its history.
  a_task_in "$pan_id" 'Pagina de start' "\"priority\":\"Low\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"2026-10-13T12:00:00Z\"" &&
    in_db "UPDATE task_items SET column_id = '$pan_doing', position = 0 WHERE id = '$new_task'" >/dev/null &&
    moved "$new_task" "$pan_done" || return 1
  pan_2=$new_task
  created_at "$pan_2" 2026-10-02T09:00:00Z && entered_at "$pan_2" "$pan_done" 2026-10-14T09:00:00Z || return 1

  a_task_in "$pan_id" 'Logo nou' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\",\"$admin_id\"]" &&
    moved "$new_task" "$pan_doing" && moved "$new_task" "$pan_done" || return 1
  pan_3=$new_task
  created_at "$pan_3" 2026-10-15T10:00:00Z && entered_at "$pan_3" "$pan_doing" 2026-10-18T12:00:00Z &&
    entered_at "$pan_3" "$pan_done" 2026-10-20T12:00:00Z || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$pan_3")" || return 1

  a_task_in "$pan_id" 'Intarziat' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$past\"" || return 1
  pan_4=$new_task

  a_task_in "$meu_id" 'Formular de contact' "\"priority\":\"Critical\",\"assigneeIds\":[\"$eva_id\"],\"dueDate\":\"2026-10-30T10:00:00Z\"" &&
    moved "$new_task" "$meu_doing" && moved "$new_task" "$meu_done" || return 1
  meu_1=$new_task
  created_at "$meu_1" 2026-10-20T10:00:00Z && entered_at "$meu_1" "$meu_doing" 2026-10-22T10:00:00Z &&
    entered_at "$meu_1" "$meu_done" 2026-10-27T10:00:00Z || return 1

  a_task_in "$meu_id" 'Fara nimeni' '"priority":"Medium"' && moved "$new_task" "$meu_done" || return 1
  meu_2=$new_task
  created_at "$meu_2" 2026-10-27T10:00:00Z && entered_at "$meu_2" "$meu_done" 2026-10-28T10:00:00Z || return 1

  a_task_in "$arh_id" 'Din arhiva' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"]" &&
    moved "$new_task" "$arh_doing" && moved "$new_task" "$arh_done" || return 1
  arh_1=$new_task
  created_at "$arh_1" 2026-10-10T10:00:00Z && entered_at "$arh_1" "$arh_doing" 2026-10-12T10:00:00Z &&
    entered_at "$arh_1" "$arh_done" 2026-10-15T10:00:00Z || return 1
  a_task_in "$arh_id" 'Uitat in arhiva' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$past\"" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$arh_id")"
}

report() { # report <token> <report> <query>: leaves the answer as the last one
  status_is 200 "$(as "$1" GET "/api/reports/$2?$3")"
}

counts() { # counts <rows> <label>: label:count of each row of the last answer
  json "b.$1.map(r => r.$2 + ':' + r.count).join(' ')"
}

# ---- 18.1: GET /api/reports/completed.

completed_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetCompletedReportQueryHandlerTests' 30
}

cycle_time_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetCycleTimeReportQueryHandlerTests' 16
}

deadlines_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~GetDeadlinesReportQueryHandlerTests' 14
}

endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ReportsEndpointTests' 42 &&
    named_tests_pass Kanabol.Api.Tests EndpointPermissionsTests.Reports_NeedNoPermissionOnTheEndpoint
}

# PAN-1, PAN-2, PAN-3 (archived after it was finished), MEU-1, MEU-2 (nobody's), ARH-1 (archived project).
the_finished_work_is_counted() {
  work_is_ready || return 1
  report "$admin_token" completed "$october" || return 1
  is 6 "$(json b.total)" &&
    is 'PAN:3 MEU:2 ARH:1' "$(counts byProject key)" &&
    is "$pan_id Panou" "$(json "[b.byProject[0].projectId, b.byProject[0].name].join(' ')")"
}

# PAN-1 and PAN-3 count for Dan and for the admin: the sum by person goes past the total.
each_assignee_counts_a_task() {
  work_is_ready || return 1
  report "$admin_token" completed "$october" || return 1
  is 'Dan Ionescu:4 Administrator:2 Eva Marin:1' "$(counts byUser name)" &&
    is "$dan_id" "$(json b.byUser[0].userId)"
}

the_weeks_start_at_from() {
  work_is_ready || return 1
  report "$admin_token" completed "$october" || return 1
  is '2026-10-05T00:00:00+00:00:1 2026-10-12T00:00:00+00:00:2 2026-10-19T00:00:00+00:00:1 2026-10-26T00:00:00+00:00:2' \
    "$(counts series periodStart)"
}

the_months_are_calendar_months() {
  work_is_ready || return 1
  report "$admin_token" completed 'from=2026-09-01T00:00:00Z&to=2026-11-03T00:00:00Z&granularity=month' || return 1
  is '2026-09-01T00:00:00+00:00:0 2026-10-01T00:00:00+00:00:6 2026-11-01T00:00:00+00:00:0' "$(counts series periodStart)"
}

# October in Bucharest starts at +03:00 and ends at +02:00: one period, on the person's own clock.
a_month_across_summer_time_is_one_period() {
  work_is_ready || return 1
  report "$admin_token" completed 'from=2026-10-01T00:00:00%2B03:00&to=2026-11-01T00:00:00%2B02:00&granularity=month' || return 1
  is '2026-10-01T00:00:00+03:00:6' "$(counts series periodStart)"
}

a_project_and_a_person_narrow_it() {
  work_is_ready || return 1
  report "$admin_token" completed "$october&projectId=$pan_id&userId=$dan_id" || return 1
  is '3 PAN:3 Dan Ionescu:3' "$(json b.total) $(counts byProject key) $(counts byUser name)" || return 1
  report "$admin_token" completed "$october&userId=$eva_id" || return 1
  is '1 MEU:1' "$(json b.total) $(counts byProject key)"
}

an_archived_project_is_reported() {
  work_is_ready || return 1
  report "$admin_token" completed "$october&projectId=$arh_id" || return 1
  is 1 "$(json b.total)"
}

wrong_intervals_are_refused() {
  work_is_ready || return 1
  field_is_refused from "$(as "$admin_token" GET '/api/reports/completed?to=2026-11-02T00:00:00Z')" &&
    field_is_refused from "$(as "$admin_token" GET '/api/reports/completed?from=luni&to=2026-11-02T00:00:00Z')" &&
    field_is_refused to "$(as "$admin_token" GET '/api/reports/completed?from=2026-10-05T00:00:00Z')" &&
    field_is_refused to "$(as "$admin_token" GET '/api/reports/completed?from=2026-11-02T00:00:00Z&to=2026-10-05T00:00:00Z')" &&
    field_is_refused to "$(as "$admin_token" GET '/api/reports/completed?from=2026-01-01T00:00:00Z&to=2027-01-02T00:00:01Z')" &&
    status_is 200 "$(as "$admin_token" GET '/api/reports/completed?from=2026-01-01T00:00:00Z&to=2027-01-02T00:00:00Z')" &&
    field_is_refused granularity "$(as "$admin_token" GET "/api/reports/completed?$october&granularity=day")"
}

a_project_that_does_not_exist_is_404() {
  work_is_ready || return 1
  code_is 404 Project.NotFound "$(as "$admin_token" GET "/api/reports/completed?$october&projectId=0199a000-0000-7000-8000-000000000000")"
}

durations() { # durations <field>: average/median of a duration of the last answer, - for null
  json "[b.$1.averageSeconds, b.$1.medianSeconds].map(s => s === null ? '-' : s).join('/')"
}

# Leads: PAN-1 7 d 1 h, PAN-2 12 d, PAN-3 5 d 2 h, MEU-1 7 d, MEU-2 1 d, ARH-1 5 d. Cycles, from the first
# entry into În lucru (group 22): PAN-1 2 d, PAN-2 12 d (created in În lucru, so from its creation), PAN-3 2 d,
# MEU-1 5 d, MEU-2 1 d (straight from Backlog to Gata, so from its creation), ARH-1 3 d.
the_lead_and_cycle_times_are_measured() {
  work_is_ready || return 1
  report "$admin_token" cycle-time "$october" || return 1
  is '6 534600/522000 360000/216000 null' "$(json b.count) $(durations leadTime) $(durations cycleTime) $(json b.perColumn)"
}

# PAN-1: Backlog 5 d 1 h, În lucru 2 d; PAN-2: În lucru 12 d; PAN-3: Backlog 3 d 2 h, În lucru 2 d.
each_column_has_its_time_in_a_project() {
  work_is_ready || return 1
  report "$admin_token" cycle-time "$october&projectId=$pan_id" || return 1
  is 'Backlog 351000/351000, În lucru 460800/172800, În pauză null/null, Gata 0/0' \
    "$(json "b.perColumn.map(c => c.name + ' ' + c.averageSeconds + '/' + c.medianSeconds).join(', ')")" &&
    is "$pan_todo $pan_doing $pan_paused $pan_done" "$(json "b.perColumn.map(c => c.columnId).join(' ')")"
}

a_persons_times_are_theirs() {
  work_is_ready || return 1
  report "$admin_token" cycle-time "$october&userId=$eva_id" || return 1
  is '1 604800/604800 432000/432000' "$(json b.count) $(durations leadTime) $(durations cycleTime)" || return 1
  report "$admin_token" cycle-time "$october&projectId=$meu_id&userId=$dan_id" || return 1
  is '0 -/- -/-' "$(json b.count) $(durations leadTime) $(durations cycleTime)"
}

cycle_time_intervals_are_refused() {
  work_is_ready || return 1
  field_is_refused from "$(as "$admin_token" GET '/api/reports/cycle-time?to=2026-11-02T00:00:00Z')" &&
    field_is_refused to "$(as "$admin_token" GET '/api/reports/cycle-time?from=2026-01-01T00:00:00Z&to=2027-01-02T00:00:01Z')"
}

deadline_numbers() { # deadline_numbers: overdue onTime late rate of the last answer, null as null
  json "[b.overdue, b.completedOnTime, b.completedLate, b.onTimeRate].map(String).join(' ')"
}

deadline_periods() {
  json "b.series.map(p => p.onTime + '/' + p.late).join(' ')"
}

# On time: PAN-1 (8 Oct, due the 10th) and MEU-1 (27 Oct, due the 30th). Late: PAN-2 (14 Oct 09:00, due
# the 13th at noon). Overdue now: PAN-4; ARH-2 is in an archived project.
the_deadlines_are_kept_and_missed() {
  work_is_ready || return 1
  report "$admin_token" deadlines "$october" || return 1
  is '1 2 1 0.6666666666666666' "$(deadline_numbers)" &&
    is '1/0 0/1 0/0 1/0' "$(deadline_periods)" || return 1
  report "$admin_token" deadlines 'from=2026-09-01T00:00:00Z&to=2026-11-03T00:00:00Z&granularity=month' || return 1
  is '0/0 2/1 0/0' "$(deadline_periods)"
}

overdue_is_todays_state() {
  work_is_ready || return 1
  report "$admin_token" deadlines 'from=2026-03-01T00:00:00Z&to=2026-04-01T00:00:00Z' || return 1
  is '1 0 0 null' "$(deadline_numbers)"
}

the_deadlines_follow_the_filter() {
  work_is_ready || return 1
  report "$admin_token" deadlines "$october&projectId=$pan_id" || return 1
  is '1 1 1 0.5' "$(deadline_numbers)" || return 1
  report "$admin_token" deadlines "$october&userId=$eva_id" || return 1
  is '0 1 0 1' "$(deadline_numbers)" || return 1
  report "$admin_token" deadlines "$october&projectId=$arh_id" || return 1
  is '0 0 0 null' "$(deadline_numbers)"
}

deadlines_intervals_are_refused() {
  work_is_ready || return 1
  field_is_refused to "$(as "$admin_token" GET '/api/reports/deadlines?from=2026-10-05T00:00:00Z&to=2026-10-04T00:00:00Z')" &&
    field_is_refused granularity "$(as "$admin_token" GET "/api/reports/deadlines?$october&granularity=quarter")"
}

task_1() {
  echo "18.1 The reports' queries: GET /api/reports/completed, cycle-time and deadlines"
  check "Docker is running" docker_is_running
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the tests of the finished tasks pass: the interval, archived, assignees, weeks, months, offsets" completed_tests_pass
  check "the tests of lead and cycle time pass: the first column, created in the second, reopened, columns" cycle_time_tests_pass
  check "the tests of the deadlines pass: overdue now, on time at the very deadline, the rate, archived" deadlines_tests_pass
  check "the endpoints' tests pass, with no permission on them" endpoint_tests_pass
  check "the real API in Production counts each finished task once, by project, archived ones too" the_finished_work_is_counted
  check "a task with two assignees counts for each, the sum going past the total" each_assignee_counts_a_task
  check "the weeks start at from and step seven days" the_weeks_start_at_from
  check "by the month the periods are calendar months" the_months_are_calendar_months
  check "October across summer time, +03:00 to +02:00, is one period" a_month_across_summer_time_is_one_period
  check "a project and a person narrow the report" a_project_and_a_person_narrow_it
  check "an archived project's work is reported" an_archived_project_is_reported
  check "from and to missing, malformed, out of order or over 366 days, and a granularity unknown, answer 400" wrong_intervals_are_refused
  check "a project that doesn't exist answers 404 Project.NotFound" a_project_that_does_not_exist_is_404
  check "lead and cycle time come as averages and medians in seconds, the cycle from the first entry into În lucru" the_lead_and_cycle_times_are_measured
  check "with a project, each of its columns has its time, in the board's order" each_column_has_its_time_in_a_project
  check "a person's times are those of their tasks, and null when nothing was finished" a_persons_times_are_theirs
  check "cycle-time takes the same interval and refuses the same mistakes" cycle_time_intervals_are_refused
  check "the deadlines: overdue now, on time, late, the rate and their periods, by week and by month" the_deadlines_are_kept_and_missed
  check "the tasks overdue are today's, whatever the interval" overdue_is_todays_state
  check "a project and a person narrow them, and an archived project has nothing overdue" the_deadlines_follow_the_filter
  check "deadlines takes the same interval and refuses the same mistakes" deadlines_intervals_are_refused
  check "backend/CLAUDE.md lists the endpoints" in_file '`GET /api/reports/deadlines' backend/CLAUDE.md
}

# ---- 18.2: who reads them.

access_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~ReportAccessTests' 37 &&
    named_tests_pass Kanabol.Api.Tests ReportsEndpointTests.Report_
}

role_is_ready() { # role_is_ready <variable> <name> <permission>: the role's id in the variable
  eval "[ -n \"\${$1:-}\" ]" && return 0
  status_is 201 "$(as "$admin_token" POST /api/roles "{\"name\":\"$2\",\"permissions\":[\"$3\"]}")" || return 1
  eval "$1=$(body_field id)"
}

# Radu has reports.view and is in no project; Mara has projects.manage; Vera projects.view_all; Olga owns
# MEU and ARH, without a permission; Ion is in no project, without one; Dan is a member of PAN.
readers_are_ready() {
  work_is_ready || return 1
  [ -n "${olga_token:-}" ] && return 0
  role_is_ready reports_role Rapoarte reports.view &&
    role_is_ready manage_role Administreaza projects.manage &&
    role_is_ready view_all_role 'Vede tot' projects.view_all || return 1
  colleague_is_ready radu radu@example.com 'Radu Rapoarte' "$reports_role" &&
    colleague_is_ready mara mara@example.com 'Mara Manager' "$manage_role" &&
    colleague_is_ready vera vera@example.com 'Vera Vede' "$view_all_role" &&
    colleague_is_ready ion ion@example.com 'Ion Strain' &&
    colleague_is_ready olga olga@example.com 'Olga Owner' || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$meu_id/members" "{\"userId\":\"$olga_id\",\"role\":\"Owner\"}")" &&
    status_is 200 "$(as "$admin_token" POST "/api/projects/$arh_id/restore")" &&
    status_is 201 "$(as "$admin_token" POST "/api/projects/$arh_id/members" "{\"userId\":\"$olga_id\",\"role\":\"Owner\"}")" &&
    status_is 204 "$(as "$admin_token" DELETE "/api/projects/$arh_id")"
}

reads_all() { # reads_all <token> <query>: each of the three reports answers 200
  for name in completed cycle-time deadlines; do
    status_is 200 "$(as "$1" GET "/api/reports/$name?$october$2")" || {
      echo "on $name"
      return 1
    }
  done
}

refused_all() { # refused_all <token> <query> <status> <code>: each of the three refuses with it
  for name in completed cycle-time deadlines; do
    code_is "$3" "$4" "$(as "$1" GET "/api/reports/$name?$october$2")" || {
      echo "on $name"
      return 1
    }
  done
}

reports_view_reads_every_project() {
  readers_are_ready || return 1
  reads_all "$radu_token" '' && reads_all "$radu_token" "&projectId=$pan_id" && reads_all "$radu_token" "&projectId=$arh_id" || return 1
  report "$radu_token" completed "$october" || return 1
  is 6 "$(json b.total)"
}

an_owner_reads_their_project() {
  readers_are_ready || return 1
  reads_all "$olga_token" "&projectId=$meu_id" && reads_all "$olga_token" "&projectId=$arh_id" &&
    reads_all "$olga_token" "&projectId=$meu_id&userId=$dan_id" || return 1
  report "$olga_token" completed "$october&projectId=$meu_id" || return 1
  is 2 "$(json b.total)"
}

an_owner_needs_a_project() {
  readers_are_ready || return 1
  for name in completed cycle-time deadlines; do
    field_is_refused projectId "$(as "$olga_token" GET "/api/reports/$name?$october")" || return 1
  done
}

an_owner_reads_no_other_project() {
  readers_are_ready || return 1
  refused_all "$olga_token" "&projectId=$pan_id" 403 Project.OwnerRequired
}

projects_manage_reads_any_project() {
  readers_are_ready || return 1
  reads_all "$mara_token" "&projectId=$pan_id" && reads_all "$mara_token" "&projectId=$arh_id" &&
    field_is_refused projectId "$(as "$mara_token" GET "/api/reports/completed?$october")"
}

members_viewers_and_outsiders_are_refused() {
  readers_are_ready || return 1
  refused_all "$dan_token" "&projectId=$pan_id" 403 Project.OwnerRequired &&
    refused_all "$vera_token" "&projectId=$pan_id" 403 Project.OwnerRequired &&
    refused_all "$ion_token" "&projectId=$pan_id" 403 Project.OwnerRequired &&
    field_is_refused projectId "$(as "$dan_token" GET "/api/reports/deadlines?$october")"
}

an_unknown_project_is_404_for_anyone() {
  readers_are_ready || return 1
  refused_all "$ion_token" '&projectId=0199a000-0000-7000-8000-000000000000' 404 Project.NotFound
}

# docs/SPEC.md §4.4: the permission's name goes only into the log, at Warning.
the_permission_is_named_only_in_the_log() {
  readers_are_ready || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" GET "/api/reports/completed?$october&projectId=$pan_id")" || return 1
  lacks 'reports\.view' "$scratch/body" || return 1
  grep -F 'asked for a report of project' "$api_log" | grep -qF 'reports.view' || {
    echo "no warning naming reports.view in the API's log"
    return 1
  }
}

# Last: IProjectAccess reads the account from the database, so the token Olga still holds opens nothing.
a_deactivated_owner_reads_nothing() {
  readers_are_ready || return 1
  status_is 200 "$(as "$admin_token" POST "/api/users/$olga_id/deactivate")" || return 1
  refused_all "$olga_token" "&projectId=$meu_id" 403 Project.OwnerRequired
}

task_2() {
  echo "18.2 Who reads the reports: reports.view, or an owner of the project asked for"
  check "Docker is running" docker_is_running
  check "the tests of who reads them pass, for each of the three reports" access_tests_pass
  check "reports.view, in no project, reads every project, one, and an archived one" reports_view_reads_every_project
  check "an owner without reports.view reads their project, archived too, for any person in it" an_owner_reads_their_project
  check "an owner without reports.view and without a project gets 400 on projectId" an_owner_needs_a_project
  check "an owner reads no other project: 403 Project.OwnerRequired" an_owner_reads_no_other_project
  check "projects.manage reads any project, and needs one too" projects_manage_reads_any_project
  check "a plain member, projects.view_all and an outsider get 403 Project.OwnerRequired" members_viewers_and_outsiders_are_refused
  check "a project that doesn't exist answers 404 Project.NotFound, whoever asks" an_unknown_project_is_404_for_anyone
  check "the permission's name is in the log's warning, not in the answer" the_permission_is_named_only_in_the_log
  check "an owner deactivated since reads nothing with the token they still hold" a_deactivated_owner_reads_nothing
  check "backend/CLAUDE.md says who reads them" in_file '`reports.view`, or an owner or `projects.manage` of `projectId`' backend/CLAUDE.md
}

# ---- 18.3: the page and kb-chart.

web=frontend
app=$web/src/app
reports_web=$app/features/reports

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

the_page_specs_name_the_rules() {
  spec_names "$reports_web/reports-page.spec.ts" \
    'is titled "Rapoarte", under Acasă' \
    'opens on the last 30 days, by the week, for every project and everyone' \
    'takes the filters from the address: from, to, project, user and granularity' \
    'sends a wrong interval in the address back to the last 30 days' \
    'puts a preset in the address, and the last 30 days out of it' \
    'bounds the dates to each other and to 366 days' \
    'puts a day chosen in a datepicker in the address' \
    'chooses a project, a person and the month through the address, the person going with the project' \
    'offers every project the person sees, the archived ones said so' \
    "offers the project's members, and without a project the report's people" \
    'shows the finished tasks: the total, by person with its note, by period, person and project' \
    'shows lead and cycle time as "2 z 4 h", with the median in the caption' \
    "shows the time in each of the project's columns with a project" \
    'shows the deadlines: overdue now, on time, late and the rate as a percent' \
    'draws the three charts: by period, the times with mean and median, and on time stacked on late' \
    'exports the data of a section as CSV, named by what it holds and its days' \
    'says "Niciun task finalizat în interval." and "Niciun task cu deadline în interval."' \
    'shows a skeleton in each section while it reads' \
    'says a section could not be read, and tries that one again' &&
    spec_names "$reports_web/report-filters.spec.ts" \
      'opens on the last 30 days, today included, by the week' \
      'takes $query as a wrong interval: $reason' \
      'gives this week from Monday to Sunday, this month from the first to the last day' \
      'sends midnight where the person is, with its offset, and the day after to' \
      'bounds the datepickers to 366 days either way' &&
    spec_names "$reports_web/reports-api.spec.ts" \
      'sends the offset with its + encoded' \
      'reads every project the person sees, archived ones too' &&
    spec_names "$reports_web/durations.spec.ts" 'writes days and hours' 'writes minutes under an hour' &&
    spec_names "$reports_web/report-csv.spec.ts" \
      'starts with a BOM and quotes every field, a row to a line' \
      'doubles the quotes in a field, and keeps its commas and diacritics' &&
    spec_names "$app/shared/ui/chart.spec.ts" \
      'colors the main series --kb-chart-1 and the second --kb-chart-2, from the tokens' \
      'draws a recessive grid in border-200, the labels in text-600, and no grid across the bars' \
      'shows a legend only for two series or more' \
      'stacks the bars when asked, with a gap in the surface between the pieces' \
      'writes its tooltip as text, the period and each series with its value' &&
    spec_names "$app/shared/ui/stat-tile.spec.ts" 'shows a text as it is, in place of a number'
}

# docs/UI_UX_SPEC.md §7.8, word for word where it gives the words.
the_page_texts_follow_the_ui_spec() {
  for words in 'Rapoarte' 'Săptămâna asta' 'Luna asta' 'Ultimele 30 de zile' 'Săptămână' 'Lună' \
    'Nu am putut încărca raportul.' 'Niciun task finalizat în interval.' 'Niciun task cu deadline în interval.' \
    "'finalizate'" 'mediana'; do
    in_file "$words" "$reports_web/reports-page.ts" || {
      echo "no '$words' in reports-page.ts"
      return 1
    }
  done
  for words in '>Task-uri finalizate<' '>Lead time și cycle time<' '>Deadline-uri<' 'Export CSV' 'Reîncearcă' \
    'label="Finalizate"' 'label="Pe om"' 'caption="un task cu mai mulți asignați se numără la fiecare"' \
    'label="Lead time mediu"' 'label="Cycle time mediu"' 'label="Întârziate acum"' 'label="La timp"' \
    'label="După termen"' 'label="Rata de respectare"' 'mat-datepicker' 'kb-filter-chip' '[stacked]="true"'; do
    in_file "$words" "$reports_web/reports-page.html" || {
      echo "no '$words' in reports-page.html"
      return 1
    }
  done
  # Four charts since group 22, Estimare și timp lucrat's the fourth.
  [ "$(grep -c '@defer (on idle)' "$reports_web/reports-page.html")" -eq 4 ] || {
    echo "the four charts don't each come with @defer"
    return 1
  }
  in_file '`${days} z ${hours} h`' "$reports_web/durations.ts" &&
    in_file '`${minutes} min`' "$reports_web/durations.ts" &&
    in_file "const BOM = '\\uFEFF';" "$reports_web/report-csv.ts" &&
    in_file "replaceAll('\"', '\"\"')" "$reports_web/report-csv.ts" &&
    in_file "export const MAX_REPORT_DAYS = 366;" "$reports_web/report-filters.ts" &&
    lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-|innerHTML|bypassSecurityTrust' \
      "$reports_web"/*.ts "$reports_web"/*.html "$reports_web"/*.scss "$app/shared/ui/chart.ts"
}

# docs/SPEC.md §5.1.1: chart.js 4, only what the bars use, the colors from the theme's tokens.
the_chart_is_chart_js_4_with_only_the_bars() {
  in_file '"chart.js": "^4.' "$web/package.json" &&
    node -e "const lock = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); const v = lock.packages['node_modules/chart.js'].version; if (!/^4\./.test(v)) { console.log('chart.js ' + v); process.exit(1); }" "$(native "$web/package-lock.json")" &&
    in_file 'ChartJs.register(BarController, BarElement, CategoryScale, LinearScale, Legend, Tooltip);' "$app/shared/ui/chart.ts" &&
    lacks "chart\.js/auto|registerables" "$app"/shared/ui/*.ts "$reports_web"/*.ts &&
    in_file "'--kb-chart-1'" "$app/shared/ui/chart.ts" &&
    in_file "'--kb-chart-2'" "$app/shared/ui/chart.ts" &&
    in_file "token('--kb-border-200')" "$app/shared/ui/chart.ts" &&
    in_file '.kb-chart-tooltip {' "$web/src/styles/_utilities.scss" &&
    [ "$(grep -rlF "from 'chart.js'" "$app" --include='*.ts' | grep -v '\.spec\.ts$')" = "$app/shared/ui/chart.ts" ]
}

# docs/SPEC.md §6.1: a feature imports no other feature; what they share is in shared/ or core/.
the_page_imports_no_other_feature() {
  lacks "from '\.\./(auth|users|roles|projects|board|tasks|notifications|dashboard)/" "$reports_web"/*.ts
}

reports_is_its_route() {
  in_file "import('./features/reports/reports.routes')" "$app/app.routes.ts" &&
    lacks "placeholder\('reports'|PlaceholderPage" "$app/app.routes.ts"
}

# docs/SPEC.md §6.1: the one feature another's route loads, the page with its project fixed (UI §7.3).
the_projects_tab_loads_the_page() {
  routes="$app/features/projects/projects.routes.ts"
  in_file "loadComponent: () => import('../reports/reports-page').then((page) => page.ReportsPage)," "$routes" &&
    in_file "data: { projectFixed: true }," "$routes" &&
    lacks "ProjectPlaceholder|project-placeholder" "$routes" &&
    [ ! -e "$app/features/projects/project-placeholder.ts" ] &&
    lacks "from '\.\./reports/" "$app"/features/projects/*.ts
}

the_tab_specs_name_the_rules() {
  spec_names "$reports_web/reports-page.spec.ts" \
    'reads the project of the route, whatever the address says, without a title, a breadcrumb or a project select' \
    'keeps the project out of the address' \
    "reads the project's members for the user select, and no list of projects" \
    'follows the route to another project' &&
    spec_names "$reports_web/report-filters.spec.ts" \
      'keeps the project fixed by the route, whatever the address says' \
      'leaves the project out of the address when the route fixes it'
}

# The page is in a chunk of its own, which both routes load, not in the projects' chunk.
the_tab_keeps_the_page_in_its_chunk() {
  built || return 1
  page=$(grep -lE 'kb-reports-page' "$scratch"/dist/browser/*.js | sed 's|.*/||')
  projects=$(grep -lE 'kb-project-page' "$scratch"/dist/browser/*.js | sed 's|.*/||')
  [ "$(printf '%s\n' $page | wc -l)" -eq 1 ] && [ -n "$projects" ] && [ "$page" != "$projects" ] || {
    echo "the page is in: ${page:-no file}; the project page in: ${projects:-no file}"
    return 1
  }
}

# The initial bundle is what index.html loads: its scripts and the chunks it preloads.
initial_files() {
  grep -oE '<script src="[^"]*"|<link rel="modulepreload" href="[^"]*"' "$scratch/dist/browser/index.html" |
    sed 's/.*"\([^"]*\)"$/\1/'
}

# chart.js's own names, which the minifier keeps, and the page's selector and texts.
chart_markers='datasetElementType|_getStackCount|getLabelAndValue'
page_markers='kb-reports-page|Nu am putut încărca raportul|Niciun task cu deadline în interval'

# docs/SPEC.md §5.1.1: chart.js in no file index.html loads, in a chunk of its own that @defer loads,
# not even the page's; and none of the page in the initial bundle.
chart_js_stays_out_of_the_first_bundle() {
  built || return 1
  initial=$(initial_files)
  [ -n "$initial" ] || {
    echo "index.html loads nothing"
    return 1
  }
  for file in $initial; do
    if grep -qE "$chart_markers|$page_markers" "$scratch/dist/browser/$file"; then
      echo "$file, in the initial bundle, holds: $(grep -oE "$chart_markers|$page_markers" "$scratch/dist/browser/$file" | sort -u | tr '\n' ' ')"
      return 1
    fi
  done
  chart=$(grep -lE "$chart_markers" "$scratch"/dist/browser/*.js | sed 's|.*/||')
  page=$(grep -lE 'kb-reports-page' "$scratch"/dist/browser/*.js | sed 's|.*/||')
  [ "$(printf '%s\n' $chart | wc -l)" -eq 1 ] && [ "$(printf '%s\n' $page | wc -l)" -eq 1 ] || {
    echo "chart.js is in: ${chart:-no file}; the page in: ${page:-no file}"
    return 1
  }
  [ "$chart" != "$page" ] || {
    echo "chart.js is in the page's own chunk, $page: @defer didn't split it"
    return 1
  }
  initial_total=$(plain "$scratch/build.log" | sed -n 's/.*Initial total *| *\([0-9.]* kB\).*/\1/p')
  echo "initial total $initial_total, chart.js in $chart, the page in $page" >&2
}

task_3() {
  echo "18.3 The reports page, the project's tab and kb-chart"
  for file in models.ts reports-api.ts reports-store.ts reports-page.ts reports-page.html reports-page.scss \
    reports.routes.ts report-filters.ts durations.ts report-csv.ts reports-testing.ts reports-api.spec.ts \
    reports-page.spec.ts report-filters.spec.ts durations.spec.ts report-csv.spec.ts; do
    check "features/reports/$file is tracked" tracked "$reports_web/$file"
  done
  check "shared/ui/chart.ts, its spec and shared/section.ts are tracked" eval \
    'tracked "$app/shared/ui/chart.ts" && tracked "$app/shared/ui/chart.spec.ts" && tracked "$app/shared/section.ts"'
  check "the specs name the rules: the filters, the sections, the charts, CSV, skeletons, errors" the_page_specs_name_the_rules
  check "the specs name the tab's rules: the project fixed, out of the address, its members" the_tab_specs_name_the_rules
  check "the page's specs pass, with its tab, filters, durations, CSV and API" specs_pass 'src/app/features/reports/*.spec.ts' 61
  check "the projects' specs still pass, the tab among the views" specs_pass 'src/app/features/projects/*.spec.ts' 63
  check "kb-chart's and kb-stat-tile's specs pass" specs_pass 'src/app/shared/ui/{chart,stat-tile}.spec.ts' 12
  check "the main panel's specs still pass, its sections on the shared Section" specs_pass 'src/app/features/dashboard/*.spec.ts' 25
  check "the texts follow docs/UI_UX_SPEC.md §7.8, each chart with @defer, nothing unsafe" the_page_texts_follow_the_ui_spec
  check "kb-chart is chart.js 4 with only the bars, colored from the tokens" the_chart_is_chart_js_4_with_only_the_bars
  check "the page imports no other feature" the_page_imports_no_other_feature
  check "/reports is the page, not a placeholder" reports_is_its_route
  check "the project's Rapoarte tab loads the page with its project fixed, the placeholder gone" the_projects_tab_loads_the_page
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "the build warns only of the budget" the_build_warns_only_of_the_budget
  check "chart.js is in a chunk of its own, and neither it nor the page is in the initial bundle" chart_js_stays_out_of_the_first_bundle
  check "the tab keeps the page in its own chunk, out of the projects' one" the_tab_keeps_the_page_in_its_chunk
  [ -f "$scratch/build.log" ] && printf '        %s\n' "$(plain "$scratch/build.log" | grep -F 'Initial total' | tr -s ' ')"
  check "frontend/CLAUDE.md says how the reports work, and the project's tab" eval \
    "in_file '\`ReportsStore\`' frontend/CLAUDE.md && in_file '\`projectFixed\`' frontend/CLAUDE.md"
}

# ---- 18.4: the group's flows end to end.

flow_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ReportsFlowTests' 7
}

# A day either side of now: the work this task changes is finished now, unlike the October one.
around_now="from=$(date -u -d '-1 day' +%Y-%m-%dT%H:%M:%SZ)&to=$(date -u -d '+1 day' +%Y-%m-%dT%H:%M:%SZ)"

# finished measured overdue onTime late rate, of PAN around now.
numbers_now() { # numbers_now <token> [query to add]
  report "$1" completed "$around_now&projectId=$pan_id${2:-}" || return 1
  finished=$(json b.total)
  report "$1" cycle-time "$around_now&projectId=$pan_id${2:-}" || return 1
  measured=$(json b.count)
  report "$1" deadlines "$around_now&projectId=$pan_id${2:-}" || return 1
  printf '%s %s %s\n' "$finished" "$measured" "$(json "[b.overdue, b.completedOnTime, b.completedLate, b.onTimeRate].map(String).join(' ')")"
}

# PAN-6, Dan's, due tomorrow: the task every later check of this task follows. Olga, from task 2, owns MEU only.
a_live_task_is_ready() {
  work_is_ready || return 1
  [ -n "${live_task:-}" ] && return 0
  is '0 0 1 0 0 null' "$(numbers_now "$admin_token")" || return 1
  a_task_in "$pan_id" 'Raport viu' "\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"$(date -u -d '+1 day' +%Y-%m-%dT%H:%M:%SZ)\"" || return 1
  live_task=$new_task
}

finishing_it_counts_it_on_time() {
  a_live_task_is_ready || return 1
  moved "$live_task" "$pan_done" || return 1
  is '1 1 1 1 0 1' "$(numbers_now "$admin_token")" &&
    is '0 0 0 0 0 null' "$(numbers_now "$admin_token" "&userId=$admin_id")"
}

moving_it_back_takes_it_out() {
  a_live_task_is_ready || return 1
  # Out of Gata to În lucru, never back before it (group 22).
  moved "$live_task" "$pan_doing" || return 1
  is '0 0 1 0 0 null' "$(numbers_now "$admin_token")" || return 1
  moved "$live_task" "$pan_done" || return 1
  is '1 1 1 1 0 1' "$(numbers_now "$admin_token")"
}

archiving_and_restoring_it_keeps_it() {
  a_live_task_is_ready || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$live_task")" || return 1
  is '1 1 1 1 0 1' "$(numbers_now "$admin_token")" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$live_task/restore")" || return 1
  is '1 1 1 1 0 1' "$(numbers_now "$admin_token")"
}

a_deadline_moved_before_turns_it_late() {
  a_live_task_is_ready || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$live_task/due-date" "{\"dueDate\":\"$(date -u -d '-2 hours' +%Y-%m-%dT%H:%M:%SZ)\"}")" || return 1
  is '1 1 1 0 1 0' "$(numbers_now "$admin_token")"
}

# Dan is a plain member of PAN: the admin makes him an owner, and he reads the same numbers without reports.view.
an_owner_reads_the_same_numbers() {
  a_live_task_is_ready || return 1
  code_is 403 Project.OwnerRequired "$(as "$dan_token" GET "/api/reports/completed?$around_now&projectId=$pan_id")" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$pan_id/members/$dan_id" '{"role":"Owner"}')" || return 1
  is "$(numbers_now "$admin_token")" "$(numbers_now "$dan_token")"
}

task_4() {
  echo "18.4 The group's flows end to end: a change to a task changes the reports"
  check "Docker is running" docker_is_running
  check "the flows' tests pass: finished, moved back, archived, restored, deadlines, assignees" flow_tests_pass
  check "the real API counts a new task of Dan's, finished by its deadline, as finished on time" finishing_it_counts_it_on_time
  check "moved back it leaves the reports, finished again it comes back" moving_it_back_takes_it_out
  check "archived after it was finished it stays, and restored too" archiving_and_restoring_it_keeps_it
  check "its deadline moved before its completion turns it late" a_deadline_moved_before_turns_it_late
  check "Dan, made an owner, reads the same numbers without reports.view" an_owner_reads_the_same_numbers
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
