#!/bin/sh
# Acceptance checks for the deadline alerts (group 17 in the project's Tasks.md; docs/SPEC.md §4.3
# "Background service", §4.4 "Notificările", §9, §13.3; docs/UI_UX_SPEC.md §7.6). The tests and specs hold
# the rules, so these checks run them, and add what they can't see: the real API process on a real
# PostgreSQL, its worker and its answers, and the frontend's production build.
#
# Usage: scripts/check-alerts.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  the worker and NotifyOverdueTasksCommand: the tests, then the real API in Production with the
#      shortest interval, a minute: its check at start, one alert per person on an overdue task, its
#      assignees and owners, nobody else, nothing for a task done, archived, not yet due or in an archived
#      project, an assignee added later alerted at the next run and nobody twice, an interval under a
#      minute stopping the API at its start, and the check turned off. Docker, PostgreSQL.
#   2  the notifications' endpoints: the tests, then the real API in Production on notifications that a
#      comment, two assignments and the overdue check made, for people of its own: the caller's own, a Page of 20 from the
#      newest, the filters unread and type, 400 on a wrong query, an outsider's none, the unread count,
#      read twice keeping the first time, another's 404 Notification.NotFound, read-all counting what it
#      marked. Docker, PostgreSQL.
#   3  the bell, its menu and the notifications page: specs, lint, formatting, the production build
#      without warnings but the budget's, the texts of docs/UI_UX_SPEC.md §7.6, Realtime reached only
#      through import(), SignalR out of the initial bundle in a chunk the board and the bell share. Only
#      the packages that scripts/setup.sh installs.
#   4  the group's flows end to end: the API's tests (once per task and person, a later assignee, what isn't
#      overdue, the alert on the user's own group, read and read-all, a task the user no longer sees),
#      then the real API with the check every minute: an alert heard by its people over a real
#      WebSocket, connected as the bell connects, without a board, and by no member or outsider. The
#      script writes a small SignalR client in Node, as check-realtime.sh does. Docker, PostgreSQL.
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database and
# drops it. Docker runs the tests' own PostgreSQL.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
# In the full gate, the build, tests and lint scripts/gate-prepare.sh did once (docs/SPEC.md §14.7).
[ -n "${KANABOL_GATE_CACHE:-}" ] && . scripts/gate-reuse.sh

failures=0
scratch=$(mktemp -d)
check_db="kanabol_alerts_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5291
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

# Without the check in the background: the setup of a check, which wants its data in place first.
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

# Project ALR, owned by the admin, with Dan and Ioana as members; Eva is from outside.
team_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready ioana ioana@example.com 'Ioana Marin' || return 1
  colleague_is_ready eva eva@example.com 'Eva Din Afara' || return 1
  [ -n "${alr_id:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Alerte","key":"ALR"}')" || return 1
  alr_id=$(body_field id)
  for member in "$dan_id" "$ioana_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$alr_id/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  done_column=$(in_db "SELECT id FROM board_columns WHERE project_id = '$alr_id' AND kind = 'Done'")
  [ -n "$done_column" ]
}

# A task of the admin's in a project, its id in new_task.
a_task_in() { # a_task_in <project id> <title> <JSON fields to add>
  status_is 201 "$(as "$admin_token" POST "/api/projects/$1/tasks" "{\"title\":\"$2\",\"priority\":\"Medium\",$3}")" || return 1
  new_task=$(body_field id)
}

# Each person told that the task is overdue, by name, sorted, with the message: name:message|...
# upper() of display_name follows its ro-x-icu collation, as the API's sorts do (docs/SPEC.md §5.3).
overdue_alerts_of() { # overdue_alerts_of <task id>
  in_db "SELECT u.display_name || ':' || n.message FROM notifications n JOIN users u ON u.id = n.user_id
    WHERE n.task_id = '$1' AND n.type = 'TaskOverdue' ORDER BY upper(u.display_name)" | tr '\n' '|' | sed 's/|$//'
}

overdue_alert_count() {
  in_db "SELECT count(*) FROM notifications WHERE type = 'TaskOverdue'"
}

wait_for() { # wait_for <seconds> <command...>: until the command succeeds, or the time is up
  limit=$1
  shift
  waited=0
  until "$@" >/dev/null 2>&1; do
    waited=$((waited + 1))
    if [ "$waited" -gt "$((limit * 2))" ]; then
      "$@"
      return 1
    fi
    sleep 0.5
  done
}

# ---- 17.1: the worker and NotifyOverdueTasksCommand.

alerts_app=backend/src/Kanabol.Application/Features/Notifications/Commands/NotifyOverdueTasks
alerts_infra=backend/src/Kanabol.Infrastructure

command_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~OverdueTaskAlertsTests' 14
}

worker_and_options_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~OverdueTasksWorkerTests|FullyQualifiedName~AlertsOptionsTests' 14
}

the_api_tests_run_without_the_worker() {
  named_tests_pass Kanabol.Api.Tests ApiFactoryTests.Alerts_InTheseTests_AreOffWithTheDefaultInterval
}

