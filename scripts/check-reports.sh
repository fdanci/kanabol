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
#      cycle of the task created in the second column from its creation, the time in each column with a
#      project only; the tasks overdue now whatever the interval, none in an archived project, those
#      finished by their deadline and after it, the rate, null without a deadline, and their periods; 400 on
#      from, to and granularity, 404 for a project that doesn't exist. Docker, PostgreSQL.
#   2  who reads them: the tests, then the real API in Production on the same work, each of the three for
#      reports.view in no project (every project, one, an archived one), an owner without it (their own
#      project, archived too, any person in it; none without a project, 400 on projectId; another's, 403),
#      projects.manage (any project), a plain member, projects.view_all and an outsider (403
#      Project.OwnerRequired), a project that doesn't exist (404), the permission's name only in the log,
#      and an owner deactivated since, whose token still holds (403). Docker, PostgreSQL.
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database and
# drops it. Docker runs the tests' own PostgreSQL.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

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

moved() { # moved <task id> <column id>: by the admin, through the API
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$1/position" "{\"columnId\":\"$2\"}")"
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

columns_of() { # columns_of <project id> <name prefix>: its columns' ids in <prefix>_todo, _doing and _done
  for place in 0:todo 1:doing 2:done; do
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

  a_task_in "$pan_id" 'Pagina de start' "\"priority\":\"Low\",\"columnId\":\"$pan_doing\",\"assigneeIds\":[\"$dan_id\"],\"dueDate\":\"2026-10-13T12:00:00Z\"" &&
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

# Leads: PAN-1 7 d 1 h, PAN-2 12 d, PAN-3 5 d 2 h, MEU-1 7 d, MEU-2 1 d, ARH-1 5 d. Cycles: PAN-1 2 d,
# PAN-2 12 d (created in În lucru, so from its creation), PAN-3 2 d, MEU-1 5 d, MEU-2 0, ARH-1 3 d.
the_lead_and_cycle_times_are_measured() {
  work_is_ready || return 1
  report "$admin_token" cycle-time "$october" || return 1
  is '6 534600/522000 345600/216000 null' "$(json b.count) $(durations leadTime) $(durations cycleTime) $(json b.perColumn)"
}

# PAN-1: De făcut 5 d 1 h, În lucru 2 d; PAN-2: În lucru 12 d; PAN-3: De făcut 3 d 2 h, În lucru 2 d.
each_column_has_its_time_in_a_project() {
  work_is_ready || return 1
  report "$admin_token" cycle-time "$october&projectId=$pan_id" || return 1
  is 'De făcut 351000/351000, În lucru 460800/172800, Gata 0/0' \
    "$(json "b.perColumn.map(c => c.name + ' ' + c.averageSeconds + '/' + c.medianSeconds).join(', ')")" &&
    is "$pan_todo $pan_doing $pan_done" "$(json "b.perColumn.map(c => c.columnId).join(' ')")"
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
  check "lead and cycle time come as averages and medians in seconds, the cycle from the first exit" the_lead_and_cycle_times_are_measured
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

if [ "$#" -eq 0 ]; then
  set -- 1 2
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