# docs/SPEC.md §4.3: every 15 minutes, on, written out here so a change to it shows.
appsettings_holds_the_defaults() {
  is '00:15:00|true' "$(node -e "
    const settings = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'));
    console.log(settings.Alerts.OverdueCheckInterval + '|' + settings.Alerts.OverdueCheckEnabled);" \
    "$(native backend/src/Kanabol.Api/appsettings.json)")"
}

# docs/SPEC.md §13.3 writes the comment compose holds, word for word.
compose_says_how_to_change_the_check() {
  comment='# The overdue check runs every 15 minutes by default: Alerts__OverdueCheckInterval=00:05:00 changes it, Alerts__OverdueCheckEnabled=false stops it (group 17)'
  in_file "$comment" docs/SPEC.md && in_file "$comment" compose.yaml
}

# The data, made while the check is off: in ALR, task 1 overdue for Dan, task 2 overdue but done, task 3
# overdue but archived, task 4 not yet due, task 5 without a deadline; project OLD, archived since, with
# an overdue task for Dan.
overdue_tasks_are_ready() {
  team_is_ready || return 1
  [ -n "${overdue_task:-}" ] && return 0
  a_task_in "$alr_id" 'Login SSO' "\"dueDate\":\"$past\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  overdue_task=$new_task
  a_task_in "$alr_id" 'Gata deja' "\"dueDate\":\"$past\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  done_task=$new_task
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$done_task/position" "{\"columnId\":\"$done_column\"}")" || return 1
  a_task_in "$alr_id" 'Arhivat' "\"dueDate\":\"$past\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  archived_task=$new_task
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$archived_task")" || return 1
  a_task_in "$alr_id" 'Mai tarziu' "\"dueDate\":\"$future\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  later_task=$new_task
  a_task_in "$alr_id" 'Fara deadline' "\"assigneeIds\":[\"$dan_id\"]" || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Vechi","key":"OLD"}')" || return 1
  old_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$old_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  a_task_in "$old_id" 'Din proiect arhivat' "\"dueDate\":\"$past\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  old_task=$new_task
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$old_id")" || return 1
  is 0 "$(overdue_alert_count)"
}

alerts_came() { # alerts_came <count>
  is "$1" "$(overdue_alert_count)"
}

# The API with the check on, every minute, the shortest interval: its first run comes at start.
the_check_runs_at_start() {
  overdue_tasks_are_ready || return 1
  start_api Production Alerts__OverdueCheckInterval=00:01:00 || return 1
  wait_for 30 alerts_came 2 || return 1
  # Production logs compact JSON: the template, then its values.
  grep -qF '"@mt":"The overdue check runs now and every {OverdueCheckInterval}","OverdueCheckInterval":"00:01:00"' "$api_log" &&
    grep -qF '"NotificationCount":2,"TaskCount":1' "$api_log" || {
    tail -n 20 "$api_log"
    return 1
  }
}

# The admin, as the owner, and Dan, as the assignee; not Ioana, a member, nor Eva, from outside.
the_overdue_task_alerts_its_assignee_and_owner_once() {
  is "Administrator:ALR-1: Login SSO is overdue|Dan Ionescu:ALR-1: Login SSO is overdue" "$(overdue_alerts_of "$overdue_task")"
}

nothing_for_a_task_done_archived_or_not_due() {
  is 0 "$(in_db "SELECT count(*) FROM notifications WHERE type = 'TaskOverdue' AND task_id <> '$overdue_task'")"
}

# Ioana joins the task, and its deadline goes away and passes again: at the next run, within a minute,
# only Ioana is told, and nobody twice.
the_next_run_alerts_an_assignee_added_later_only() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$overdue_task/assignees" "{\"assigneeIds\":[\"$dan_id\",\"$ioana_id\"]}")" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$overdue_task/due-date" "{\"dueDate\":\"$future\"}")" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$overdue_task/due-date" "{\"dueDate\":\"$past\"}")" || return 1
  wait_for 90 alerts_came 3 || return 1
  is "Administrator:ALR-1: Login SSO is overdue|Dan Ionescu:ALR-1: Login SSO is overdue|Ioana Marin:ALR-1: Login SSO is overdue" \
    "$(overdue_alerts_of "$overdue_task")" || return 1
  grep -qF '"NotificationCount":1,"TaskCount":1' "$api_log"
}

# A run that fails is logged and the next one comes, so a quiet log is what says none failed.
no_run_failed() {
  lacks '"@mt":"The overdue check failed' "$api_log"
}

# An interval under a minute stops the API at once, saying why.
an_interval_under_a_minute_refuses_to_start() {
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  launch_api "$scratch/short.log" Production Alerts__OverdueCheckInterval=00:00:30
  pid=$started_pid
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
  grep -q 'Alerts:OverdueCheckInterval must be at least 00:01:00' "$scratch/short.log" || {
    tail -n 20 "$scratch/short.log"
    return 1
  }
  if curl -s -o /dev/null "$API_URL/health"; then
    echo "something answers on $API_URL"
    return 1
  fi
}

# Alerts__OverdueCheckEnabled=false: the API runs, says so, and a new overdue task alerts nobody.
the_check_turned_off_alerts_nobody() {
  start_api Production Alerts__OverdueCheckEnabled=false || return 1
  a_task_in "$alr_id" 'Cu verificarea oprita' "\"dueDate\":\"$past\",\"assigneeIds\":[\"$dan_id\"]" || return 1
  sleep 3
  is 0 "$(in_db "SELECT count(*) FROM notifications WHERE type = 'TaskOverdue' AND task_id = '$new_task'")" || return 1
  grep -qF '"@mt":"The overdue check is turned off"' "$api_log"
}

task_1() {
  echo "17.1 The worker and NotifyOverdueTasksCommand"
  check "Docker is running" docker_is_running
  check "the command is tracked" tracked "$alerts_app/NotifyOverdueTasksCommand.cs"
  check "its handler is tracked" tracked "$alerts_app/NotifyOverdueTasksCommandHandler.cs"
  check "the worker is tracked" tracked "$alerts_infra/Alerts/OverdueTasksWorker.cs"
  check "its options are tracked" tracked "$alerts_infra/Configuration/AlertsOptions.cs"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the command's tests pass: who is told, once per task and person, what isn't overdue" command_tests_pass
  check "the worker's and the options' tests pass: at start, every interval, a failure logged, a minute at least" worker_and_options_tests_pass
  check "the API's tests run with the check off" the_api_tests_run_without_the_worker
  check "appsettings.json checks every 15 minutes, on" appsettings_holds_the_defaults
  check "compose says how to change the check, as docs/SPEC.md §13.3 writes it" compose_says_how_to_change_the_check
  check "with the check off, the overdue tasks are in place and nobody is told" overdue_tasks_are_ready
  check "the API with a check every minute runs it at its start" the_check_runs_at_start
  check "the overdue task tells its assignee and the owner, once each, in English" the_overdue_task_alerts_its_assignee_and_owner_once
  check "nothing for a task done, archived, not yet due, without a deadline or in an archived project" nothing_for_a_task_done_archived_or_not_due
  check "the next run, within a minute, tells an assignee added later, and nobody twice" the_next_run_alerts_an_assignee_added_later_only
  check "no run of the check failed" no_run_failed
  check "an interval under a minute stops the API at its start, saying why" an_interval_under_a_minute_refuses_to_start
  check "the check turned off tells nobody, and says so" the_check_turned_off_alerts_nobody
  check "backend/CLAUDE.md says how the check is set" in_file '| `Alerts__OverdueCheckInterval` ' backend/CLAUDE.md
}

# ---- 17.2: the notifications' endpoints.

notifications_app=backend/src/Kanabol.Application/Features/Notifications

notification_handler_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~NotificationHandlerTests' 17
}

notification_domain_tests_pass() {
  tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~Notifications.NotificationTests' 5
}

notification_endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~NotificationsEndpointTests' 16
}

notifications_need_no_permission() {
  named_tests_pass Kanabol.Api.Tests EndpointPermissionsTests.Notifications_NeedNoPermissionOnTheEndpoint
}

notifications_as() { # notifications_as <token> [query]: prints the status, the page in the last answer
  as "$1" GET "/api/notifications${2:+?$2}"
}

# With the check off, the admin creates NOT-1 for Nora, Ilie comments on it, and the admin creates NOT-2,
# overdue, for Nora; then the API starts with the check on, which tells Nora and the admin of NOT-2. Nora has
# four, from the newest: NOT-2 overdue, NOT-2 assigned, the comment on NOT-1, NOT-1 assigned.
notifications_are_ready() {
  team_is_ready || return 1
  colleague_is_ready nora nora@example.com 'Nora Pop' || return 1
  colleague_is_ready ilie ilie@example.com 'Ilie Marin' || return 1
  [ -n "${not_one:-}" ] && return 0
  api_is_up || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Notificari","key":"NOT"}')" || return 1
  not_id=$(body_field id)
  for member in "$nora_id" "$ilie_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$not_id/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  a_task_in "$not_id" 'Login SSO' "\"assigneeIds\":[\"$nora_id\"]" || return 1
  not_one=$new_task
  status_is 201 "$(as "$ilie_token" POST "/api/tasks/$not_one/comments" '{"body":"Merge și pe mobil"}')" || return 1
  a_task_in "$not_id" 'Export CSV' "\"dueDate\":\"$past\",\"assigneeIds\":[\"$nora_id\"]" || return 1
  not_two=$new_task
  start_api Production Alerts__OverdueCheckInterval=00:01:00 || return 1
  wait_for 30 not_two_alerts_are 2 && wait_for 30 noras_count_is 4
}

not_two_alerts_are() {
  is "$1" "$(in_db "SELECT count(*) FROM notifications WHERE type = 'TaskOverdue' AND task_id = '$not_two'")"
}

noras_count_is() {
  is "$1" "$(in_db "SELECT count(*) FROM notifications WHERE user_id = '$nora_id' AND task_id IN ('$not_one', '$not_two')")"
}

noras_list_is_a_page_from_the_newest() {
  notifications_are_ready || return 1
  start_api Production Alerts__OverdueCheckEnabled=false || return 1
  status_is 200 "$(notifications_as "$nora_token")" || return 1
  is '1|20|4' "$(json "[b.page, b.pageSize, b.totalCount].join('|')")" || return 1
  is 'TaskOverdue:NOT-2:Export CSV|TaskAssigned:NOT-2:Export CSV|CommentAdded:NOT-1:Login SSO|TaskAssigned:NOT-1:Login SSO' \
    "$(json "b.items.map(n => [n.type, n.task.key, n.task.title].join(':')).join('|')")"
}

each_comes_with_its_fields() {
  status_is 200 "$(notifications_as "$nora_token")" || return 1
  is "NOT-2: Export CSV is overdue|$not_two|null|true" \
    "$(json "[b.items[0].message, b.items[0].task.id, String(b.items[0].readAt), !isNaN(Date.parse(b.items[0].createdAt))].join('|')")" || return 1
  is 'Ilie Marin commented on NOT-1: Login SSO|Administrator assigned you NOT-1: Login SSO' "$(json "[b.items[2].message, b.items[3].message].join('|')")"
}

the_filters_and_the_page_hold() {
  status_is 200 "$(notifications_as "$nora_token" 'unread=true&type=CommentAdded')" || return 1
  is 'CommentAdded:1' "$(json "b.items.map(n => n.type).join() + ':' + b.totalCount")" || return 1
  status_is 200 "$(notifications_as "$nora_token" 'type=TaskOverdue&type=CommentAdded&page=2&pageSize=1')" || return 1
  is 'CommentAdded:2' "$(json "b.items.map(n => n.type).join() + ':' + b.totalCount")"
}

wrong_queries_are_400_on_their_field() {
  for pair in 'type=Unknown:type' 'pageSize=101:pageSize' 'page=0:page' 'unread=poate:unread'; do
    query=${pair%:*}
    field=${pair##*:}
    status_is 400 "$(notifications_as "$nora_token" "$query")" || return 1
    is "$field" "$(json "Object.keys(b.errors).join()")" || return 1
  done
}

an_outsider_sees_none_of_noras() {
  status_is 200 "$(notifications_as "$eva_token")" || return 1
  is 0 "$(json "b.totalCount")"
}

unread_count_is() { # unread_count_is <count> <token>
  status_is 200 "$(as "$2" GET /api/notifications/unread-count)" || return 1
  is "{\"count\":$1}" "$(cat "$scratch/body")"
}

noras_unread_count_is_four() {
  unread_count_is 4 "$nora_token"
}

# Nora reads the comment's notification, twice: the first time stays, and the count goes down by one.
reading_one_is_idempotent() {
  status_is 200 "$(notifications_as "$nora_token" 'type=CommentAdded')" || return 1
  comment_notification=$(json "b.items[0].id")
  task_updated=$(in_db "SELECT updated_at FROM task_items WHERE id = '$not_one'")
  status_is 200 "$(as "$nora_token" POST "/api/notifications/$comment_notification/read")" || return 1
  first_read=$(json "b.readAt")
  [ "$first_read" != null ] || return 1
  status_is 200 "$(as "$nora_token" POST "/api/notifications/$comment_notification/read")" || return 1
  is "$first_read" "$(json "b.readAt")" || return 1
  is "$comment_notification|NOT-1" "$(json "b.id + '|' + b.task.key")" || return 1
  unread_count_is 3 "$nora_token" || return 1
  is "$task_updated" "$(in_db "SELECT updated_at FROM task_items WHERE id = '$not_one'")"
}

# Eva can't read Nora's, which stays unread.
anothers_is_404() {
  status_is 200 "$(notifications_as "$nora_token" 'unread=true')" || return 1
  noras=$(json "b.items[0].id")
  code_is 404 Notification.NotFound "$(as "$eva_token" POST "/api/notifications/$noras/read")" || return 1
  is '' "$(in_db "SELECT read_at FROM notifications WHERE id = '$noras'")"
}

read_all_counts_what_it_marked() {
  status_is 200 "$(as "$nora_token" POST /api/notifications/read-all)" || return 1
  is '{"count":3}' "$(cat "$scratch/body")" || return 1
  status_is 200 "$(as "$nora_token" POST /api/notifications/read-all)" || return 1
  is '{"count":0}' "$(cat "$scratch/body")" || return 1
  unread_count_is 0 "$nora_token" || return 1
  is 2 "$(in_db "SELECT count(*) FROM notifications WHERE user_id = '$admin_id' AND read_at IS NULL AND task_id IN ('$not_one', '$not_two')")"
}

without_a_token_is_401() {
  status_is 401 "$(send GET /api/notifications)" &&
    status_is 401 "$(send GET /api/notifications/unread-count)" &&
    status_is 401 "$(send POST /api/notifications/read-all)"
}

task_2() {
  echo "17.2 The notifications' endpoints"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked backend/src/Kanabol.Api/Controllers/NotificationsController.cs
  for file in Queries/GetNotifications/GetNotificationsQueryHandler.cs Queries/GetUnreadNotificationCount/GetUnreadNotificationCountQueryHandler.cs \
    Commands/MarkNotificationRead/MarkNotificationReadCommandHandler.cs Commands/MarkAllNotificationsRead/MarkAllNotificationsReadCommandHandler.cs; do
    check "$file is tracked" tracked "$notifications_app/$file"
  done
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the domain's tests pass: read once, the first time kept" notification_domain_tests_pass
  check "the handlers' tests pass: the caller's own, the filters, the pages, read and read-all" notification_handler_tests_pass
  check "the endpoints' tests pass" notification_endpoint_tests_pass
  check "the endpoints need no permission" notifications_need_no_permission
  check "a comment, two assignments and an overdue check make Nora four notifications" notifications_are_ready
  check "Nora's list is a page of 20 from the newest, each with its task" noras_list_is_a_page_from_the_newest
  check "each comes with its message, its task, its time and no readAt" each_comes_with_its_fields
  check "unread, type repeated, page and pageSize hold" the_filters_and_the_page_hold
  check "an unknown type, a page out of range or a wrong unread is 400 on its field" wrong_queries_are_400_on_their_field
  check "an outsider sees none of Nora's" an_outsider_sees_none_of_noras
  check "Nora's unread count is 4" noras_unread_count_is_four
  check "reading one twice keeps the first time, counts one less and leaves the task alone" reading_one_is_idempotent
  check "someone else's is 404 Notification.NotFound, and stays unread" anothers_is_404
  check "read-all answers how many it marked, then none, and leaves the admin's" read_all_counts_what_it_marked
  check "without a token they are 401" without_a_token_is_401
  check "backend/CLAUDE.md lists the endpoints" in_file '`POST /api/notifications/read-all`' backend/CLAUDE.md
}

# ---- 17.3: the bell, its menu and the notifications page.

web=frontend
app=$web/src/app
bell_web=$app/core/notifications
page_web=$app/features/notifications

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
  (cd "$web" && npx ng build --output-path "$scratch/dist") >"$scratch/build.log" 2>&1 || {
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

the_bell_specs_name_the_rules() {
  spec_names "$bell_web/notification-bell.spec.ts" \
    'reads the unread count once signed in, and shows it on the bell' \
    'hides the badge at 0, and shows 99+ over 99' \
    "opens the hub for the user's own group, without joining any project" \
    'reads the count again at each NotificationCreated and each reconnection' \
    'reads the last 10 when the menu opens, quietly, from the newest' \
    'reads the list again each time it opens, and at NotificationCreated only while open' \
    'marks a row read and opens its task' \
    'marks all of them read, with the menu still open, then reads count and list again' \
    'forgets the count and the list when the session ends, and stops listening' \
    'reads nothing while the password has to change, as the API would refuse it' &&
    spec_names "$bell_web/notification-text.spec.ts" \
      'says a task is past its deadline' \
      'says the user was assigned, without who assigned them' \
      'says there is a new comment, without its author' \
      'writes the badge: nothing at 0, the number up to 99, then 99+' &&
    spec_names "$page_web/notifications-page.spec.ts" \
      'reads the first page of 50, every type, read or not' \
      'takes the filters from the address: unread=1 and type=' \
      'marks a row read and opens its task, and tells the bell' \
      'says "Nicio notificare." without filters' \
      'says "Nicio notificare pentru filtrele alese." with them, and offers to clear them' \
      'says it could not read them, and tries again' &&
    spec_names "$app/core/realtime/realtime.spec.ts" \
      'opens the connection for the bell without joining a project, once'
}

# docs/UI_UX_SPEC.md §7.6, word for word where it gives the words.
the_texts_follow_the_ui_spec() {
  for words in '${subject}:subject: a depășit deadline-ul' 'Ai fost asignat pe ${subject}:subject:' 'Comentariu nou pe ${subject}:subject:' \
    "TaskOverdue: 'event_busy'" "TaskAssigned: 'person_add'" "CommentAdded: 'chat_bubble'" "count > 99 ? '99+'"; do
    in_file "$words" "$bell_web/notification-text.ts" || {
      echo "no '$words' in notification-text.ts"
      return 1
    }
  done
  for words in 'Marchează toate ca citite' 'Nicio notificare.' 'Vezi toate' 'routerLink="/notifications"' 'kb-menu kb-notifications-menu'; do
    in_file "$words" "$bell_web/notification-bell.ts" || {
      echo "no '$words' in notification-bell.ts"
      return 1
    }
  done
  for words in 'Doar necitite' 'Toate' 'Marchează toate ca citite' 'Nicio notificare.' 'Nicio notificare pentru filtrele alese.' 'Nu am putut încărca notificările.' 'Reîncearcă'; do
    grep -qF -- "$words" "$page_web/notifications-page.html" "$page_web/notifications-page.ts" || {
      echo "no '$words' in the page"
      return 1
    }
  done
  in_file 'export const BELL_MENU_SIZE = 10;' "$bell_web/bell-store.ts" &&
    in_file 'const DEFAULT_PAGE_SIZE = 50;' "$page_web/notifications-page.ts" &&
    in_file "params.get('unread') === '1'" "$page_web/notifications-page.ts" &&
    in_file "params.get('type')" "$page_web/notifications-page.ts" &&
    in_file 'width: 400px;' "$web/src/styles/_utilities.scss" &&
    in_file 'class="badge kb-badge"' "$bell_web/notification-bell.ts" &&
    in_file 'background: var(--kb-fill-accent);' "$web/src/styles/_utilities.scss" &&
    in_file 'background: var(--kb-tint-100);' "$bell_web/notification-bell.ts" &&
    in_file 'background: var(--kb-accent-600);' "$bell_web/notification-line.ts" &&
    lacks '#[0-9a-fA-F]{3,8}\b|::ng-deep|\.mat-mdc-|\.mdc-|innerHTML|bypassSecurityTrust' \
      "$bell_web"/*.ts "$page_web"/*.ts "$page_web/notifications-page.scss"
}

the_page_has_its_route() {
  in_file "import('./features/notifications/notifications.routes')" "$app/app.routes.ts" &&
    lacks "placeholder\('notifications'" "$app/app.routes.ts" &&
    in_file '<kb-notification-bell />' "$app/core/layout/navbar.ts"
}

# docs/SPEC.md §9: the bell reaches Realtime only through import(), and nothing the first bundle loads
# imports it; the board's stores do, in its own chunk, Arhivate's since group 22.
realtime_comes_only_through_import() {
  in_file "await import('../realtime/realtime')" "$bell_web/bell-store.ts" || return 1
  static=$(grep -rlE "from '[./]*(core/)?realtime/realtime'|from '\./realtime'" "$app" --include='*.ts' |
    grep -vE '\.spec\.ts$|realtime-testing\.ts$|/realtime/realtime\.ts$' | sed "s|$app/||" | sort | tr '\n' ' ' | sed 's/ $//')
  is 'features/board/archived-store.ts features/board/board-store.ts' "$static"
}

# The initial bundle is what index.html loads: its scripts and the chunks it preloads.
initial_files() {
  grep -oE '<script src="[^"]*"|<link rel="modulepreload" href="[^"]*"' "$scratch/dist/browser/index.html" |
    sed 's/.*"\([^"]*\)"$/\1/'
}

# SignalR's own names, which the minifier keeps.
signalr_marker='WebSocketTransport|HubConnectionBuilder|negotiateVersion'

# The bell in the first bundle, SignalR out of it, in one chunk that the board's chunk and the bell's
# import() share.
signalr_stays_out_of_the_initial_bundle() {
  built || return 1
  initial=$(initial_files)
  [ -n "$initial" ] || {
    echo "index.html loads nothing"
    return 1
  }
  bell_found=
  for file in $initial; do
    if grep -qE "$signalr_marker" "$scratch/dist/browser/$file"; then
      echo "$file, in the initial bundle, holds SignalR"
      return 1
    fi
    grep -q 'kb-notification-bell' "$scratch/dist/browser/$file" && bell_found=yes
  done
  [ -n "$bell_found" ] || {
    echo "the bell isn't in the initial bundle"
    return 1
  }
  holders=$(grep -lE "$signalr_marker" "$scratch"/dist/browser/*.js | sed 's|.*/||' | tr '\n' ' ' | sed 's/ $//')
  [ -n "$holders" ] && [ "$(printf '%s\n' $holders | wc -l)" -eq 1 ] || {
    echo "SignalR is in: ${holders:-no file}"
    return 1
  }
  importers=$(grep -lF "$holders" "$scratch"/dist/browser/*.js | sed 's|.*/||' | tr '\n' ' ')
  [ "$(printf '%s\n' $importers | wc -l)" -ge 2 ] || {
    echo "only $importers load $holders"
    return 1
  }
}

task_3() {
  echo "17.3 The bell, its menu and the notifications page"
  for file in models.ts notifications-api.ts notification-text.ts notification-line.ts bell-store.ts notification-bell.ts \
    notification-bell.spec.ts notification-text.spec.ts notifications-api.spec.ts notifications-testing.ts; do
    check "core/notifications/$file is tracked" tracked "$bell_web/$file"
  done
  for file in notifications-page.ts notifications-page.html notifications-page.scss notifications-store.ts notifications.routes.ts notifications-page.spec.ts; do
    check "features/notifications/$file is tracked" tracked "$page_web/$file"
  done
  check "the specs name the rules: the badge, the hub, the menu, the page, the session" the_bell_specs_name_the_rules
  check "the specs of the bell, its texts, the API, Realtime and the layout pass" specs_pass 'src/app/core/{notifications,realtime,layout}/*.spec.ts' 81
  check "the page's specs pass" specs_pass 'src/app/features/notifications/*.spec.ts' 13
  check "the texts, icons, sizes and colors follow docs/UI_UX_SPEC.md §7.6, nothing unsafe" the_texts_follow_the_ui_spec
  check "/notifications is the page, and the navbar holds the bell" the_page_has_its_route
  check "Realtime comes to the bell only through import()" realtime_comes_only_through_import
  check "the lint is clean" frontend_lint_is_clean
  check "the formatting is clean" frontend_formatting_is_clean
  check "the production build passes" built
  check "the build warns only of the budget" the_build_warns_only_of_the_budget
  check "SignalR stays out of the initial bundle, in a chunk the board and the bell share" signalr_stays_out_of_the_initial_bundle
  check "frontend/CLAUDE.md says how the bell works" in_file '`kb-notification-bell`' frontend/CLAUDE.md
}

# ---- A SignalR client as a browser is one: the JSON protocol over a WebSocket, the token only in the
# query string, as check-realtime.sh's. Built once, on first use.

hub_client() {
  [ -f "$scratch/hub.mjs" ] && return 0
  cat >"$scratch/hub.mjs" <<'JS'
// node hub.mjs <API address> <script file>: runs the script's steps and writes what happened as JSON.
// A step connects a name with a token, as the bell does, without joining any project; sends a request to
// the API; or waits until a name heard so many messages of an event, or the time is up. What each
// connection heard comes last.
import { readFileSync } from 'node:fs';

const RS = '\x1e';
const [base, scriptFile] = process.argv.slice(2);
const steps = JSON.parse(readFileSync(scriptFile, 'utf8'));

async function connect(token) {
  const query = `access_token=${encodeURIComponent(token)}`;
  const negotiated = await fetch(`${base}/hubs/board/negotiate?negotiateVersion=1&${query}`, { method: 'POST' });
  if (negotiated.status !== 200) throw new Error(`negotiate answered ${negotiated.status}`);
  const { connectionId, connectionToken } = await negotiated.json();
  const socket = new WebSocket(`${base.replace(/^http/, 'ws')}/hubs/board?id=${encodeURIComponent(connectionToken)}&${query}`);
  const heard = [];
  let shake;
  const shaken = new Promise((resolve, reject) => (shake = { resolve, reject }));
  socket.onmessage = (event) => {
    for (const frame of String(event.data).split(RS).filter(Boolean)) {
      const message = JSON.parse(frame);
      if (!('type' in message)) {
        if (message.error) shake.reject(new Error(message.error));
        else shake.resolve();
      } else if (message.type === 1) {
        heard.push({ event: message.target, payload: message.arguments[0] });
      } else if (message.type === 7) {
        heard.push({ event: '(closed)', payload: message.error ?? null });
      }
    }
  };
  await new Promise((resolve, reject) => {
    socket.onopen = resolve;
    socket.onerror = () => reject(new Error('the WebSocket failed'));
  });
  socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS);
  await shaken;
  return { connectionId, heard, close: () => socket.close() };
}

const hubs = {};
const result = { connections: {}, steps: [], heard: {} };
try {
  for (const step of steps) {
    if (step.connect) {
      try {
        hubs[step.connect] = await connect(step.token);
        result.connections[step.connect] = hubs[step.connect].connectionId;
        result.steps.push('connected');
      } catch (error) {
        result.steps.push(`refused: ${error.message}`);
      }
    } else if (step.request) {
      const answer = await fetch(`${base}${step.request}`, {
        method: step.method,
        headers: { Authorization: `Bearer ${step.token}`, 'Content-Type': 'application/json' },
        body: step.body === undefined ? undefined : JSON.stringify(step.body),
      });
      result.steps.push(String(answer.status));
    } else if (step.until) {
      const started = Date.now();
      const count = () => hubs[step.until].heard.filter((message) => message.event === step.event).length;
      while (count() < step.count && Date.now() - started < step.timeout) {
        await new Promise((resolve) => setTimeout(resolve, 250));
      }
      result.steps.push(count() >= step.count ? 'heard' : `heard ${count()} of ${step.count}`);
    }
  }
  // Whatever else was on its way reaches the sockets.
  await new Promise((resolve) => setTimeout(resolve, 1500));
} finally {
  for (const [name, hub] of Object.entries(hubs)) {
    result.heard[name] = hub.heard;
    hub.close();
  }
}
console.log(JSON.stringify(result));
JS
}

hub() { # hub <steps as JSON>: runs them, and leaves what happened in the scratch folder
  hub_client || return 1
  printf '%s' "$1" >"$scratch/hub-steps.json"
  rm -f "$scratch/hub-result.json"
  node "$(native "$scratch/hub.mjs")" "$API_URL" "$(native "$scratch/hub-steps.json")" >"$scratch/hub-result.json" 2>"$scratch/hub-error.log" || {
    cat "$scratch/hub-error.log"
    return 1
  }
}

heard() { # heard <JavaScript expression on r, the last hub run>: prints its value
  node -e "const r = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/hub-result.json")" "$1"
}

steps_were() { # steps_were <expected steps, separated by |>
  is "$1" "$(heard "r.steps.join('|')")" || {
    cat "$scratch/hub-result.json"
    return 1
  }
}

# ---- 17.4: the group's flows end to end, in the tests and on the real API.

flow_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    OverdueAlertsFlowTests.OverdueTask_TellsItsAssigneeAndTheOwnerOnce_AndNobodyElse \
    OverdueAlertsFlowTests.AssigneeAddedLater_IsToldAtTheNextRun_AndNobodyTwice_EvenAfterTheDeadlinePassesAgain \
    OverdueAlertsFlowTests.TaskDoneArchivedNotDueOrInAnArchivedProject_TellsNobody \
    OverdueAlertsFlowTests.Alert_IsHeardOnTheUsersOwnGroup_WithoutABoard_AndByNoOutsider \
    OverdueAlertsFlowTests.ReadAndReadAll_CountOnlyWhatTheyMarkNow_AndNobodyReadsAnothers \
    OverdueAlertsFlowTests.Alert_StaysInTheList_WhenItsUserNoLongerSeesTheTask
}

# One run on the real API with the check every minute: Radu, Sara (a member) and Eva (from outside) are
# connected as the bell connects, without a board, and so is the admin. The admin creates HUB-1, overdue,
# for Radu: Radu hears of the assignment at once, then, at the next run, Radu and the admin hear of the
# deadline.
the_alert_runs_over_the_hub() {
  team_is_ready || return 1
  [ -n "${hub_project:-}" ] && return 0
  colleague_is_ready radu radu@example.com 'Radu Stan' || return 1
  colleague_is_ready sara sara@example.com 'Sara Ene' || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Pe hub","key":"HUB"}')" || return 1
  hub_project=$(body_field id)
  for member in "$radu_id" "$sara_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$hub_project/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  # A fresh start: its own run comes at once, before anyone is connected, and the next one a minute later.
  stop_api
  start_api Production Alerts__OverdueCheckInterval=00:01:00 || return 1
  hub "[
    {\"connect\":\"admin\",\"token\":\"$admin_token\"},
    {\"connect\":\"radu\",\"token\":\"$radu_token\"},
    {\"connect\":\"sara\",\"token\":\"$sara_token\"},
    {\"connect\":\"eva\",\"token\":\"$eva_token\"},
    {\"request\":\"/api/projects/$hub_project/tasks\",\"method\":\"POST\",\"token\":\"$admin_token\",
     \"body\":{\"title\":\"Login pe hub\",\"priority\":\"Medium\",\"dueDate\":\"$past\",\"assigneeIds\":[\"$radu_id\"]}},
    {\"until\":\"radu\",\"event\":\"NotificationCreated\",\"count\":2,\"timeout\":90000},
    {\"until\":\"admin\",\"event\":\"NotificationCreated\",\"count\":1,\"timeout\":10000}
  ]" || return 1
  steps_were 'connected|connected|connected|connected|201|heard|heard'
  hub_task=$(in_db "SELECT id FROM task_items WHERE project_id = '$hub_project'")
}

notification_ids_heard_by() { # notification_ids_heard_by <name>: sorted, one per line
  heard "r.heard.$1.filter(m => m.event === 'NotificationCreated').map(m => m.payload.notificationId).join('\n')" | tr -d '\r' | sort
}

# Radu hears the ids of his two notifications, the assignment's and the deadline's, each once.
the_assignee_hears_each_of_his_once() {
  the_alert_runs_over_the_hub || return 1
  is "$(in_db "SELECT id FROM notifications WHERE user_id = '$radu_id' AND task_id = '$hub_task' ORDER BY id" | sort)" \
    "$(notification_ids_heard_by radu)" || return 1
  is 'TaskAssigned,TaskOverdue' "$(in_db "SELECT string_agg(type, ',' ORDER BY type) FROM notifications WHERE user_id = '$radu_id' AND task_id = '$hub_task'")"
}

the_payload_is_the_id_only() {
  the_alert_runs_over_the_hub || return 1
  is 'notificationId' "$(heard "[...new Set(r.heard.radu.map(m => Object.keys(m.payload).join()))].join('|')")"
}

# The admin, the owner, hears of the deadline only: nobody is told of an assignment they made.
the_owner_hears_the_deadline_only() {
  the_alert_runs_over_the_hub || return 1
  is "$(in_db "SELECT id FROM notifications WHERE user_id = '$admin_id' AND task_id = '$hub_task' AND type = 'TaskOverdue'")" \
    "$(notification_ids_heard_by admin)"
}

nobody_else_hears_a_thing() {
  the_alert_runs_over_the_hub || return 1
  is 0 "$(heard "r.heard.sara.length + r.heard.eva.length")"
}

the_bell_reads_them_after() {
  the_alert_runs_over_the_hub || return 1
  status_is 200 "$(as "$radu_token" GET /api/notifications/unread-count)" || return 1
  is '{"count":2}' "$(cat "$scratch/body")" || return 1
  status_is 200 "$(as "$radu_token" GET '/api/notifications?pageSize=10')" || return 1
  is 'TaskOverdue:HUB-1|TaskAssigned:HUB-1' "$(json "b.items.map(n => n.type + ':' + n.task.key).join('|')")"
}

task_4() {
  echo "17.4 The group's flows end to end, and an alert heard on the bell's hub connection"
  check "Docker is running" docker_is_running
  check "the flows' tests pass: once per task and person, a later assignee, what isn't overdue, the hub, read and read-all" flow_tests_pass
  check "the real API with the check every minute tells Radu over his connection, without a board" the_alert_runs_over_the_hub
  check "Radu hears each of his two notifications once, by id" the_assignee_hears_each_of_his_once
  check "the payload is the notification's id only" the_payload_is_the_id_only
  check "the owner hears of the deadline only" the_owner_hears_the_deadline_only
  check "a member and an outsider hear nothing" nobody_else_hears_a_thing
  check "the bell's count and list then hold both" the_bell_reads_them_after
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
