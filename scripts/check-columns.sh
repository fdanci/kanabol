#!/bin/sh
# Acceptance checks for the fixed columns, the estimate, the worked time and the general settings (group 22
# in the project's Tasks.md; docs/SPEC.md §4.4, §5.1, §5.1.1, §13.3, §14.7; docs/UI_UX_SPEC.md §5, §7.4,
# §7.5, §7.8, §7.9). The tests hold the rules, so these checks run them, and add what they can't see: the
# migration over an old database on the local PostgreSQL, the real API process on it, its answers, and one task
# through all of it, as boards hear it over the hub.
#
# Usage: scripts/check-columns.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  the fixed columns and the migration AddColumnKinds: the tests (the domain's columns, the migration
#      over an old database and back, the column handlers and endpoints); then, on the local PostgreSQL, a
#      database migrated up to AddRomanianCollation with an ordinary project, one with two done columns,
#      one with a single column and one with columns of its own, which the real API in Production migrates
#      at start: Backlog, În lucru, În pauză and Gata in each, the project's own kept between them, every
#      task in its column, the one finished in the second done column open again; a new project gets the
#      four fixed columns, and the API speaks of kind and columnKind, never isDone; the fixed ones answer
#      409 Column.Fixed to a rename, a move and a delete, a new column of the project's own comes right
#      before Gata, moves only between Backlog and Gata (400 on position otherwise), can't take a fixed name
#      in any case (400 on name), goes when empty, an isDone sent as before changes nothing, and
#      Column.LastColumn is gone from the code; then the migration back, is_done on Gata and the columns as
#      they are. Docker, PostgreSQL.
#   2  the moves: the tests (the order of the columns, the moves and the creations, their endpoints); then the
#      real API in Production: a task starts in Backlog, any other column of the project (the project's own
#      too) or another project's answers 400 on columnId and takes no number; a task in În lucru, În pauză
#      or Gata goes back neither to Backlog nor to a column of the project's own before În lucru (409
#      Task.CannotMoveBack, nothing moved, no history), but goes from Gata to În lucru, open again, and
#      between the columns before În lucru both ways; a column of another project is 400 before the 409. The
#      estimate: a task without one enters În lucru only with one (400 on estimatedMinutes otherwise,
#      nothing moved), 15 to 60,000 minutes, which writes EstimatedMinutes next to the move, at its moment;
#      sent on any other move it's ignored, whatever it holds; optional at creation, without history; PUT
#      …/estimate changes it with its entry, takes it away before În lucru but not after (400), is 403 for
#      one who only sees the project. The pause: every entry into În pauză asks for a reason, one line of 1
#      to 500 characters once trimmed (400 on pauseReason otherwise, nothing moved), which the task keeps
#      and the history writes next to the move, at its moment; leaving forgets it without an entry, a reorder
#      there asks for none, and one sent on any other move is ignored. The column Arhivate: the tests (its
#      handler and endpoint); then on the real API, the board's canSeeArchived for tasks.archive, GET
#      …/board/archived from the latest archiving, each card with its column, who archived it, its estimate
#      and its reason, which the restore keeps, back in its column; 403 Auth.PermissionRequired without
#      tasks.archive, 404 Project.NotFound for a project the caller doesn't see, 400 on pageSize over 100.
#      Docker, PostgreSQL.
#   3  the worked time and the general settings: the tests (the schedule, its handlers, Schedule:TimeZone, the
#      migration's row and back, the endpoints; WorkingTime.Between, the counter in the domain and through the
#      handlers, the migration AddWorkedTime, which starts it for the open tasks already in În lucru, and the
#      DTOs' fields); then the real API in Production: settings.manage seeded and the Admin's, GET
#      /api/settings/work-schedule for anyone signed in (Monday to Friday, 08:30–16:30, Europe/Bucharest),
#      PUT for settings.manage only (403 Auth.PermissionRequired otherwise), 400 on the field that's wrong,
#      the change logged with who and the schedule before and after; with every hour of every day in the
#      schedule, a task entering În lucru starts counting, its GET, the board and the list give isWorking and
#      the seconds up to now, which only leaving stores, without history; with today out of the schedule,
#      back in În lucru it counts nothing, the archive stops it and the restore starts it again, the seconds
#      kept; Schedule__TimeZone unknown stops the start, naming it; the API image built from backend/ carries
#      the zone data, starts past the options with Europe/Vienna and stops on an unknown zone; compose.yaml's
#      comment names Schedule__TimeZone. Docker, PostgreSQL, Node.
#   4  the work-time report and the cycle time from În lucru: the tests (GET /api/reports/work-time, the cycle
#      time, who reads them, their endpoints); then the real API in Production: RAP with four tasks moved
#      through the API, their moments put a day ago in the database and their worked time set there: the
#      cycle time from the first entry into În lucru (a task through a column of its own first, one straight
#      from Backlog to Gata from its creation), work-time's sums over the estimated tasks that were worked,
#      the deviation, the overruns, by person and by project; a plain member of RAP gets 403
#      Project.OwnerRequired, a missing from 400. Docker, PostgreSQL.
#   5  the frontend: the fixed columns' bars and icons at 3:1 at least on the column, in light and in dark,
#      computed from _tokens.scss, which has no token since the group's base; the board's fixed columns with
#      their bar and icon, the ⋮ only on the project's own, "+ Task" only in Backlog, the project's own
#      moved only between Backlog and Gata, a started card kept out of the columns before În lucru, by the
#      mouse and the keyboard, and the board's specs; the estimate's sheet before a task without one starts
#      and the pause's before every pause, by the mouse and the keyboard, their texts, the card waiting in its
#      new column while they ask and back in its place when they're turned down, the chip "lucrat / estimat"
#      and the pause's reason on the card, and their specs; Arhivate, for tasks.archive, last, closed at first
#      in a 44px band with its count, open at 300px, as this browser left it, 50 at a time, a card of the
#      board archived by a drop on it after the question, one of its own restored into its column by a drop
#      anywhere or by its ⋮, untouched by the filters, and their specs; the task's panel with "Estimare" edited
#      in place, "Timp lucrat" counting now in În lucru against the estimate, "Motivul pauzei", the "Stare"
#      select asking the board's sheets and keeping a started task from the columns before În lucru, the
#      "Reîncarcă" of Atașamente and Istoric, the history's lines of the estimate and the pause, and their
#      specs; the lists on columnKind; "Setări generale" at /settings for settings.manage, in the sidebar
#      and with its group on the roles screen, its card's texts and specs; the production build without a
#      warning, its Initial total, printed, under 750 kB, and the settings page in a chunk of its own, not
#      in what index.html loads; the reports' fourth section, "Estimare și timp lucrat", with its tiles, the
#      schedule of now under its title, its chart by person in the charts' colors, its tables "Pe om" and
#      "Pe proiect" with their deviation, its CSV and the tasks over their estimate, which open the task, in
#      the reports' chunk, and its specs. The packages only.
#   6  one task from Backlog to Gata: the hub's tests (the board's events, among them the estimate's before its
#      move, their sender, their flows); then the API built from this code, whatever the tests built, in
#      Production: CAP with Dan a plain member, ALT, and every hour of every day worked. Ana's board, the
#      admin's, takes CAP-1, created for Dan, into În lucru, first without an estimate (400 on
#      estimatedMinutes), then with 30 minutes, for 2.5 seconds, on pause with a reason, into the archive,
#      back, into În lucru for 1.5 seconds and into Gata, every request with its connection's id; Dan's board
#      of CAP and the admin's of ALT listen over WebSockets, as a browser does. Both boards of CAP hear every
#      step in the order it went, TaskUpdated of the estimate before its TaskMoved, each by the admin from
#      Ana's connection, nothing of the 400; Dan hears of his assignment once, Ana of nothing, ALT's board
#      nothing at all. The worked time counts only in În lucru and keeps through the pause, the archive and
#      the restore; Arhivate held the task, from În pauză with its reason and its time, and has it no more;
#      the board, the database and the history tell it the same way; work-time counts its estimate and time
#      by Dan and by CAP, cycle-time from În lucru, between the time worked and the lead time. Last,
#      backend/CLAUDE.md and frontend/CLAUDE.md say what the group changed, and nothing of what it took away.
#      Docker, PostgreSQL, Node.
#
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB or
# else in the API's user secrets (ConnectionStrings:Default); each run works in its own database,
# kanabol_columns_old_<pid> for task 1 and kanabol_columns_check_<pid> for the others, and drops them. Docker runs the tests' own PostgreSQL. The real API listens on
# port 5278.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
old_db="kanabol_columns_old_$$"
api_db="kanabol_columns_check_$$"
check_db=$api_db
api_dir=backend/src/Kanabol.Api
api_port=5278
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
base_connection=
created_databases=
admin_token=
before_kinds=20261109202207_AddRomanianCollation

admin_email=admin@kanabol.local
initial_password='o parola initiala de verificare'
admin_password='parola adminului pentru coloane'
jwt_secret=columns-check-secret-with-at-least-32-characters

cleanup() {
  stop_api
  [ -z "${api_image_built:-}" ] || "$docker_cli" image rm -f "kanabol-api:columns-check-$$" >/dev/null 2>&1
  drop_database
  rm -rf "$scratch"
}
trap cleanup EXIT

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

native() { # native <path>: as Windows programs (node, curl) read it
  cygpath -m "$1" 2>/dev/null || echo "$1"
}

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
}

# grep's options may come before the regex or after the paths. Found is a failure, and so is an error of
# grep's own (status 2), so a wrong path or option can't pass for "nothing found".
lacks() { # lacks [grep options...] <extended regex> <file or folder...> [grep options...]
  grep -rnE "$@"
  [ "$?" -eq 1 ]
}

docker_is_running() {
  docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
  "$docker_cli" info --format '{{.ServerVersion}}'
}

# ---- The tests.

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

test_class_exists() { # test_class_exists <project> <class>: some test is in it, so its filter can't run nothing
  dotnet test "backend/tests/$1/$1.csproj" --nologo --no-build --list-tests 2>/dev/null | tr -d '\r' | grep -qF ".$2." || {
    echo "no test of $2 in $1"
    return 1
  }
}

classes_pass() { # classes_pass <project> <minimum passed> <class...>: each has tests, and all of them pass
  project=$1
  minimum=$2
  shift 2
  stop_api
  dotnet build "backend/tests/$project/$project.csproj" --nologo -v q >/dev/null 2>&1 || return 1
  filter=
  for class in "$@"; do
    test_class_exists "$project" "$class" || return 1
    filter="${filter:+$filter|}FullyQualifiedName~.$class."
  done
  tests_pass "$project" "$filter" "$minimum"
}

# ---- A database of this run, on the local PostgreSQL, and the real API, in Production, on it.

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
// The names have ă, î and ț, which the console would turn into a, i and t.
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

# Each task works in the database of this run, made on first use and dropped at the end.
need_database() {
  if [ -z "$admin_connection" ]; then
    base_connection=${KANABOL_CHECK_DB:-$(dotnet user-secrets list --id kanabol-api 2>/dev/null |
      tr -d '\r' | sed -n 's/^ConnectionStrings:Default = //p')}
    if [ -z "$base_connection" ]; then
      echo "No database: set KANABOL_CHECK_DB or the API's ConnectionStrings:Default user secret."
      return 1
    fi
    admin_connection=$(printf '%s' "$base_connection" | sed 's/Database=[^;]*/Database=postgres/')
  fi
  case " $created_databases " in *" $check_db "*) return 0 ;; esac
  sql "$admin_connection" "CREATE DATABASE $check_db" || return 1
  created_databases="$created_databases $check_db"
}

# Task 1 works on an old database it migrates back at the end; the other tasks on one the API makes.
use_database() { # use_database <name>
  [ "$check_db" = "$1" ] && return 0
  stop_api
  admin_token=
  check_db=$1
}

connection_to() { # connection_to <database>
  printf '%s' "$base_connection" | sed "s/Database=[^;]*/Database=$1/"
}

in_db() { # in_db <statement>: its rows, as sql prints them
  sql "$(connection_to "$check_db")" "$1" | tr -d '\r'
}

drop_database() {
  [ -n "$admin_connection" ] || return 0
  for database in $created_databases; do
    sql "$admin_connection" "DROP DATABASE IF EXISTS $database WITH (FORCE)" >/dev/null 2>&1
  done
}

api_builds() {
  stop_api
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q >"$scratch/api-build.log" 2>&1 || {
    tail -n 20 "$scratch/api-build.log"
    return 1
  }
}

ef() { # ef <arguments...>: runs dotnet ef from backend/, against the built projects
  stop_api
  (cd backend && dotnet ef "$@" -p src/Kanabol.Infrastructure -s src/Kanabol.Api --no-build)
}

# The API in Production on this run's database, which it migrates at start, without the overdue check.
api_is_up() {
  # One that died by itself fails the check that finds it, with its log; the next check starts another.
  if [ -n "$api_pid" ]; then
    kill -0 "$api_pid" 2>/dev/null && return 0
    echo "the API stopped by itself:"
    tail -n 30 "$api_log"
    api_pid=
    return 1
  fi
  need_database || return 1
  [ -f "$api_dir/bin/Debug/net10.0/Kanabol.Api.dll" ] || api_builds || return 1
  (
    cd "$api_dir" || exit 1
    # Seq's address leads nowhere, so a developer's own Seq gets nothing of these checks.
    exec env ASPNETCORE_ENVIRONMENT=Production \
      ConnectionStrings__Default="$(connection_to "$check_db")" \
      Jwt__Secret="$jwt_secret" \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword="$initial_password" \
      Alerts__OverdueCheckEnabled=false \
      Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 \
      dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$api_log" 2>&1 &
  api_pid=$!
  attempts=0
  until curl -s -o /dev/null "$API_URL/health"; do
    attempts=$((attempts + 1))
    if [ "$attempts" -gt 120 ] || ! kill -0 "$api_pid" 2>/dev/null; then
      api_pid=
      tail -n 30 "$api_log"
      return 1
    fi
    sleep 0.5
  done
}

stop_api() {
  [ -n "$api_pid" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  rm -f "$scratch/body"
  curl -sS --max-time 30 -o "$scratch/body" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

# Bodies go from a file: curl reads its arguments in the ANSI code page, which turns ă into a.
as() { # as <token> <method> <path> [JSON body]
  if [ $# -ge 4 ]; then
    printf '%s' "$4" >"$scratch/request.json"
    send "$2" "$3" -H "Authorization: Bearer $1" -H 'Content-Type: application/json' --data-binary "@$(native "$scratch/request.json")"
  else
    send "$2" "$3" -H "Authorization: Bearer $1"
  fi
}

body_field() { # body_field <name>: the value of the first text field with that name in the last answer
  sed -n "s/^[^\"]*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body" | head -n 1
}

json() { # json <JavaScript expression on b, the last answer, in ASCII> [argument]: prints its value; the argument is process.argv[3]
  node -e "const b = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/body")" "$@" | tr -d '\r'
}

status_is() { # status_is <expected status> <actual status>: shows the answer when it differs
  is "$1" "$2" || {
    head -c 600 "$scratch/body" 2>/dev/null
    echo
    return 1
  }
}

code_is() { # code_is <status> <code> <actual status>: the status and the ProblemDetails code
  status_is "$1" "$3" || return 1
  is "$2" "$(body_field code)"
}

# A token of an earlier task stays good, but the tests between them stopped the API: it starts again first.
the_admin_signs_in() {
  api_is_up || return 1
  [ -n "$admin_token" ] && return 0
  status_is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.78.0.1' \
    -d "{\"email\":\"$admin_email\",\"password\":\"$initial_password\"}")" || return 1
  status_is 200 "$(send POST /api/auth/change-password -H "Authorization: Bearer $(body_field accessToken)" \
    -H 'Content-Type: application/json' -d "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

columns_of() { # columns_of <project id>: its columns from the left, as name:kind, separated by commas
  status_is 200 "$(as "$admin_token" GET "/api/projects/$1/board-columns")" >/dev/null || return 1
  json "b.map(c => c.name + ':' + c.kind).join(',')"
}

# ---- Task 1: the fixed columns and the migration AddColumnKinds.

column_tests_pass() {
  classes_pass Kanabol.Domain.Tests 15 ProjectColumnTests ProjectTests &&
    classes_pass Kanabol.Application.Tests 25 AddColumnKindsMigrationTests BoardColumnCommandHandlerTests CreateProjectCommandHandlerTests &&
    classes_pass Kanabol.Api.Tests 5 BoardColumnsEndpointTests
}

# Ids end in the project's digit, then the column's place from 1, as in AddColumnKindsMigrationTests.
usual=00000000-0000-7000-8000-000000000001
two_done=00000000-0000-7000-8000-000000000002
single=00000000-0000-7000-8000-000000000003
own=00000000-0000-7000-8000-000000000005

old_column() { # old_column <project id> <place from 1>
  printf '00000000-0000-7000-8000-000000000c%s%s' "$(printf '%s' "$1" | tail -c 1)" "$2"
}

an_old_database_with_four_kinds_of_boards() {
  need_database || return 1
  api_builds || return 1
  ef database update "$before_kinds" --connection "$(connection_to "$check_db")" >"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  in_db "INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES
    ('$usual', 'Obișnuit', 'OBI', 20, now()), ('$two_done', 'Două gata', 'DOI', 20, now()),
    ('$single', 'Una', 'UNA', 20, now()), ('$own', 'Ale lor', 'OWN', 20, now())" || return 1
  in_db "INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES
    ('$(old_column $usual 1)', '$usual', 'De făcut', 0, false, now()),
    ('$(old_column $usual 2)', '$usual', 'În lucru', 1, false, now()),
    ('$(old_column $usual 3)', '$usual', 'Gata', 2, true, now()),
    ('$(old_column $two_done 1)', '$two_done', 'De făcut', 0, false, now()),
    ('$(old_column $two_done 2)', '$two_done', 'În lucru', 1, false, now()),
    ('$(old_column $two_done 3)', '$two_done', 'Gata', 2, true, now()),
    ('$(old_column $two_done 4)', '$two_done', 'Livrat', 3, true, now()),
    ('$(old_column $single 1)', '$single', 'Tot', 0, false, now()),
    ('$(old_column $own 1)', '$own', 'Idei', 0, false, now()),
    ('$(old_column $own 2)', '$own', 'Analiză', 1, false, now()),
    ('$(old_column $own 3)', '$own', 'Review', 2, false, now()),
    ('$(old_column $own 4)', '$own', 'Livrat', 3, true, now())" || return 1
  in_db "INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at, completed_at) VALUES
    (gen_random_uuid(), '$usual', '$(old_column $usual 1)', 1, 'De făcut', 'Low', 0, now(), NULL),
    (gen_random_uuid(), '$usual', '$(old_column $usual 2)', 2, 'În lucru', 'Low', 0, now(), NULL),
    (gen_random_uuid(), '$usual', '$(old_column $usual 3)', 3, 'Gata', 'Low', 0, now(), now()),
    (gen_random_uuid(), '$two_done', '$(old_column $two_done 3)', 1, 'Gata, două', 'Low', 0, now(), now()),
    (gen_random_uuid(), '$two_done', '$(old_column $two_done 4)', 2, 'Livrat', 'Low', 0, now(), now()),
    (gen_random_uuid(), '$single', '$(old_column $single 1)', 1, 'Singur', 'Low', 0, now(), NULL),
    (gen_random_uuid(), '$own', '$(old_column $own 3)', 1, 'În review', 'Low', 0, now(), NULL),
    (gen_random_uuid(), '$own', '$(old_column $own 4)', 2, 'Livrat, al lor', 'Low', 0, now(), now())" || return 1
  in_db "SELECT title, column_id, position FROM task_items ORDER BY title" >"$scratch/tasks-before" || return 1
  is 12 "$(in_db 'SELECT count(*) FROM board_columns')"
}

# AddColumnKinds among the migrations applied, and the newest of the code the last of them.
api_migrates_the_old_database() {
  api_is_up || return 1
  newest=$(ls backend/src/Kanabol.Infrastructure/Persistence/Migrations | sed -n 's/^\([0-9]*_[A-Za-z0-9]*\)\.cs$/\1/p' | sort | tail -n 1)
  is "1|$newest" "$(in_db "SELECT count(*) FILTER (WHERE migration_id LIKE '%_AddColumnKinds'), max(migration_id) FROM \"__EFMigrationsHistory\"")"
}

every_board_has_the_fixed_columns() {
  the_admin_signs_in || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Gata:Done' "$(columns_of "$usual")" || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Livrat:Custom,Gata:Done' "$(columns_of "$two_done")" || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Gata:Done' "$(columns_of "$single")" || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Analiză:Custom,Review:Custom,Gata:Done' "$(columns_of "$own")"
}

every_task_keeps_its_column() {
  in_db "SELECT title, column_id, position FROM task_items ORDER BY title" >"$scratch/tasks-after" || return 1
  diff "$scratch/tasks-before" "$scratch/tasks-after"
}

only_tasks_in_gata_stay_finished() {
  is 'Gata
Gata, două
Livrat, al lor' "$(in_db "SELECT title FROM task_items WHERE completed_at IS NOT NULL ORDER BY title")" || return 1
  # The list says the column's kind, and never isDone.
  status_is 200 "$(as "$admin_token" GET "/api/projects/$two_done/tasks?sort=key:asc")" || return 1
  is 'Gata, două:Done:true Livrat:Custom:false' "$(json "b.items.map(t => t.title + ':' + t.columnKind + ':' + (t.completedAt !== null)).join(' ')")" || return 1
  is undefined "$(json "b.items[0].columnIsDone")"
}

a_new_project_gets_the_four_fixed_columns() {
  the_admin_signs_in || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Nou","key":"NOU"}')" || return 1
  new_project=$(body_field id)
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Gata:Done' "$(json "b.columns.map(c => c.name + ':' + c.kind).join(',')")" || return 1
  is undefined "$(json "b.columns[0].isDone")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$new_project/board")" || return 1
  is 'Backlog Done' "$(json "b.columns[0].kind + ' ' + b.columns[3].kind")"
}

# Each fixed kind once per project: the database itself refuses a second Gata.
the_database_holds_one_of_each() {
  is 'Backlog|5|5
Done|5|5
InProgress|5|5
Paused|5|5' "$(in_db "SELECT kind, count(*), count(DISTINCT project_id) FROM board_columns WHERE kind <> 'Custom' GROUP BY kind ORDER BY kind")" || return 1
  # sql itself, not in_db, whose pipe would hide its status.
  if sql "$(connection_to "$check_db")" "INSERT INTO board_columns (id, project_id, name, position, kind, created_at) VALUES (gen_random_uuid(), '$usual', 'Altă Gata', 9, 'Done', now())" >"$scratch/second-done.log" 2>&1; then
    echo "a second Gata went in"
    return 1
  fi
  grep -q 'ux_board_columns_project_id_kind' "$scratch/second-done.log" || {
    tail -n 5 "$scratch/second-done.log"
    return 1
  }
}

column_id() { # column_id <project id> <name>: the id of that column, as the API gives it
  status_is 200 "$(as "$admin_token" GET "/api/projects/$1/board-columns")" >/dev/null || return 1
  json "(b.find(c => c.name === process.argv[3]) || {}).id" "$2"
}

the_fixed_columns_never_change() {
  the_admin_signs_in && [ -n "${new_project:-}" ] || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$new_project/board-columns")" || return 1
  for column in $(json "b.map(c => c.id).join(' ')"); do
    code_is 409 Column.Fixed "$(as "$admin_token" PUT "/api/projects/$new_project/board-columns/$column" '{"name":"Altceva"}')" || return 1
    code_is 409 Column.Fixed "$(as "$admin_token" PATCH "/api/projects/$new_project/board-columns/$column/position" '{"position":1}')" || return 1
    code_is 409 Column.Fixed "$(as "$admin_token" DELETE "/api/projects/$new_project/board-columns/$column")" || return 1
  done
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Gata:Done' "$(columns_of "$new_project")"
}

a_column_of_its_own_comes_right_before_gata() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$new_project/board-columns" '{"name":"Review"}')" || return 1
  is '3 Custom' "$(json "b.position + ' ' + b.kind")" || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$new_project/board-columns" '{"name":"QA"}')" || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,Review:Custom,QA:Custom,Gata:Done' "$(columns_of "$new_project")"
}

it_moves_only_between_backlog_and_gata() {
  review=$(column_id "$new_project" Review)
  [ -n "$review" ] || return 1
  for place in 0 5 9; do
    status_is 400 "$(as "$admin_token" PATCH "/api/projects/$new_project/board-columns/$review/position" "{\"position\":$place}")" || return 1
    is true "$(json "'position' in b.errors")" || return 1
  done
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$new_project/board-columns/$review/position" '{"position":1}')" || return 1
  is 'Backlog,Review,În lucru,În pauză,QA,Gata' "$(json "b.map(c => c.name).join(',')")" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$new_project/board-columns/$review/position" '{"position":4}')" || return 1
  is 'Backlog,În lucru,În pauză,QA,Review,Gata' "$(json "b.map(c => c.name).join(',')")"
}

the_fixed_names_are_taken() {
  for name in Gata gata Backlog 'ÎN PAUZĂ' 'în lucru'; do
    status_is 400 "$(as "$admin_token" POST "/api/projects/$new_project/board-columns" "{\"name\":\"$name\"}")" || return 1
    is true "$(json "'name' in b.errors")" || return 1
    status_is 400 "$(as "$admin_token" PUT "/api/projects/$new_project/board-columns/$review" "{\"name\":\"$name\"}")" || return 1
  done
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,QA:Custom,Review:Custom,Gata:Done' "$(columns_of "$new_project")"
}

an_empty_column_of_its_own_goes_and_is_done_changes_nothing() {
  status_is 201 "$(as "$admin_token" POST "/api/projects/$new_project/board-columns" '{"name":"Livrat","isDone":true}')" || return 1
  livrat=$(body_field id)
  is Custom "$(json b.kind)" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$new_project/board-columns/$livrat" '{"name":"Livrat","isDone":true}')" || return 1
  is Custom "$(json b.kind)" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$new_project/board-columns/$livrat")" || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,QA:Custom,Review:Custom,Gata:Done' "$(columns_of "$new_project")"
}

last_column_is_gone() {
  lacks 'LastColumn|isDone|IsDone|columnIsDone|ColumnIsDone' backend/src frontend/src --include='*.cs' --include='*.ts' --include='*.html' \
    --exclude-dir=Migrations --exclude-dir=bin --exclude-dir=obj
}

the_migration_goes_back() {
  ef database update "$before_kinds" --connection "$(connection_to "$check_db")" >"$scratch/back.log" 2>&1 || {
    tail -n 15 "$scratch/back.log"
    return 1
  }
  is 'Gata|5' "$(in_db "SELECT name, count(*) FROM board_columns WHERE is_done GROUP BY name")" || return 1
  is 'Backlog,În lucru,În pauză,Livrat,Gata' "$(in_db "SELECT string_agg(name, ',' ORDER BY position) FROM board_columns WHERE project_id = '$two_done'")" || return 1
  is 0 "$(in_db "SELECT count(*) FROM information_schema.columns WHERE table_name = 'board_columns' AND column_name = 'kind'")"
}

task_1() {
  use_database "$old_db"
  echo "22.1 the fixed columns and the migration AddColumnKinds"
  check "Docker is running" docker_is_running
  check "the column tests pass: the domain, the migration and back, the handlers and the endpoints" column_tests_pass
  check "an old database has an ordinary project, two done columns, a single column and columns of its own" an_old_database_with_four_kinds_of_boards
  check "the real API in Production migrates it at start" api_migrates_the_old_database
  check "every board has Backlog, În lucru, În pauză and Gata, the project's own kept between them" every_board_has_the_fixed_columns
  check "every task stays in its column, at its place" every_task_keeps_its_column
  check "only the tasks in Gata stay finished, and the list says columnKind" only_tasks_in_gata_stay_finished
  check "a new project gets the four fixed columns, kind and never isDone" a_new_project_gets_the_four_fixed_columns
  check "the database holds each fixed kind once per project" the_database_holds_one_of_each
  check "the fixed columns answer 409 Column.Fixed to a rename, a move and a delete" the_fixed_columns_never_change
  check "a column of the project's own comes right before Gata" a_column_of_its_own_comes_right_before_gata
  check "it moves only between Backlog and Gata, 400 on position otherwise" it_moves_only_between_backlog_and_gata
  check "the fixed names are taken in any case, 400 on name" the_fixed_names_are_taken
  check "an empty one of the project's own goes, and an isDone sent as before changes nothing" an_empty_column_of_its_own_goes_and_is_done_changes_nothing
  check "Column.LastColumn and isDone are gone from the code" last_column_is_gone
  check "the migration goes back: is_done on Gata, the columns as they are" the_migration_goes_back
}

# ---- Task 2: the moves.

move_tests_pass() {
  classes_pass Kanabol.Domain.Tests 40 ColumnOrderTests TaskEstimateTests TaskPauseTests &&
    classes_pass Kanabol.Application.Tests 65 MoveTaskCommandHandlerTests CreateTaskCommandHandlerTests TaskEstimateCommandHandlerTests TaskPauseCommandHandlerTests &&
    classes_pass Kanabol.Api.Tests 17 TaskMoveEndpointTests TasksEndpointTests TaskEstimateEndpointTests TaskPauseEndpointTests
}

# MUT, the admin's: Backlog, Analiză (the project's own, right after it), În lucru, În pauză, Review, Gata.
a_board_to_move_on() {
  the_admin_signs_in || return 1
  [ -n "${mut:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Mutări","key":"MUT"}')" || return 1
  mut=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$mut/board-columns" '{"name":"Analiză"}')" || return 1
  mut_analysis=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$mut/board-columns/$mut_analysis/position" '{"position":1}')" || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$mut/board-columns" '{"name":"Review"}')" || return 1
  mut_review=$(body_field id)
  for kind in Backlog InProgress Paused Done; do
    eval "mut_$kind=\$(in_db \"SELECT id FROM board_columns WHERE project_id = '\$mut' AND kind = '\$kind'\")"
  done
  is 'Backlog:Backlog,Analiză:Custom,În lucru:InProgress,În pauză:Paused,Review:Custom,Gata:Done' "$(columns_of "$mut")"
}

new_task_in() { # new_task_in <title> [JSON fields to add]: the task's id in new_task
  status_is 201 "$(as "$admin_token" POST "/api/projects/$mut/tasks" "{\"title\":\"$1\",\"priority\":\"Low\"${2:+,$2}}")" || return 1
  new_task=$(body_field id)
}

moved_to() { # moved_to <task id> <column id> <expected status>: with an estimate and a reason, which only În lucru and În pauză take
  status_is "$3" "$(as "$admin_token" PATCH "/api/tasks/$1/position" "{\"columnId\":\"$2\",\"estimatedMinutes\":60,\"pauseReason\":\"Aștept\"}")"
}

a_task_starts_in_backlog() {
  a_board_to_move_on || return 1
  new_task_in 'Fără coloană' || return 1
  is "$mut_Backlog Backlog" "$(json "b.columnId + ' ' + b.columnName")" || return 1
  new_task_in 'Cu Backlog' "\"columnId\":\"$mut_Backlog\"" || return 1
  is "$mut_Backlog 1" "$(json "b.columnId + ' ' + b.position")"
}

another_column_is_refused_and_takes_no_number() {
  a_board_to_move_on || return 1
  other=$(in_db "SELECT id FROM board_columns WHERE project_id <> '$mut' LIMIT 1")
  for column in "$mut_analysis" "$mut_InProgress" "$mut_Paused" "$mut_review" "$mut_Done" "$other"; do
    status_is 400 "$(as "$admin_token" POST "/api/projects/$mut/tasks" "{\"title\":\"Altundeva\",\"priority\":\"Low\",\"columnId\":\"$column\"}")" || return 1
    is true "$(json "'columnId' in b.errors")" || return 1
  done
  is '3|2' "$(in_db "SELECT next_task_number, (SELECT count(*) FROM task_items WHERE project_id = '$mut') FROM projects WHERE id = '$mut'")"
}

# From each column at or after În lucru, back to Backlog and to Analiză: 409, the task where it was, no history.
no_task_goes_back_before_in_progress() {
  a_board_to_move_on || return 1
  for from in "$mut_InProgress" "$mut_Paused" "$mut_review" "$mut_Done"; do
    new_task_in 'Înapoi' || return 1
    task=$new_task
    moved_to "$task" "$from" 200 || return 1
    for back in "$mut_Backlog" "$mut_analysis"; do
      code_is 409 Task.CannotMoveBack "$(as "$admin_token" PATCH "/api/tasks/$task/position" "{\"columnId\":\"$back\",\"position\":0}")" || return 1
    done
    is "$from|1" "$(in_db "SELECT column_id, (SELECT count(*) FROM task_history_entries WHERE task_id = '$task' AND field = 'ColumnId') FROM task_items WHERE id = '$task'")" || return 1
  done
}

it_goes_forward_and_back_to_in_progress() {
  a_board_to_move_on || return 1
  new_task_in 'Înainte' || return 1
  task=$new_task
  moved_to "$task" "$mut_analysis" 200 && moved_to "$task" "$mut_Backlog" 200 && moved_to "$task" "$mut_analysis" 200 || return 1
  moved_to "$task" "$mut_Done" 200 || return 1
  is true "$(json "b.completedAt !== null")" || return 1
  moved_to "$task" "$mut_InProgress" 200 || return 1
  is null "$(json "b.completedAt")" || return 1
  moved_to "$task" "$mut_Paused" 200 && moved_to "$task" "$mut_InProgress" 200 && moved_to "$task" "$mut_review" 200
}

a_wrong_column_is_said_before_the_409() {
  a_board_to_move_on || return 1
  new_task_in 'Ordinea' || return 1
  moved_to "$new_task" "$mut_InProgress" 200 || return 1
  other=$(in_db "SELECT id FROM board_columns WHERE project_id <> '$mut' LIMIT 1")
  status_is 400 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$other\"}")" || return 1
  status_is 400 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$mut_Backlog\",\"position\":-1}")"
}

estimate_of() { # estimate_of <task id>: its minutes, empty for none
  in_db "SELECT estimated_minutes FROM task_items WHERE id = '$1'"
}

entries_of() { # entries_of <task id>: its history, field:old:new, the oldest first
  in_db "SELECT string_agg(field || ':' || coalesce(old_value, '') || ':' || coalesce(new_value, ''), ' ' ORDER BY changed_at, field) FROM task_history_entries WHERE task_id = '$1'"
}

in_progress_asks_for_an_estimate() {
  a_board_to_move_on || return 1
  new_task_in 'Fără estimare' || return 1
  task=$new_task
  status_is 400 "$(as "$admin_token" PATCH "/api/tasks/$task/position" "{\"columnId\":\"$mut_InProgress\"}")" || return 1
  is true "$(json "'estimatedMinutes' in b.errors")" || return 1
  for minutes in 14 60001; do
    status_is 400 "$(as "$admin_token" PATCH "/api/tasks/$task/position" "{\"columnId\":\"$mut_InProgress\",\"estimatedMinutes\":$minutes}")" || return 1
  done
  is "$mut_Backlog||" "$(in_db "SELECT column_id, estimated_minutes, (SELECT string_agg(field, ',') FROM task_history_entries WHERE task_id = '$task') FROM task_items WHERE id = '$task'")" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task/position" "{\"columnId\":\"$mut_InProgress\",\"estimatedMinutes\":240}")" || return 1
  is '240 În lucru' "$(json "b.estimatedMinutes + ' ' + b.columnName")" || return 1
  is "ColumnId:$mut_Backlog:$mut_InProgress EstimatedMinutes::240" "$(entries_of "$task")" || return 1
  is 1 "$(in_db "SELECT count(DISTINCT changed_at) FROM task_history_entries WHERE task_id = '$task'")"
}

an_estimate_elsewhere_is_ignored() {
  a_board_to_move_on || return 1
  new_task_in 'Altundeva' || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$mut_analysis\",\"estimatedMinutes\":5}")" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$mut_Paused\",\"estimatedMinutes\":90,\"pauseReason\":\"Aștept\"}")" || return 1
  is '' "$(estimate_of "$new_task")"
}

an_estimate_at_creation_writes_no_history() {
  a_board_to_move_on || return 1
  new_task_in 'Estimat' '"estimatedMinutes":90' || return 1
  is '90|0' "$(in_db "SELECT estimated_minutes, (SELECT count(*) FROM task_history_entries WHERE task_id = '$new_task') FROM task_items WHERE id = '$new_task'")" || return 1
  status_is 400 "$(as "$admin_token" POST "/api/projects/$mut/tasks" '{"title":"Prea puțin","priority":"Low","estimatedMinutes":10}')" || return 1
  is true "$(json "'estimatedMinutes' in b.errors")" || return 1
  # With one already, it enters În lucru without another.
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$mut_InProgress\"}")" || return 1
  is 90 "$(estimate_of "$new_task")"
}

put_estimate_changes_it() {
  a_board_to_move_on || return 1
  new_task_in 'Schimbat' '"estimatedMinutes":60' || return 1
  task=$new_task
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$task/estimate" '{"estimatedMinutes":120}')" || return 1
  is 120 "$(json b.estimatedMinutes)" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$task/estimate" '{"estimatedMinutes":null}')" || return 1
  is null "$(json b.estimatedMinutes)" || return 1
  is 'EstimatedMinutes:60:120 EstimatedMinutes:120:' "$(entries_of "$task")" || return 1
  status_is 400 "$(as "$admin_token" PUT "/api/tasks/$task/estimate" '{"estimatedMinutes":14}')" || return 1
  moved_to "$task" "$mut_InProgress" 200 && moved_to "$task" "$mut_Done" 200 || return 1
  status_is 400 "$(as "$admin_token" PUT "/api/tasks/$task/estimate" '{"estimatedMinutes":null}')" || return 1
  is true "$(json "'estimatedMinutes' in b.errors")" || return 1
  is 60 "$(estimate_of "$task")"
}

an_onlooker_changes_no_estimate() {
  a_board_to_move_on || return 1
  [ -n "${viewer_token:-}" ] || {
    status_is 201 "$(as "$admin_token" POST /api/roles '{"name":"Privitor","permissions":["projects.view_all"]}')" || return 1
    role=$(body_field id)
    status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"vera@example.com\",\"displayName\":\"Vera Privitor\",\"roleId\":\"$role\",\"initialPassword\":\"$admin_password\"}")" || return 1
    status_is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.78.0.2' \
      -d "{\"email\":\"vera@example.com\",\"password\":\"$admin_password\"}")" || return 1
    status_is 200 "$(send POST /api/auth/change-password -H "Authorization: Bearer $(body_field accessToken)" \
      -H 'Content-Type: application/json' -d "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"parola Verei pentru coloane\"}")" || return 1
    viewer_token=$(body_field accessToken)
  }
  new_task_in 'Privit' || return 1
  code_is 403 Project.MemberRequired "$(as "$viewer_token" PUT "/api/tasks/$new_task/estimate" '{"estimatedMinutes":60}')" || return 1
  is '' "$(estimate_of "$new_task")"
}

paused_with() { # paused_with <task id> <reason as JSON>: the status of its move into În pauză
  as "$admin_token" PATCH "/api/tasks/$1/position" "{\"columnId\":\"$mut_Paused\",\"pauseReason\":$2}"
}

paused_asks_for_a_reason() {
  a_board_to_move_on || return 1
  new_task_in 'Pauză' || return 1
  task=$new_task
  for reason in null '""' '"   "' '"pe două\nrânduri"' "\"$(printf 'a%.0s' $(seq 1 501))\""; do
    status_is 400 "$(paused_with "$task" "$reason")" || return 1
    is true "$(json "'pauseReason' in b.errors")" || return 1
  done
  is "$mut_Backlog||0" "$(in_db "SELECT column_id, pause_reason, (SELECT count(*) FROM task_history_entries WHERE task_id = '$task') FROM task_items WHERE id = '$task'")" || return 1
  status_is 200 "$(paused_with "$task" '"  Aștept răspunsul clientului "')" || return 1
  is 'Aștept răspunsul clientului' "$(json b.pauseReason)" || return 1
  is "ColumnId:$mut_Backlog:$mut_Paused PauseReason::Aștept răspunsul clientului" "$(entries_of "$task")" || return 1
  is 1 "$(in_db "SELECT count(DISTINCT changed_at) FROM task_history_entries WHERE task_id = '$task'")" || return 1
  status_is 200 "$(paused_with "$new_task" "\"$(printf 'b%.0s' $(seq 1 500))\"")" >/dev/null
}

leaving_the_pause_forgets_the_reason() {
  a_board_to_move_on || return 1
  new_task_in 'Pe pauză' || return 1
  task=$new_task
  status_is 200 "$(paused_with "$task" '"Aștept"')" || return 1
  new_task_in 'Alături' || return 1
  status_is 200 "$(paused_with "$new_task" '"Și eu"')" || return 1
  # A reorder there asks for nothing and keeps the reason.
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$mut_Paused\",\"position\":0}")" || return 1
  is 'Și eu 0' "$(json "b.pauseReason + ' ' + b.position")" || return 1
  moved_to "$task" "$mut_InProgress" 200 || return 1
  is null "$(json b.pauseReason)" || return 1
  is '|1' "$(in_db "SELECT pause_reason, (SELECT count(*) FROM task_history_entries WHERE task_id = '$task' AND field = 'PauseReason') FROM task_items WHERE id = '$task'")"
}

a_reason_elsewhere_is_ignored() {
  a_board_to_move_on || return 1
  new_task_in 'Fără pauză' || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$new_task/position" "{\"columnId\":\"$mut_Done\",\"pauseReason\":\"pe două\\nrânduri\"}")" || return 1
  is '|0' "$(in_db "SELECT pause_reason, (SELECT count(*) FROM task_history_entries WHERE task_id = '$new_task' AND field = 'PauseReason') FROM task_items WHERE id = '$new_task'")"
}

archived_tests_pass() {
  classes_pass Kanabol.Application.Tests 6 GetArchivedTasksQueryHandlerTests &&
    classes_pass Kanabol.Api.Tests 5 ArchivedTasksEndpointTests
}

archived_of() { # archived_of <token> [query]: the status of GET …/board/archived for MUT
  as "$1" GET "/api/projects/$mut/board/archived${2:+?$2}"
}

# Two tasks archived, the one from Backlog last: it comes first, then the one from În pauză, with its column,
# who archived it, its estimate and its reason; the restore takes it back there, with them.
the_archived_column_gives_the_latest_first() {
  a_board_to_move_on || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$mut/board")" || return 1
  is true "$(json b.canSeeArchived)" || return 1
  new_task_in "Arhivat din pauză" '"estimatedMinutes":45' || return 1
  shelved=$new_task
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$shelved/position" "{\"columnId\":\"$mut_Paused\",\"pauseReason\":\"Aștept avizul\"}")" || return 1
  new_task_in "Arhivat din Backlog" || return 1
  dropped=$new_task
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$shelved")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$dropped")" || return 1
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  status_is 200 "$(archived_of "$admin_token" 'page=1&pageSize=2')" || return 1
  is "2 $dropped Backlog $admin_id" "$(json "b.items.length + ' ' + b.items[0].id + ' ' + b.items[0].columnName + ' ' + b.items[0].archivedBy.userId")" || return 1
  is "$shelved $mut_Paused 45 false $admin_id" "$(json "(c => c.id + ' ' + c.columnId + ' ' + c.estimatedMinutes + ' ' + c.isWorking + ' ' + c.archivedBy.userId)(b.items[1])")" || return 1
  is 'În pauză|Aștept avizul|Low' "$(json "b.items[1].columnName + '|' + b.items[1].pauseReason + '|' + b.items[1].priority")" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$shelved/restore")" || return 1
  is "$mut_Paused 45" "$(json "b.columnId + ' ' + b.estimatedMinutes")" || return 1
  is 'Aștept avizul' "$(json b.pauseReason)" || return 1
  status_is 200 "$(archived_of "$admin_token")" || return 1
  is false "$(json "b.items.some((c) => c.id === process.argv[3])" "$shelved")"
}

the_archived_column_is_for_tasks_archive_only() {
  a_board_to_move_on || return 1
  person_signs_in "victor@example.com" "Victor Vede Tot" '["projects.view_all"]' || return 1
  status_is 200 "$(as "$person_token" GET "/api/projects/$mut/board")" || return 1
  is false "$(json b.canSeeArchived)" || return 1
  code_is 403 Auth.PermissionRequired "$(archived_of "$person_token")" || return 1
  person_signs_in "ion@example.com" "Ion Din Afară" '["tasks.archive"]' || return 1
  code_is 404 Project.NotFound "$(archived_of "$person_token")" || return 1
  status_is 400 "$(archived_of "$admin_token" 'pageSize=101')" || return 1
  is pageSize "$(json "Object.keys(b.errors).join(',')")"
}

task_2() {
  use_database "$api_db"
  echo "22.2 the moves"
  check "Docker is running" docker_is_running
  check "the tests pass: the order of the columns, the moves, the creations and their endpoints" move_tests_pass
  check "the tests pass: the column Arhivate and canSeeArchived" archived_tests_pass
  check "the real API in Production makes MUT, with columns of its own before and after În lucru" a_board_to_move_on
  check "a task starts in Backlog, given or not" a_task_starts_in_backlog
  check "any other column is 400 on columnId, the project's own too, and takes no number" another_column_is_refused_and_takes_no_number
  check "from În lucru or after, never back before it: 409 Task.CannotMoveBack, no history" no_task_goes_back_before_in_progress
  check "before În lucru both ways, and out of Gata to În lucru, open again" it_goes_forward_and_back_to_in_progress
  check "a column of another project, or a wrong place, is 400 before the 409" a_wrong_column_is_said_before_the_409
  check "În lucru asks a task without an estimate for one, 15 to 60,000, written next to the move" in_progress_asks_for_an_estimate
  check "an estimate sent on another move is ignored" an_estimate_elsewhere_is_ignored
  check "an estimate at creation writes no history, and the task enters În lucru without another" an_estimate_at_creation_writes_no_history
  check "PUT …/estimate changes it, takes it away before În lucru and not after" put_estimate_changes_it
  check "one who only sees the project changes no estimate" an_onlooker_changes_no_estimate
  check "În pauză asks every entry for a reason, one line of 1 to 500, written next to the move" paused_asks_for_a_reason
  check "leaving the pause forgets the reason without an entry, a reorder there asks for none" leaving_the_pause_forgets_the_reason
  check "a reason sent on another move is ignored" a_reason_elsewhere_is_ignored
  check "Arhivate gives the latest archived first, with its column, who and what it keeps; the restore takes it back" the_archived_column_gives_the_latest_first
  check "Arhivate is for tasks.archive only (403), in a project the caller sees (404), in pages of at most 100" the_archived_column_is_for_tasks_archive_only
}

# ---- Task 3: the general settings.

settings_tests_pass() {
  classes_pass Kanabol.Domain.Tests 15 WorkScheduleTests &&
    classes_pass Kanabol.Application.Tests 20 WorkScheduleHandlerTests ScheduleOptionsTests AddWorkScheduleMigrationTests &&
    classes_pass Kanabol.Api.Tests 8 SettingsEndpointTests
}

# Someone signed in with a role of their own; their token in person_token.
person_signs_in() { # person_signs_in <email> <name> <permissions as a JSON list>
  the_admin_signs_in || return 1
  status_is 201 "$(as "$admin_token" POST /api/roles "{\"name\":\"Rol $2\",\"permissions\":$3}")" || return 1
  role=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"$1\",\"displayName\":\"$2\",\"roleId\":\"$role\",\"initialPassword\":\"$admin_password\"}")" || return 1
  status_is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.78.0.3' \
    -d "{\"email\":\"$1\",\"password\":\"$admin_password\"}")" || return 1
  status_is 200 "$(send POST /api/auth/change-password -H "Authorization: Bearer $(body_field accessToken)" \
    -H 'Content-Type: application/json' -d "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"parola lui $1 pentru coloane\"}")" || return 1
  person_token=$(body_field accessToken)
}

schedule_is() { # schedule_is <token> <expected days start end zone>
  status_is 200 "$(as "$1" GET /api/settings/work-schedule)" || return 1
  is "$2" "$(json "b.days.join(',') + ' ' + b.start + ' ' + b.end + ' ' + b.timeZone")"
}

settings_manage_is_seeded_and_the_admins() {
  the_admin_signs_in || return 1
  is 'settings.manage|t' "$(in_db "SELECT p.name, bool_or(r.is_system) FROM permissions p JOIN role_permissions rp ON rp.permission_id = p.id JOIN roles r ON r.id = rp.role_id WHERE p.name = 'settings.manage' GROUP BY p.name" | sed 's/|True$/|t/')"
}

anyone_reads_the_schedule() {
  person_signs_in "mihai@example.com" "Mihai Fără Setări" '["projects.manage"]' || return 1
  mihai_token=$person_token
  schedule_is "$mihai_token" '1,2,3,4,5 08:30 16:30 Europe/Bucharest' || return 1
  is 401 "$(send GET /api/settings/work-schedule)"
}

only_settings_manage_changes_it() {
  [ -n "${mihai_token:-}" ] || anyone_reads_the_schedule || return 1
  code_is 403 Auth.PermissionRequired "$(as "$mihai_token" PUT /api/settings/work-schedule '{"days":[1],"start":"09:00","end":"17:00"}')" || return 1
  status_is 200 "$(as "$admin_token" PUT /api/settings/work-schedule '{"days":[6,1,2,3,4],"start":"09:00","end":"17:30"}')" || return 1
  schedule_is "$mihai_token" '1,2,3,4,6 09:00 17:30 Europe/Bucharest' || return 1
  is '1,2,3,4,6|09:00|17:30' "$(in_db "SELECT work_days, to_char(start_time, 'HH24:MI'), to_char(end_time, 'HH24:MI') FROM work_schedules")"
}

a_wrong_field_is_400_on_it() {
  the_admin_signs_in || return 1
  for case in '[]|09:00|17:00|days' '[1,1]|09:00|17:00|days' '[0]|09:00|17:00|days' '[1]|9:00|17:00|start' '[1]|09:00|24:00|end' '[1]|17:00|09:00|end'; do
    days=${case%%|*}
    rest=${case#*|}
    start=${rest%%|*}
    rest=${rest#*|}
    end=${rest%%|*}
    field=${rest#*|}
    status_is 400 "$(as "$admin_token" PUT /api/settings/work-schedule "{\"days\":$days,\"start\":\"$start\",\"end\":\"$end\"}")" || return 1
    is "$field" "$(json "Object.keys(b.errors).join(',')")" || return 1
  done
}

the_change_is_logged() {
  grep -q '"@mt":"Work schedule changed by {UserId} from {OldSchedule} to {NewSchedule}"' "$api_log" &&
    grep -q '"OldSchedule":"1,2,3,4,5 08:30-16:30","NewSchedule":"1,2,3,4,6 09:00-17:30"' "$api_log" || {
    grep 'Work schedule' "$api_log" | tail -n 3
    return 1
  }
}

an_unknown_zone_stops_the_start() {
  stop_api
  need_database || return 1
  (
    cd "$api_dir" || exit 1
    exec env ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__Default="$(connection_to "$check_db")" \
      Jwt__Secret="$jwt_secret" Storage__Root="$scratch/attachments" Admin__InitialPassword="$initial_password" \
      Alerts__OverdueCheckEnabled=false Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 Schedule__TimeZone=Europe/Atlantida \
      timeout 90 dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$scratch/zone.log" 2>&1
  status=$?
  [ "$status" -ne 0 ] && [ "$status" -ne 124 ] || {
    echo "the API ran on with an unknown zone (exit $status)"
    return 1
  }
  grep -q 'Schedule:TimeZone has to be a time zone this system knows' "$scratch/zone.log"
}

api_image() {
  [ -n "${api_image_built:-}" ] && return 0
  "$docker_cli" build -q -t "kanabol-api:columns-check-$$" backend >"$scratch/image.log" 2>&1 || {
    tail -n 20 "$scratch/image.log"
    return 1
  }
  api_image_built=1
}

# The image starts with no database it can reach: past the options it stops on the database, before them on the zone.
image_started_with() { # image_started_with <zone>: the container's log
  "$docker_cli" run --rm -e Schedule__TimeZone="$1" -e Jwt__Secret="$jwt_secret" -e Storage__Root=/data/attachments \
    -e 'ConnectionStrings__Default=Host=127.0.0.1;Port=9;Database=none;Username=none;Password=none;Timeout=2' \
    -e Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 "kanabol-api:columns-check-$$" 2>&1 | tr -d '\r'
}

the_image_carries_the_zone_data() {
  docker_is_running >/dev/null && api_image || return 1
  "$docker_cli" run --rm --entrypoint sh "kanabol-api:columns-check-$$" -c 'test -s /usr/share/zoneinfo/Europe/Bucharest && test -s /usr/share/zoneinfo/Europe/Vienna' || {
    echo "the image has no zone data"
    return 1
  }
  image_started_with Europe/Vienna >"$scratch/image-vienna.log"
  if grep -q 'Schedule:TimeZone' "$scratch/image-vienna.log"; then
    echo "the image doesn't know Europe/Vienna"
    return 1
  fi
  grep -qiE 'npgsql|connect|database' "$scratch/image-vienna.log" || {
    echo "the image didn't get past the options"
    tail -n 5 "$scratch/image-vienna.log"
    return 1
  }
  image_started_with Europe/Atlantida >"$scratch/image-atlantida.log"
  grep -q 'Schedule:TimeZone has to be a time zone this system knows' "$scratch/image-atlantida.log"
}

compose_names_the_zone() {
  grep -qF "# The work schedule's time zone is Europe/Bucharest by default: Schedule__TimeZone=Europe/Vienna changes it (group 22)" compose.yaml
}

worked_time_tests_pass() {
  classes_pass Kanabol.Domain.Tests 28 WorkingTimeTests TaskWorkedTimeTests &&
    classes_pass Kanabol.Application.Tests 9 WorkedTimeHandlerTests AddWorkedTimeMigrationTests &&
    classes_pass Kanabol.Api.Tests 1 WorkedTimeEndpointTests
}

# The schedule's day in Bucharest, ISO (1 = Monday), and the minutes since its midnight.
bucharest_now() {
  node -e "const p = Object.fromEntries(new Intl.DateTimeFormat('en-GB', { timeZone: 'Europe/Bucharest', weekday: 'short', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date()).map((x) => [x.type, x.value])); console.log(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].indexOf(p.weekday) + 1, Number(p.hour) * 60 + Number(p.minute))"
}

worked_of() { # worked_of <task id>: isWorking and workedSeconds of its GET, and worked_seconds and work_started_at set or not in the database
  status_is 200 "$(as "$admin_token" GET "/api/tasks/$1")" || return 1
  echo "$(json "b.isWorking + ' ' + b.workedSeconds") $(in_db "SELECT worked_seconds, work_started_at IS NOT NULL FROM task_items WHERE id = '$1'" | sed 's/|True$/|t/; s/|False$/|f/')"
}

at_least() { # at_least <minimum> <seconds>
  [ "$2" -ge "$1" ] 2>/dev/null || {
    echo "expected at least $1 seconds, got $2"
    return 1
  }
}

# Every hour of every day in the schedule, through the API, past midnight first when it's close, so the next
# seconds stay inside 00:00-23:59; today, ISO, in today.
every_hour_is_worked() {
  set -- $(bucharest_now)
  if [ "$2" -ge 1437 ]; then
    sleep $(((1440 - $2) * 60 + 5))
    set -- $(bucharest_now)
  fi
  today=$1
  status_is 200 "$(as "$admin_token" PUT /api/settings/work-schedule '{"days":[1,2,3,4,5,6,7],"start":"00:00","end":"23:59"}')"
}

# Every hour of every day counts first, then none of today's: an open interval goes by the schedule of now.
the_counter_runs_in_in_progress_by_the_schedule() {
  a_board_to_move_on || return 1
  every_hour_is_worked || return 1
  new_task_in "Cronometrat" '"estimatedMinutes":60' || return 1
  timed=$new_task
  is 'false 0 0|f' "$(worked_of "$timed")" || return 1
  moved_to "$timed" "$mut_InProgress" 200 || return 1
  is 'true 60' "$(json "b.isWorking + ' ' + b.estimatedMinutes")" || return 1
  sleep 3
  read_now=$(worked_of "$timed") || return 1
  set -- $read_now
  is 'true 0|t' "$1 $3" || return 1
  at_least 2 "$2" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$mut/board")" || return 1
  is 'true 60 null' "$(json "(t => t.isWorking + ' ' + t.estimatedMinutes + ' ' + t.pauseReason)(b.tasks.find((t) => t.id === process.argv[3]))" "$timed")" || return 1
  at_least 2 "$(json "b.tasks.find((t) => t.id === process.argv[3]).workedSeconds" "$timed")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$mut/tasks?search=Cronometrat")" || return 1
  is 'true 60' "$(json "b.items[0].isWorking + ' ' + b.items[0].estimatedMinutes")" || return 1
  moved_to "$timed" "$mut_Paused" 200 || return 1
  worked=$(json b.workedSeconds)
  at_least 3 "$worked" || return 1
  is "false $worked $worked|f" "$(worked_of "$timed")" || return 1
  is 'ColumnId,ColumnId,PauseReason' "$(in_db "SELECT string_agg(field, ',' ORDER BY field) FROM task_history_entries WHERE task_id = '$timed'")" || return 1
  others=$(node -e "console.log(JSON.stringify([1, 2, 3, 4, 5, 6, 7].filter((d) => d !== Number(process.argv[1]))))" "$today")
  status_is 200 "$(as "$admin_token" PUT /api/settings/work-schedule "{\"days\":$others,\"start\":\"00:00\",\"end\":\"23:59\"}")" || return 1
  moved_to "$timed" "$mut_InProgress" 200 || return 1
  sleep 2
  is "true $worked $worked|t" "$(worked_of "$timed")" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$timed")" || return 1
  is "$worked|f" "$(in_db "SELECT worked_seconds, work_started_at IS NOT NULL FROM task_items WHERE id = '$timed'" | sed 's/|False$/|f/')" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$timed/restore")" || return 1
  is "true $worked" "$(json "b.isWorking + ' ' + b.workedSeconds")" || return 1
  is "$worked|t" "$(in_db "SELECT worked_seconds, work_started_at IS NOT NULL FROM task_items WHERE id = '$timed'" | sed 's/|True$/|t/')"
}

task_3() {
  use_database "$api_db"
  echo "22.3 the worked time and the general settings"
  check "Docker is running" docker_is_running
  check "the tests pass: the schedule, its handlers, the time zone, the migration and the endpoints" settings_tests_pass
  check "the tests pass: WorkingTime, the counter, its handlers, the migration AddWorkedTime and the DTOs" worked_time_tests_pass
  check "the real API in Production seeds settings.manage, which the Admin has" settings_manage_is_seeded_and_the_admins
  check "anyone signed in reads the schedule: Monday to Friday, 08:30-16:30, Europe/Bucharest" anyone_reads_the_schedule
  check "only settings.manage changes it, 403 Auth.PermissionRequired otherwise" only_settings_manage_changes_it
  check "a wrong field is 400 on it" a_wrong_field_is_400_on_it
  check "the change is logged with who, the schedule before and after" the_change_is_logged
  check "the counter runs in În lucru by the schedule of now, changed through the API, and stops in the archive" the_counter_runs_in_in_progress_by_the_schedule
  check "an unknown Schedule__TimeZone stops the start, naming it" an_unknown_zone_stops_the_start
  check "the API image carries the zone data, knows Europe/Vienna and refuses an unknown zone" the_image_carries_the_zone_data
  check "compose.yaml's comment names Schedule__TimeZone" compose_names_the_zone
}

# ---- Task 4: the work-time report and the cycle time from În lucru.

work_time_tests_pass() {
  classes_pass Kanabol.Application.Tests 60 GetWorkTimeReportQueryHandlerTests GetCycleTimeReportQueryHandlerTests ReportAccessTests &&
    classes_pass Kanabol.Api.Tests 45 ReportsEndpointTests
}

# RAP, the admin's: Backlog, În lucru, În pauză, Analiză (the project's own), Gata. Four tasks, each moved
# through the API, then their moments put a day ago in the database, from B, on the hour:
#   RAP-1 15 min estimated, 10 min worked: created B, În lucru B+1h, Gata B+2h (lead 2 h, cycle 1 h)
#   RAP-2 15 min estimated, 1 h worked: created B, În lucru B+1h, Gata B+4h (lead 4 h, cycle 3 h)
#   RAP-3 no estimate, 20 min worked: created B, straight to Gata B+1h (lead 1 h, cycle 1 h, from its creation)
#   RAP-4 15 min estimated, 15 min worked: created B, Analiză B+1h, În lucru B+3h, Gata B+5h (lead 5 h, cycle 2 h)
# The admin is assigned to RAP-1, 2 and 4.
a_project_with_work_done() {
  the_admin_signs_in || return 1
  [ -n "${rap:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Rapoarte de timp","key":"RAP"}')" || return 1
  rap=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$rap/board-columns" '{"name":"Analiză"}')" || return 1
  rap_analysis=$(body_field id)
  for kind in Backlog InProgress Done; do
    eval "rap_$kind=\$(in_db \"SELECT id FROM board_columns WHERE project_id = '\$rap' AND kind = '\$kind'\")"
  done
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  base=$(in_db "SELECT to_char(date_trunc('hour', now() - interval '1 day') AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"')")
  for spec in "1|15|600|$rap_InProgress,$rap_Done|1,2" "2|15|3600|$rap_InProgress,$rap_Done|1,4" "3||1200|$rap_Done|1" "4|15|900|$rap_analysis,$rap_InProgress,$rap_Done|1,3,5"; do
    number=${spec%%|*}
    rest=${spec#*|}
    estimate=${rest%%|*}
    rest=${rest#*|}
    worked=${rest%%|*}
    rest=${rest#*|}
    columns=${rest%%|*}
    hours=${rest#*|}
    fields="\"assigneeIds\":[\"$admin_id\"]"
    [ "$number" = 3 ] && fields='"assigneeIds":[]'
    [ -n "$estimate" ] && fields="$fields,\"estimatedMinutes\":$estimate"
    status_is 201 "$(as "$admin_token" POST "/api/projects/$rap/tasks" "{\"title\":\"Timp $number\",\"priority\":\"Low\",$fields}")" || return 1
    task=$(body_field id)
    for column in $(echo "$columns" | tr ',' ' '); do
      status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$task/position" "{\"columnId\":\"$column\"}")" || return 1
    done
    # The moves' entries, from the first, get B plus their hours; the task its creation, completion and worked time.
    sql_hours=$(echo "$hours" | tr ',' ' ')
    index=0
    for entry in $(in_db "SELECT id FROM task_history_entries WHERE task_id = '$task' AND field = 'ColumnId' ORDER BY changed_at, id"); do
      index=$((index + 1))
      hour=$(echo "$sql_hours" | cut -d' ' -f"$index")
      in_db "UPDATE task_history_entries SET changed_at = timestamptz '$base' + interval '$hour hours' WHERE id = '$entry'" >/dev/null
    done
    last=$(echo "$sql_hours" | tr ' ' '\n' | tail -n 1)
    in_db "UPDATE task_items SET created_at = timestamptz '$base', completed_at = timestamptz '$base' + interval '$last hours', worked_seconds = $worked, work_started_at = NULL WHERE id = '$task'" >/dev/null
    eval "rap_task_$number=\$task"
  done
  is 'RAP-1 RAP-2 RAP-3 RAP-4' "$(in_db "SELECT string_agg('RAP-' || number, ' ' ORDER BY number) FROM task_items WHERE project_id = '$rap' AND completed_at IS NOT NULL")"
}

around_now() { # the interval of the reports: two days back to a day ahead, in UTC
  echo "from=$(in_db "SELECT to_char((now() - interval '2 days') AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"')")&to=$(in_db "SELECT to_char((now() + interval '1 day') AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"')")"
}

the_cycle_time_starts_in_in_progress() {
  a_project_with_work_done || return 1
  status_is 200 "$(as "$admin_token" GET "/api/reports/cycle-time?$(around_now)&projectId=$rap")" || return 1
  is '4 10800/10800 6300/5400' "$(json "b.count + ' ' + [b.leadTime, b.cycleTime].map((d) => d.averageSeconds + '/' + d.medianSeconds).join(' ')")"
}

the_work_time_report_sums_the_estimated_ones() {
  a_project_with_work_done || return 1
  status_is 200 "$(as "$admin_token" GET "/api/reports/work-time?$(around_now)&projectId=$rap")" || return 1
  is '4 3 2700 5100 0.8889 1' "$(json "[b.count, b.estimatedCount, b.estimatedSeconds, b.workedSeconds, b.deviation.toFixed(4), b.overCount].join(' ')")" || return 1
  is "$rap_task_2 RAP-2 Timp 2 900 3600" "$(json "b.over.map((t) => [t.taskId, t.key, t.title, t.estimatedSeconds, t.workedSeconds].join(' ')).join(';')")" || return 1
  is "$admin_id 3 2700 5100" "$(json "b.byUser.map((u) => [u.userId, u.count, u.estimatedSeconds, u.workedSeconds].join(' ')).join(';')")" || return 1
  is "RAP 4 2700 5100" "$(json "b.byProject.map((p) => [p.key, p.count, p.estimatedSeconds, p.workedSeconds].join(' ')).join(';')")"
}

the_work_time_report_is_read_as_the_others() {
  a_project_with_work_done || return 1
  person_signs_in "toma@example.com" "Toma Fără Rapoarte" '["tasks.create"]' || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$rap/members" "{\"userId\":\"$(in_db "SELECT id FROM users WHERE email = 'toma@example.com'")\",\"role\":\"Member\"}")" || return 1
  code_is 403 Project.OwnerRequired "$(as "$person_token" GET "/api/reports/work-time?$(around_now)&projectId=$rap")" || return 1
  status_is 400 "$(as "$admin_token" GET "/api/reports/work-time?to=2026-11-02T00:00:00Z")" || return 1
  is from "$(json "Object.keys(b.errors).join(',')")"
}

task_4() {
  use_database "$api_db"
  echo "22.4 the work-time report and the cycle time from În lucru"
  check "Docker is running" docker_is_running
  check "the tests pass: work-time, the cycle time, who reads them and their endpoints" work_time_tests_pass
  check "the real API in Production makes RAP, with four tasks moved and their moments a day ago" a_project_with_work_done
  check "the cycle time starts at the first entry into În lucru, or at the creation without one" the_cycle_time_starts_in_in_progress
  check "work-time sums the estimated tasks that were worked, by person and project, and the overruns" the_work_time_report_sums_the_estimated_ones
  check "work-time is read as the other reports: an owner, 403 Project.OwnerRequired for a member, 400 on from" the_work_time_report_is_read_as_the_others
}

# ---- Task 5: the frontend. Only the packages.

web=frontend
app=$web/src/app
board=$app/features/board
# The group's base: dev before group 22, whose tokens the fixed columns may not add to.
group_base=acf782f

plain() { # Without the colors the tools print
  sed 's/\x1b\[[0-9;]*m//g' "$1"
}

in_file() { # in_file <text> <file>: the text, as it is, in the file
  grep -qF -- "$1" "$2" || {
    echo "no \"$1\" in $2"
    return 1
  }
}

specs_pass() { # specs_pass <spec file or glob, from frontend/> <minimum passed>
  log="$scratch/specs-$(printf '%s' "$1" | tr '/*' '__').log"
  (cd "$web" && npx ng test --no-watch --include "$1") >"$log" 2>&1 || {
    plain "$log" | grep -E '×|FAIL|Error' | head -n 25
    return 1
  }
  passed=$(plain "$log" | sed -n 's/^ *Tests *\([0-9]*\) passed.*/\1/p' | tail -n 1)
  [ "${passed:-0}" -ge "$2" ] || {
    echo "only ${passed:-0} specs passed, expected at least $2"
    return 1
  }
}

specs_named() { # specs_named <spec file> <name...>: each is an it() of the file
  file=$1
  shift
  for name in "$@"; do
    in_file "it('$name'" "$file" || return 1
  done
}

# The fixed columns' colors (docs/UI_UX_SPEC.md §7.4): existing tokens only, and the bar and the icon at 3:1 at
# least on the column's background, color-mix(navy-900 4%, surface-50), in light and in dark, from _tokens.scss.
fixed_colors_reach_3_to_1() {
  node - "$web/src/styles/_tokens.scss" <<'JS'
const fs = require('fs');
const text = fs.readFileSync(process.argv[2], 'utf8');
const dark = text.indexOf('@mixin dark');
const themes = { light: text.slice(0, dark), dark: text.slice(dark) };
const token = (css, name) => {
  const found = css.match(new RegExp(`--kb-${name}:\\s*(#[0-9a-fA-F]{6})`));
  if (!found) throw new Error(`no --kb-${name}`);
  return found[1];
};
const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
const lum = (c) => {
  const [r, g, b] = c.map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};
const ratio = (a, b) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
};
let failed = false;
for (const [theme, css] of Object.entries(themes)) {
  const navy = rgb(token(css, 'navy-900'));
  const surface = rgb(token(css, 'surface-50'));
  const background = navy.map((v, i) => v * 0.04 + surface[i] * 0.96);
  for (const [column, name] of [['Backlog', 'text-500'], ['În lucru', 'accent-600'], ['În pauză', 'warning-600'], ['Gata', 'success-600'], ['Arhivate', 'text-600']]) {
    const value = ratio(rgb(token(css, name)), background);
    if (value < 3) {
      console.log(`${theme}: ${column}, --kb-${name}, ${value.toFixed(2)}:1`);
      failed = true;
    }
  }
}
process.exit(failed ? 1 : 0);
JS
}

no_token_is_new() {
  git diff --quiet "$group_base" -- "$web/src/styles/_tokens.scss" || {
    echo "_tokens.scss changed since $group_base"
    return 1
  }
}

the_fixed_columns_wear_their_kind() {
  for pair in 'backlog:text-500' 'inprogress:accent-600' 'paused:warning-600' 'done:success-600'; do
    in_file ".kind-${pair%%:*} {" "$board/board-column.ts" || return 1
    in_file "--kb-kind: var(--kb-${pair#*:});" "$board/board-column.ts" || return 1
  done
  for icon in "Backlog: 'inbox'" "InProgress: 'play_circle'" "Paused: 'pause_circle'" "Done: 'check_circle'"; do
    in_file "$icon" "$board/board-column.ts" || return 1
  done
  in_file 'box-shadow: inset 0 3px 0 var(--kb-kind);' "$board/board-column.ts" &&
    in_file 'class="material-symbols-outlined kind-icon" aria-hidden="true"' "$board/board-column.ts" &&
    in_file "@if (canManage() && column().kind === 'Custom')" "$board/board-column.ts" &&
    in_file "[canCreate]=\"store.canCreate() && column.kind === 'Backlog'\"" "$board/board-page.html" &&
    in_file '[cdkDropListEnterPredicate]="accepts"' "$board/board-column.ts" &&
    in_file 'Un task început nu se mai întoarce înainte de «În lucru».' "$board/board-page.ts"
}

the_board_specs_pass() {
  specs_named "$board/board-page.spec.ts" \
    'marks each fixed column with the bar and the icon of its kind, hidden from screen readers, and none of its own' \
    'gives an owner a menu on the columns of the project only, and a new column' \
    'offers "+ Task" in Backlog only' \
    'moves a column of its own only between Backlog and Gata' \
    'offers to delete only an empty column of its own, and asks first' \
    'keeps a started card out of the columns before În lucru while it is dragged' \
    'puts a started card let go before În lucru back where it was, saying why' \
    'says why when the CDK hands back a started card let go over Analiză' \
    'stops a started card at În lucru from the keyboard' || return 1
  specs_pass 'src/app/features/board/*.spec.ts' 94
}

# The estimate's sheet and the pause's (UI §7.4), shared with the task's panel, their texts, and the card.
the_sheets_ask_as_the_spec_says() {
  sheets=$app/shared/ui
  in_file 'Cât estimezi pentru «{{ data.key }}»?' "$sheets/estimate-sheet.ts" &&
    in_file 'Estimare (ore)' "$sheets/estimate-sheet.ts" &&
    in_file 'Cât timp de lucru crezi că ia. O poți schimba din task.' "$sheets/estimate-sheet.ts" &&
    in_file 'Începe lucrul' "$sheets/estimate-sheet.ts" &&
    in_file 'Între 0,25 și 1000 de ore.' "$app/shared/models/estimate.ts" &&
    in_file 'De ce pui pe pauză «{{ data.key }}»?' "$sheets/pause-sheet.ts" &&
    in_file 'Pune pe pauză' "$sheets/pause-sheet.ts" &&
    in_file 'Scrie motivul pauzei.' "$sheets/pause-sheet.ts" &&
    in_file 'export const PAUSE_REASON_MAX = 500;' "$sheets/pause-sheet.ts" &&
    in_file 'width: SHEET_WIDTH.confirm,' "$app/shared/move-questions.ts" &&
    in_file 'Mutarea a fost anulată.' "$board/board-page.ts"
}

the_card_shows_the_work_and_the_pause() {
  in_file 'timer</span' "$board/task-card.ts" &&
    in_file "[kind]=\"over() ? 'over' : 'neutral'\"" "$board/task-card.ts" &&
    in_file 'color: var(--kb-accent-600);' "$board/task-card.ts" &&
    in_file 'pause_circle</span>' "$board/task-card.ts" &&
    in_file '[matTooltip]="reason"' "$board/task-card.ts" &&
    in_file 'text-overflow: ellipsis;' "$board/task-card.ts" &&
    in_file 'Pe pauză: ${task.pauseReason}:reason:' "$board/board-column.ts"
}

the_asking_specs_pass() {
  specs_named "$board/board-page.spec.ts" \
    'asks for an estimate before a card without one starts, and sends it with the move' \
    'moves a card that has an estimate into În lucru without asking' \
    'asks why before every pause, and shows the reason on the card' \
    'asks nothing for a card reordered within În pauză' \
    'puts the card back and asks the API nothing when the sheet is turned down' \
    'asks from the keyboard too, and says when the sheet was turned down' \
    'shows the time worked against the estimate, danger once over, the timer in accent in În lucru' || return 1
  specs_named "$board/board-store.spec.ts" \
    'lets the card wait in its new place while the move asks, then sends the answers and shows them' \
    'puts the card back, asking the API nothing, when the person gives up' || return 1
  specs_pass 'src/app/shared/move-questions.spec.ts' 9 &&
    specs_pass 'src/app/shared/models/estimate.spec.ts' 6
}

# Arhivate (UI §7.4): for tasks.archive, last, closed at first in a 44px band, open at 300px, its state in
# localStorage, 50 at a time, archiving by a drop after the question, restoring by a drop or its ⋮.
the_archived_column_is_as_the_spec_says() {
  in_file "export const ARCHIVED_STATE_KEY = 'kb.board.archived';" "$board/archived-store.ts" &&
    in_file 'export const ARCHIVED_PAGE_SIZE = 50;' "$board/board-api.ts" &&
    in_file '`/projects/${projectId}/board/archived`' "$board/board-api.ts" &&
    in_file '@if (store.canSeeArchived()) {' "$board/board-page.html" &&
    in_file 'width: 44px;' "$board/archived-column.ts" &&
    in_file 'width: 300px;' "$board/archived-column.ts" &&
    in_file 'writing-mode: vertical-rl;' "$board/archived-column.ts" &&
    in_file 'inventory_2' "$board/archived-column.ts" &&
    in_file '--kb-kind: var(--kb-text-600);' "$board/archived-column.ts" &&
    in_file 'Încarcă mai multe' "$board/archived-column.ts" &&
    in_file 'Niciun task arhivat.' "$board/archived-column.ts" &&
    in_file 'Restaurează' "$board/archived-column.ts" &&
    in_file 'opacity: 0.72;' "$board/task-card.ts" &&
    in_file 'arhivat {{ archivedAt | relativeTime }}' "$board/task-card.ts" &&
    in_file 'Arhivezi task-ul «${task.title}:title:»?' "$board/board-page.ts" &&
    in_file '«${card.key}:key:» a revenit în «${card.columnName}:column:».' "$board/board-page.ts"
}

the_archived_specs_pass() {
  specs_named "$board/board-page.spec.ts" \
    'comes last, after + Coloană, only for tasks.archive' \
    'stays away, and reads nothing, without tasks.archive' \
    'is closed at first: a band with its icon, its name downwards and its count, which a click opens' \
    'opens as this browser left it' \
    'shows its cards faded, with when they were archived in place of the deadline' \
    'reads 50 at a time, with Încarcă mai multe at the end' \
    'says when nothing is archived' \
    'archives a card of the board let go on it, after the question' \
    'restores one of its cards let go anywhere on the board into its own column' \
    'restores a card from its ⋮, from the keyboard' \
    'lets every column of the board take one of its cards, Backlog too, and takes the board cards of a member' \
    'keeps all its cards whatever the filters' &&
    specs_pass 'src/app/features/board/*.spec.ts' 117
}

# The task's panel (UI §7.5): Estimare in place, Timp lucrat, Motivul pauzei, the Stare select's rules, the
# tabs' Reîncarcă, and the history's new lines.
the_panel_is_as_the_spec_says() {
  tasks_web=$app/features/tasks
  in_file 'Estimare</dt>' "$tasks_web/task-panel.html" &&
    in_file 'Fără estimare' "$tasks_web/task-panel.html" &&
    in_file 'Timp lucrat</dt>' "$tasks_web/task-panel.html" &&
    in_file 'Se numără acum, în programul de lucru.' "$tasks_web/task-panel.html" &&
    in_file 'din {{ formatHoursMinutes(task.estimatedMinutes) }} estimate' "$tasks_web/task-panel.html" &&
    in_file 'Motivul pauzei</dt>' "$tasks_web/task-panel.html" &&
    in_file '[disabled]="goesBack(task, column)"' "$tasks_web/task-panel.html" &&
    in_file 'await this.questions.estimate(task.key)' "$tasks_web/task-panel.ts" &&
    in_file 'await this.questions.pauseReason(task.key)' "$tasks_web/task-panel.ts" &&
    in_file 'matTooltip="Reîncarcă"' "$app/shared/ui/refresh-button.ts" &&
    in_file 'prefers-reduced-motion: reduce' "$app/shared/ui/refresh-button.ts" &&
    in_file '<kb-refresh-button' "$tasks_web/task-attachments.ts" &&
    in_file '<kb-refresh-button' "$tasks_web/task-timeline.ts" &&
    in_file 'a estimat task-ul la {{ line.change }}' "$tasks_web/task-timeline.ts" &&
    in_file 'a pus task-ul pe pauză: «{{ line.change }}»' "$tasks_web/task-timeline.ts" &&
    in_file "\$localize\`Estimarea\`" "$tasks_web/task-history.ts" &&
    in_file "task.columnKind === 'Done'" "$tasks_web/task-table.html"
}

the_panel_specs_pass() {
  specs_named "$app/features/tasks/task-panel.spec.ts" \
    'asks for an estimate before a task without one goes into În lucru, and moves with it' \
    'puts the select back, asking the API nothing, when the estimate is turned down' \
    'asks why before a pause, and shows the reason under Stare' \
    'turns off the columns before În lucru for a started task' \
    'edits the estimate in place: Enter saves it in minutes, with a comma' \
    'refuses an estimate out of limits under its field, and Esc goes back without closing' \
    'shows the time worked, counting now in În lucru, against the estimate, danger once over' \
    'reads the task again for its files from Reîncarcă on Atașamente' || return 1
  specs_named "$app/features/tasks/task-timeline.spec.ts" \
    'writes a first estimate, a change of it and a pause with its reason' \
    'reads the history again from Reîncarcă, the button off and turning while it reads' || return 1
  specs_pass 'src/app/features/tasks/*.spec.ts' 154
}

# "Setări generale" (UI §7.9, §5): /settings for settings.manage, in a chunk of its own, the sidebar's entry
# and the roles screen's group, the card's texts, and the 409's.
the_settings_page_is_as_the_spec_says() {
  settings_web=$app/features/settings
  in_file "settingsManage: 'settings.manage'," "$app/core/auth/permissions.ts" &&
    in_file '...withPermission(PERMISSIONS.settingsManage, {' "$app/app.routes.ts" &&
    in_file "import('./features/settings/settings.routes')" "$app/app.routes.ts" &&
    in_file "link: '/settings'," "$app/core/layout/navigation.ts" &&
    in_file "icon: 'settings'," "$app/core/layout/navigation.ts" &&
    in_file '[PERMISSIONS.settingsManage, $localize`Schimbă setările generale`]' "$app/features/roles/permission-groups.ts" &&
    in_file 'Program de lucru' "$settings_web/settings-page.html" &&
    in_file 'Timpul lucrat pe task-uri se numără doar în acest program.' "$settings_web/settings-page.html" &&
    in_file 'class="kb-filter-chip day"' "$settings_web/settings-page.html" &&
    in_file 'type="time"' "$settings_web/settings-page.html" &&
    in_file 'Se schimbă din configurația serverului.' "$settings_web/settings-page.html" &&
    in_file 'Alege cel puțin o zi.' "$settings_web/settings-page.ts" &&
    in_file 'Ora de început trebuie să fie înaintea celei de sfârșit.' "$settings_web/settings-page.ts" &&
    in_file 'Programul de lucru a fost salvat.' "$settings_web/settings-page.ts" &&
    in_file 'Programul a fost schimbat între timp de altcineva.' "$app/core/api/error-interceptor.ts"
}

the_settings_specs_pass() {
  specs_pass 'src/app/features/settings/*.spec.ts' 6 &&
    specs_named "$app/core/layout/layout.spec.ts" 'adds Setări generale last in Administrare for settings.manage' &&
    specs_pass 'src/app/features/roles/*.spec.ts' 24 &&
    specs_pass 'src/app/core/layout/*.spec.ts' 20
}

# The production build, once per run: no warning, the Initial total under 750 kB, and the new pages and
# sections (the settings page, the reports' new section) in chunks of their own, not in what index.html loads.
built_web() {
  [ -f "$scratch/web-built" ] && return 0
  (cd "$web" && npx ng build --output-path "$scratch/web-dist") >"$scratch/web-build.log" 2>&1 || {
    plain "$scratch/web-build.log" | tail -n 25
    return 1
  }
  touch "$scratch/web-built"
}

the_build_is_clean_and_under_750_kb() {
  built_web || return 1
  if plain "$scratch/web-build.log" | grep -F 'WARNING'; then
    return 1
  fi
  total=$(plain "$scratch/web-build.log" | sed -n 's/.*Initial total *| *\([0-9.]*\) kB.*/\1/p')
  [ -n "$total" ] || {
    echo "no Initial total in the build's output"
    return 1
  }
  echo "Initial total $total kB"
  node -e "process.exit(Number(process.argv[1]) < 750 ? 0 : 1)" "$total"
}

lazy_in_a_chunk() { # lazy_in_a_chunk <text the chunk holds>: in a file of the build, none of those index.html loads
  built_web || return 1
  initial=$(grep -oE '<script src="[^"]*"|<link rel="modulepreload" href="[^"]*"' "$scratch/web-dist/browser/index.html" |
    sed 's/.*"\([^"]*\)"$/\1/')
  holders=$(grep -lF -- "$1" "$scratch/web-dist/browser/"*.js 2>/dev/null)
  [ -n "$holders" ] || {
    echo "no chunk holds $1"
    return 1
  }
  for file in $initial; do
    if grep -qF -- "$1" "$scratch/web-dist/browser/$file"; then
      echo "$1 is in $file, which index.html loads"
      return 1
    fi
  done
}

# The reports' fourth section (UI §7.8): "Estimare și timp lucrat", its tiles, chart, tables, CSV and list.
the_work_time_section_is_as_the_spec_says() {
  reports_web=$app/features/reports
  in_file 'Estimare și timp lucrat' "$reports_web/reports-page.html" &&
    in_file "'/reports/work-time'" "$reports_web/reports-api.ts" &&
    in_file "'/settings/work-schedule'" "$reports_web/reports-api.ts" &&
    in_file 'Timpul lucrat se numără doar în programul de lucru (${scheduleText(schedule)}:schedule:).' "$reports_web/reports-page.ts" &&
    in_file 'label="Estimat"' "$reports_web/reports-page.html" &&
    in_file 'label="Lucrat"' "$reports_web/reports-page.html" &&
    in_file 'label="Abatere"' "$reports_web/reports-page.html" &&
    in_file 'label="Peste estimare"' "$reports_web/reports-page.html" &&
    in_file 'Task-uri peste estimare' "$reports_web/reports-page.html" &&
    in_file 'Niciun task finalizat cu estimare în acest interval.' "$reports_web/reports-page.ts" &&
    in_file "this.save('estimare'," "$reports_web/reports-page.ts" &&
    in_file "const sign = percent > 0 ? '+' : percent < 0 ? '−' : '';" "$reports_web/reports-page.ts" &&
    in_file "tone: 'secondary'," "$reports_web/reports-page.ts"
}

the_work_time_specs_pass() {
  specs_named "$app/features/reports/reports-page.spec.ts" \
    'comes fourth, with the schedule of now under its title' \
    'shows Estimat, Lucrat, Abatere and Peste estimare, the deviation with its sign' \
    'writes a deviation under the estimate with a true minus, and none as a dash' \
    'draws estimated and worked by person, in hours, in the charts colors' \
    'lists the tasks over their estimate, at most 20, each opening its task' \
    'says when no finished task had an estimate' \
    'exports the tables as CSV, named by what it holds and its days' \
    'reads only its own section again on Reîncearcă' &&
    specs_pass 'src/app/features/reports/*.spec.ts' 71
}

task_5() {
  echo "22.5 the frontend"
  check "the fixed columns' bars and icons reach 3:1 on the column, in light and in dark" fixed_colors_reach_3_to_1
  check "no token is new since the group's base" no_token_is_new
  check "the fixed columns wear their kind: bar, icon, no menu, + Task in Backlog only, no going back" the_fixed_columns_wear_their_kind
  check "the board's specs pass: the fixed columns, the columns of the project, no going back" the_board_specs_pass
  check "the estimate's and the pause's sheets ask as UI §7.4 says, 440px, shared with the task's panel" the_sheets_ask_as_the_spec_says
  check "the card shows the time worked against the estimate, and in În pauză the reason" the_card_shows_the_work_and_the_pause
  check "the specs of the questions pass: the sheets, the board's moves by mouse and keyboard, the store" the_asking_specs_pass
  check "Arhivate: for tasks.archive, last, a 44px band or 300px, its state kept, 50 at a time, archive and restore" the_archived_column_is_as_the_spec_says
  check "the specs of Arhivate pass: the band, the pages, archiving by a drop, restoring by a drop or the ⋮" the_archived_specs_pass
  check "the task's panel: Estimare in place, Timp lucrat, Motivul pauzei, Stare's rules, Reîncarcă, the history's lines" the_panel_is_as_the_spec_says
  check "the specs of the panel pass: the estimate, the worked time, the pause, the moves, the tabs' Reîncarcă" the_panel_specs_pass
  check "Setări generale: /settings for settings.manage, the sidebar, the roles screen's group, the card's texts" the_settings_page_is_as_the_spec_says
  check "the specs of the settings pass: the page, the sidebar, the roles screen" the_settings_specs_pass
  check "the production build warns of nothing, its Initial total under 750 kB" the_build_is_clean_and_under_750_kb
  check "the settings page comes in a chunk of its own, not in the first bundle" lazy_in_a_chunk 'kb-settings-page'
  check "the reports' Estimare și timp lucrat: its tiles, the schedule of now, chart, tables, CSV and overruns" the_work_time_section_is_as_the_spec_says
  check "the specs of the reports pass, Estimare și timp lucrat among them" the_work_time_specs_pass
  check "the new section comes with the reports' page, not in the first bundle" lazy_in_a_chunk 'Estimare și timp lucrat'
}

# ---- Task 6: one task from Backlog to Gata on the real API, heard by the boards over the hub.

flow_tests_pass() {
  classes_pass Kanabol.Application.Tests 13 BoardEventsHandlerTests &&
    classes_pass Kanabol.Api.Tests 12 BoardRealtimeFlowTests BoardSenderTests
}

# check-realtime.sh's client, with answers kept: a request may keep its answer under a name, whose id a later
# step's path or body gives as {name}.
hub_client() {
  [ -f "$scratch/hub.mjs" ] && return 0
  cat >"$scratch/hub.mjs" <<'JS'
// node hub.mjs <API address> <steps file>: runs the steps and writes what happened as JSON. A step connects a
// name with a token, joins a project's board, waits, or sends a request to the API, with the connection id of
// one of the names in X-Connection-Id when asked; a request may keep its answer under a name, whose id a later
// step's path or body gives as {name}. The answers kept, and what each connection heard, come last.
import { readFileSync } from 'node:fs';

const RS = '\x1e';
const [base, stepsFile] = process.argv.slice(2);
const steps = JSON.parse(readFileSync(stepsFile, 'utf8'));
const kept = {};
const filled = (text) => text.replace(/\{(\w+)\}/g, (whole, name) => kept[name]?.id ?? whole);

async function connect(token) {
  const query = `access_token=${encodeURIComponent(token)}`;
  const negotiated = await fetch(`${base}/hubs/board/negotiate?negotiateVersion=1&${query}`, { method: 'POST' });
  if (negotiated.status !== 200) throw new Error(`negotiate answered ${negotiated.status}`);
  const { connectionId, connectionToken } = await negotiated.json();
  const socket = new WebSocket(`${base.replace(/^http/, 'ws')}/hubs/board?id=${encodeURIComponent(connectionToken)}&${query}`);
  const heard = [];
  const waiting = new Map();
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
      } else if (message.type === 3) {
        waiting.get(message.invocationId)?.(message);
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
  let next = 0;
  return {
    connectionId,
    heard,
    async invoke(target, ...args) {
      const invocationId = String(++next);
      const done = new Promise((resolve) => waiting.set(invocationId, resolve));
      socket.send(JSON.stringify({ type: 1, invocationId, target, arguments: args }) + RS);
      const completion = await done;
      if (completion.error) throw new Error(completion.error);
      return completion.result;
    },
    close: () => socket.close(),
  };
}

const hubs = {};
const result = { connections: {}, steps: [], kept, heard: {} };
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
    } else if (step.join) {
      // The hub answers null for a join, or the refusal's code (docs/SPEC.md §9).
      try {
        const refusal = await hubs[step.join].invoke('JoinProject', step.project);
        result.steps.push(refusal === null ? 'joined' : `refused: ${refusal}`);
      } catch (error) {
        result.steps.push(`failed: ${error.message}`);
      }
    } else if (step.request) {
      const headers = { Authorization: `Bearer ${step.token}`, 'Content-Type': 'application/json' };
      if (step.as) headers['X-Connection-Id'] = hubs[step.as].connectionId;
      const answer = await fetch(`${base}${filled(step.request)}`, {
        method: step.method,
        headers,
        body: step.body === undefined ? undefined : filled(JSON.stringify(step.body)),
      });
      result.steps.push(String(answer.status));
      const text = await answer.text();
      if (step.keep) kept[step.keep] = text ? JSON.parse(text) : null;
    } else if (step.wait) {
      await new Promise((resolve) => setTimeout(resolve, step.wait));
    }
  }
  // Whatever was sent has reached the sockets by the time the requests answered; this is for the wire.
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

flow() { # flow <JavaScript expression on r, the flow's result, in ASCII> [argument]: prints its value; the argument is process.argv[3]
  node -e "const r = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/flow-result.json")" "$@" | tr -d '\r'
}

pause_reason='Aștept răspunsul clientului'

# CAP, the admin's, with Dan a plain member, and ALT, another of the admin's; every hour worked.
a_project_followed_by_two_boards() {
  the_admin_signs_in || return 1
  [ -n "${cap:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Cap-coadă","key":"CAP"}')" || return 1
  cap_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Alături","key":"ALT"}')" || return 1
  alt=$(body_field id)
  person_signs_in "dan@example.com" "Dan Ascultă" '["tasks.create"]' || return 1
  dan_token=$person_token
  dan_id=$(in_db "SELECT id FROM users WHERE email = 'dan@example.com'")
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  status_is 201 "$(as "$admin_token" POST "/api/projects/$cap_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")" || return 1
  for kind in InProgress Paused Done; do
    eval "cap_$kind=\$(in_db \"SELECT id FROM board_columns WHERE project_id = '\$cap_id' AND kind = '\$kind'\")"
  done
  every_hour_is_worked || return 1
  cap=$cap_id
}

# CAP-1, created for Dan, into În lucru (first without an estimate), on pause, archived, restored, back in
# În lucru and into Gata, every request from Ana's board, the admin's; Dan's board and one of ALT listen. Once
# per run: the checks after it read what it left.
the_flow_runs() {
  a_project_followed_by_two_boards || return 1
  [ -s "$scratch/flow-result.json" ] && return 0
  from_ana="\"token\": \"$admin_token\", \"as\": \"ana\""
  cat >"$scratch/flow-steps.json" <<JSON
[
  { "connect": "ana", "token": "$admin_token" },
  { "connect": "dan", "token": "$dan_token" },
  { "connect": "alt", "token": "$admin_token" },
  { "join": "ana", "project": "$cap" },
  { "join": "dan", "project": "$cap" },
  { "join": "alt", "project": "$alt" },
  { "request": "/api/projects/$cap/tasks", "method": "POST", $from_ana, "keep": "task",
    "body": { "title": "Cap-coadă", "priority": "Medium", "assigneeIds": ["$dan_id"] } },
  { "request": "/api/tasks/{task}/position", "method": "PATCH", $from_ana, "keep": "refused",
    "body": { "columnId": "$cap_InProgress" } },
  { "request": "/api/tasks/{task}/position", "method": "PATCH", $from_ana,
    "body": { "columnId": "$cap_InProgress", "estimatedMinutes": 30 } },
  { "wait": 2500 },
  { "request": "/api/tasks/{task}/position", "method": "PATCH", $from_ana, "keep": "paused",
    "body": { "columnId": "$cap_Paused", "pauseReason": "$pause_reason" } },
  { "request": "/api/tasks/{task}", "method": "DELETE", $from_ana },
  { "request": "/api/projects/$cap/board/archived", "method": "GET", "token": "$admin_token", "keep": "archived" },
  { "request": "/api/tasks/{task}/restore", "method": "POST", $from_ana, "keep": "restored" },
  { "request": "/api/tasks/{task}/position", "method": "PATCH", $from_ana, "body": { "columnId": "$cap_InProgress" } },
  { "wait": 1500 },
  { "request": "/api/tasks/{task}/position", "method": "PATCH", $from_ana, "keep": "done",
    "body": { "columnId": "$cap_Done" } }
]
JSON
  hub_client || return 1
  node "$(native "$scratch/hub.mjs")" "$API_URL" "$(native "$scratch/flow-steps.json")" >"$scratch/flow.json" 2>"$scratch/flow-error.log" || {
    cat "$scratch/flow-error.log"
    return 1
  }
  mv "$scratch/flow.json" "$scratch/flow-result.json"
}

the_flow_ran() {
  [ -s "$scratch/flow-result.json" ] || {
    echo "the flow didn't run"
    return 1
  }
  task=$(flow r.kept.task.id)
}

the_flow_answers_as_the_spec_says() {
  the_flow_ran || return 1
  is 'connected|connected|connected|joined|joined|joined|201|400|200|200|204|200|200|200|200' "$(flow "r.steps.join('|')")" || {
    head -c 1500 "$scratch/flow-result.json"
    return 1
  }
  is 'CAP-1 Backlog null false' "$(flow "(t => [t.key, t.columnName, String(t.estimatedMinutes), t.isWorking].join(' '))(r.kept.task)")" || return 1
  is estimatedMinutes "$(flow "Object.keys(r.kept.refused.errors).join(',')")"
}

# Ana's board, whose requests these are, and Dan's: the same messages, in the order the requests went, each of
# CAP-1 by the admin from Ana's connection; nothing of the 400. The estimate comes before its move.
the_boards_hear_every_step_in_order() {
  the_flow_ran || return 1
  expected="TaskCreated,TaskUpdated:Assignees,TaskUpdated:EstimatedMinutes,TaskMoved:$cap_InProgress,TaskMoved:$cap_Paused,TaskArchived,TaskRestored,TaskMoved:$cap_InProgress,TaskMoved:$cap_Done"
  for board in ana dan; do
    is "$expected" "$(flow "r.heard[process.argv[3]].filter((h) => h.event !== 'NotificationCreated').map((h) => h.event + (h.payload.field ? ':' + h.payload.field : '') + (h.event === 'TaskMoved' ? ':' + h.payload.columnId : '')).join(',')" "$board")" || return 1
    is true "$(flow "r.heard[process.argv[3]].filter((h) => h.event !== 'NotificationCreated').every((h) => h.payload.taskId === r.kept.task.id && h.payload.projectId === '$cap' && h.payload.byUserId === '$admin_id' && h.payload.originConnectionId === r.connections.ana)" "$board")" || return 1
  done
}

only_whom_it_concerns_hears() {
  the_flow_ran || return 1
  is '1 0 0' "$(flow "[r.heard.dan, r.heard.ana, r.heard.alt].map((h) => h.filter((e) => e.event === 'NotificationCreated').length).join(' ')")" || return 1
  is "$(in_db "SELECT id FROM notifications WHERE user_id = '$dan_id' AND type = 'TaskAssigned'")" \
    "$(flow "r.heard.dan.find((e) => e.event === 'NotificationCreated').payload.notificationId")" || return 1
  is 0 "$(flow r.heard.alt.length)"
}

# 2.5 seconds in În lucru, then 1.5: the pause, the archive and the restore count nothing and keep what was.
the_worked_time_counts_only_in_in_progress() {
  the_flow_ran || return 1
  paused=$(flow r.kept.paused.workedSeconds)
  at_least 2 "$paused" || return 1
  is 'false 30' "$(flow "r.kept.paused.isWorking + ' ' + r.kept.paused.estimatedMinutes")" || return 1
  is "false $paused" "$(flow "r.kept.restored.isWorking + ' ' + r.kept.restored.workedSeconds")" || return 1
  finished=$(flow r.kept.done.workedSeconds)
  at_least $((paused + 1)) "$finished" || return 1
  is 'false true null 30' "$(flow "(t => [t.isWorking, t.completedAt !== null, String(t.pauseReason), t.estimatedMinutes].join(' '))(r.kept.done)")" || return 1
  is "$finished|f" "$(in_db "SELECT worked_seconds, work_started_at IS NOT NULL FROM task_items WHERE id = '$task'" | sed 's/|False$/|f/')"
}

arhivate_held_it_while_archived() {
  the_flow_ran || return 1
  paused=$(flow r.kept.paused.workedSeconds)
  is "1 $task $cap_Paused 30 false $paused $admin_id" "$(flow "(c => [r.kept.archived.items.length, c.id, c.columnId, c.estimatedMinutes, c.isWorking, c.workedSeconds, c.archivedBy.userId].join(' '))(r.kept.archived.items[0])")" || return 1
  is "În pauză|$pause_reason" "$(flow "r.kept.archived.items[0].columnName + '|' + r.kept.archived.items[0].pauseReason")" || return 1
  is "$cap_Paused|$pause_reason" "$(flow "r.kept.restored.columnId + '|' + r.kept.restored.pauseReason")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$cap/board/archived")" || return 1
  is 0 "$(json b.items.length)"
}

the_board_and_the_history_agree() {
  the_flow_ran || return 1
  finished=$(flow r.kept.done.workedSeconds)
  status_is 200 "$(as "$admin_token" GET "/api/projects/$cap/board")" || return 1
  is "$cap_Done 30 $finished false null" "$(json "(t => [t.columnId, t.estimatedMinutes, t.workedSeconds, t.isWorking, String(t.pauseReason)].join(' '))(b.tasks.find((t) => t.id === process.argv[3]))" "$task")" || return 1
  is 'ColumnId,EstimatedMinutes,ColumnId,PauseReason,ArchivedAt,ArchivedAt,ColumnId,ColumnId' \
    "$(in_db "SELECT string_agg(field, ',' ORDER BY changed_at, field) FROM task_history_entries WHERE task_id = '$task'")" || return 1
  is "30|$pause_reason" "$(in_db "SELECT new_value FROM task_history_entries WHERE task_id = '$task' AND field IN ('EstimatedMinutes', 'PauseReason') ORDER BY field" | paste -sd '|' -)"
}

# The cycle time from the first entry into În lucru, which holds the pause and the archive, so at least the
# time worked, and at most the lead time.
the_reports_count_it() {
  the_flow_ran || return 1
  finished=$(flow r.kept.done.workedSeconds)
  status_is 200 "$(as "$admin_token" GET "/api/reports/work-time?$(around_now)&projectId=$cap")" || return 1
  is "1 1 1800 $finished 0 0 true" "$(json "[b.count, b.estimatedCount, b.estimatedSeconds, b.workedSeconds, b.overCount, b.over.length, b.deviation < 0].join(' ')")" || return 1
  is "$dan_id 1 1800 $finished" "$(json "b.byUser.map((u) => [u.userId, u.count, u.estimatedSeconds, u.workedSeconds].join(' ')).join(';')")" || return 1
  is "CAP 1 1800 $finished" "$(json "b.byProject.map((p) => [p.key, p.count, p.estimatedSeconds, p.workedSeconds].join(' ')).join(';')")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/reports/cycle-time?$(around_now)&projectId=$cap")" || return 1
  is '1 true true' "$(json "[b.count, b.cycleTime.averageSeconds >= Number(process.argv[3]), b.cycleTime.averageSeconds <= b.leadTime.averageSeconds].join(' ')" "$finished")"
}

notes_say() { # notes_say <file> <text...>: each text, as it is, in the file
  notes=$1
  shift
  for text in "$@"; do
    in_file "$text" "$notes" || {
      echo "$notes doesn't say $text"
      return 1
    }
  done
}

the_backend_notes_are_up_to_date() {
  notes_say backend/CLAUDE.md '`GET /api/projects/{id}/board-columns`, `GET …/board-columns/{columnId}` | The columns, from the left, each with its `kind`' \
    '`Column.Fixed`' '`Task.CannotMoveBack`' '`PUT /api/tasks/{id}/estimate`' '`GET /api/projects/{id}/board/archived?page=1&pageSize=50`' \
    '`canSeeArchived`' '`WorkingTime.Between`' '`AddWorkedTime`' '`GET /api/settings/work-schedule`' '`PUT /api/settings/work-schedule`' \
    '`Settings.ChangedMeanwhile`' '| `Schedule__TimeZone` ' '`GET /api/reports/work-time?from=' '`EstimatedMinutes`, `Assignees`' \
    '`scripts/check-columns.sh [task-number...]`' || return 1
  lacks 'and `isDone`|Column\.LastColumn|MarkColumnDone|done column,|starts in the first one|with the default columns' backend/CLAUDE.md
}

the_frontend_notes_are_up_to_date() {
  notes_say frontend/CLAUDE.md '`cdkDropListEnterPredicate`' '`MoveQuestions`' '`EstimateSheet`' '`PauseSheet`' '`kb-archived-column`' \
    '`kb.board.archived`' '`kb-refresh-button`' '`SettingsPage`' '`ReportsStore.workTime`' '`scripts/check-columns.sh 5`' \
    'is 707 kB after group 22' || return 1
  lacks 'isDone|LastColumn|Marchează ca terminată|is 706 kB' frontend/CLAUDE.md
}

task_6() {
  use_database "$api_db"
  echo "22.6 one task from Backlog to Gata on the real API, heard by the boards"
  check "Docker is running" docker_is_running
  check "the tests pass: the board's events, their sender and their flows over the hub" flow_tests_pass
  check "the API builds from this code, whatever the tests built" api_builds
  check "the real API in Production makes CAP, with Dan a plain member, and ALT, every hour worked" a_project_followed_by_two_boards
  check "Ana's board takes CAP-1 from Backlog through În lucru, a pause and the archive to Gata" the_flow_runs
  check "each request answers as the spec says, În lucru without an estimate 400 on it" the_flow_answers_as_the_spec_says
  check "both boards of CAP hear every step in order, from Ana's connection, and nothing of the 400" the_boards_hear_every_step_in_order
  check "Dan hears of his assignment once, Ana of nothing, and ALT's board nothing at all" only_whom_it_concerns_hears
  check "the worked time counts only in În lucru, kept through the pause, the archive and the restore" the_worked_time_counts_only_in_in_progress
  check "Arhivate held it, from În pauză with its reason, and lets it go at the restore" arhivate_held_it_while_archived
  check "the board and the history tell it the same way" the_board_and_the_history_agree
  check "work-time and cycle-time count it: the estimate and the time worked, by Dan and by CAP" the_reports_count_it
  check "backend/CLAUDE.md says the fixed columns, the moves, the worked time, Arhivate and the settings" the_backend_notes_are_up_to_date
  check "frontend/CLAUDE.md says the board's rules, the sheets, Arhivate, the settings and the first bundle" the_frontend_notes_are_up_to_date
}

# ---- Task 7: group 23, the columns' permission, dragging the columns and the fixes of group 22.

group_23_tests_pass() {
  classes_pass Kanabol.Domain.Tests 30 PermissionsTests ProjectColumnTests &&
    classes_pass Kanabol.Application.Tests 95 ProjectAccessTests BoardColumnCommandHandlerTests GetBoardQueryHandlerTests &&
    classes_pass Kanabol.Api.Tests 20 BoardColumnsEndpointTests BoardEndpointTests BoardRealtimeFlowTests &&
    classes_pass Kanabol.Application.Tests 31 BoardEventsHandlerTests GetArchivedTasksQueryHandlerTests WorkedTimeHandlerTests &&
    classes_pass Kanabol.Api.Tests 5 ArchivedTasksEndpointTests
}

# Someone with a role of their own, signed in from an address of their own (5 logins a minute per address):
# their token in person_token, their id in person_id and their role's in person_role.
someone_signs_in() { # someone_signs_in <email> <name> <permissions as a JSON list> <last byte of the address>
  the_admin_signs_in || return 1
  status_is 201 "$(as "$admin_token" POST /api/roles "{\"name\":\"Rol $2\",\"permissions\":$3}")" || return 1
  person_role=$(body_field id)
  status_is 201 "$(as "$admin_token" POST /api/users "{\"email\":\"$1\",\"displayName\":\"$2\",\"roleId\":\"$person_role\",\"initialPassword\":\"$admin_password\"}")" || return 1
  person_id=$(body_field id)
  status_is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' -H "X-Forwarded-For: 10.78.23.$4" \
    -d "{\"email\":\"$1\",\"password\":\"$admin_password\"}")" || return 1
  status_is 200 "$(send POST /api/auth/change-password -H "Authorization: Bearer $(body_field accessToken)" \
    -H 'Content-Type: application/json' -d "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"parola lui $1 pentru coloane\"}")" || return 1
  person_token=$(body_field accessToken)
}

board_says() { # board_says <token> <project id>: canManage and canManageColumns of the board, as "true|false"
  status_is 200 "$(as "$1" GET "/api/projects/$2/board")" >/dev/null || return 1
  json "[b.canManage, b.canManageColumns].join('|')"
}

# PRM, the admin's, with Mara (columns.manage) and Petru (tasks.create) as plain members, and Vera
# (columns.manage and projects.view_all) outside it. Once per run.
a_project_whose_columns_a_member_manages() {
  the_admin_signs_in || return 1
  [ -n "${prm:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Permisiunea coloanelor","key":"PRM"}')" || return 1
  prm_id=$(body_field id)
  someone_signs_in "mara@example.com" "Mara Coloane" '["columns.manage"]' 11 || return 1
  mara_token=$person_token
  mara_role=$person_role
  mara_id=$person_id
  status_is 201 "$(as "$admin_token" POST "/api/projects/$prm_id/members" "{\"userId\":\"$person_id\",\"role\":\"Member\"}")" || return 1
  someone_signs_in "petru@example.com" "Petru Fără Coloane" '["tasks.create"]' 12 || return 1
  petru_token=$person_token
  status_is 201 "$(as "$admin_token" POST "/api/projects/$prm_id/members" "{\"userId\":\"$person_id\",\"role\":\"Member\"}")" || return 1
  someone_signs_in "vera@example.com" "Vera Din Afară" '["columns.manage","projects.view_all"]' 13 || return 1
  vera_token=$person_token
  prm=$prm_id
}

columns_manage_is_seeded_and_the_admins() {
  the_admin_signs_in || return 1
  is 'columns.manage|t' "$(in_db "SELECT p.name, bool_or(r.is_system) FROM permissions p JOIN role_permissions rp ON rp.permission_id = p.id JOIN roles r ON r.id = rp.role_id WHERE p.name = 'columns.manage' GROUP BY p.name" | sed 's/|True$/|t/')"
}

the_board_says_who_manages_the_columns() {
  a_project_whose_columns_a_member_manages || return 1
  is 'true|true' "$(board_says "$admin_token" "$prm")" || return 1
  is 'false|true' "$(board_says "$mara_token" "$prm")" || return 1
  is 'false|false' "$(board_says "$petru_token" "$prm")" || return 1
  is 'false|false' "$(board_says "$vera_token" "$prm")"
}

a_member_with_columns_manage_changes_the_columns() {
  a_project_whose_columns_a_member_manages || return 1
  status_is 201 "$(as "$mara_token" POST "/api/projects/$prm/board-columns" '{"name":"Review"}')" || return 1
  review=$(body_field id)
  status_is 201 "$(as "$mara_token" POST "/api/projects/$prm/board-columns" '{"name":"QA"}')" || return 1
  qa=$(body_field id)
  status_is 200 "$(as "$mara_token" PUT "/api/projects/$prm/board-columns/$review" '{"name":"În review"}')" || return 1
  status_is 200 "$(as "$mara_token" PATCH "/api/projects/$prm/board-columns/$review/position" '{"position":4}')" || return 1
  status_is 204 "$(as "$mara_token" DELETE "/api/projects/$prm/board-columns/$qa")" || return 1
  is 'Backlog:Backlog,În lucru:InProgress,În pauză:Paused,În review:Custom,Gata:Done' "$(columns_of "$prm")"
}

# A plain member and someone outside the project, columns.manage or not, get the owner's 403 and change nothing.
the_others_change_no_column() {
  a_project_whose_columns_a_member_manages || return 1
  before=$(columns_of "$prm")
  code_is 403 Project.OwnerRequired "$(as "$petru_token" POST "/api/projects/$prm/board-columns" '{"name":"Petru"}')" || return 1
  code_is 403 Project.OwnerRequired "$(as "$vera_token" POST "/api/projects/$prm/board-columns" '{"name":"Vera"}')" || return 1
  is "$before" "$(columns_of "$prm")"
}

# Read from the database: Mara's token still says columns.manage once her role has lost it.
columns_manage_is_read_from_the_database() {
  a_project_whose_columns_a_member_manages || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/roles/$mara_role" '{"name":"Rol Mara Coloane","permissions":[]}')" || return 1
  code_is 403 Project.OwnerRequired "$(as "$mara_token" POST "/api/projects/$prm/board-columns" '{"name":"Târziu"}')" || return 1
  is 'false|false' "$(board_says "$mara_token" "$prm")" || return 1
  status_is 200 "$(as "$admin_token" PUT "/api/roles/$mara_role" '{"name":"Rol Mara Coloane","permissions":["columns.manage"]}')"
}

crs_task() { # crs_task <title> <column id>: a task of CRS moved into that column, its id in crs_moved
  status_is 201 "$(as "$admin_token" POST "/api/projects/$crs/tasks" "{\"title\":\"$1\",\"description\":\"\",\"priority\":\"Low\"}")" || return 1
  crs_moved=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$crs_moved/position" "{\"columnId\":\"$2\"}")"
}

# CRS: Analiză before În lucru and Review after În pauză, each holding a task, and Gol, empty.
a_column_with_tasks_stays_on_its_side_of_in_progress() {
  the_admin_signs_in || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Peste În lucru","key":"CRS"}')" || return 1
  crs=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$crs/board-columns" '{"name":"Analiză"}')" || return 1
  crs_analysis=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_analysis/position" '{"position":1}')" || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$crs/board-columns" '{"name":"Review"}')" || return 1
  crs_review=$(body_field id)
  crs_task 'De analizat' "$crs_analysis" || return 1
  crs_task 'De verificat' "$crs_review" || return 1
  crs_reviewed=$crs_moved
  # Review back before În lucru, Analiză forward after it: 409, nothing moved; a place before Backlog is 400 first.
  code_is 409 Column.CrossesInProgress "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_review/position" '{"position":1}')" || return 1
  code_is 409 Column.CrossesInProgress "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_analysis/position" '{"position":2}')" || return 1
  status_is 400 "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_review/position" '{"position":0}')" || return 1
  is 'Backlog:Backlog,Analiză:Custom,În lucru:InProgress,În pauză:Paused,Review:Custom,Gata:Done' "$(columns_of "$crs")" || return 1
  # On its side of În lucru it moves: Review between În lucru and În pauză.
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_review/position" '{"position":3}')" || return 1
  is 'Backlog:Backlog,Analiză:Custom,În lucru:InProgress,Review:Custom,În pauză:Paused,Gata:Done' "$(columns_of "$crs")"
}

# Its task archived, Review still holds it: the 409 stays. Empty, Gol crosses În lucru both ways.
an_archived_task_holds_it_and_an_empty_one_goes_anywhere() {
  [ -n "${crs_reviewed:-}" ] || a_column_with_tasks_stays_on_its_side_of_in_progress || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$crs_reviewed")" || return 1
  code_is 409 Column.CrossesInProgress "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_review/position" '{"position":1}')" || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$crs/board-columns" '{"name":"Gol"}')" || return 1
  crs_empty=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_empty/position" '{"position":1}')" || return 1
  is 'Backlog:Backlog,Gol:Custom,Analiză:Custom,În lucru:InProgress,Review:Custom,În pauză:Paused,Gata:Done' "$(columns_of "$crs")" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/projects/$crs/board-columns/$crs_empty/position" '{"position":5}')" || return 1
  is 'Backlog:Backlog,Analiză:Custom,În lucru:InProgress,Review:Custom,În pauză:Paused,Gol:Custom,Gata:Done' "$(columns_of "$crs")"
}

# Arhivate, the board's last column, is taken as the four fixed names are, in any case (400 on name).
arhivate_is_a_reserved_name() {
  a_project_whose_columns_a_member_manages || return 1
  before=$(columns_of "$prm")
  column=$(column_id "$prm" 'În review')
  [ -n "$column" ] || return 1
  for name in Arhivate ARHIVATE ' arhivate '; do
    status_is 400 "$(as "$admin_token" POST "/api/projects/$prm/board-columns" "{\"name\":\"$name\"}")" || return 1
    is true "$(json "'name' in b.errors")" || return 1
    status_is 400 "$(as "$admin_token" PUT "/api/projects/$prm/board-columns/$column" "{\"name\":\"$name\"}")" || return 1
    is true "$(json "'name' in b.errors")" || return 1
  done
  is "$before" "$(columns_of "$prm")"
}

heard() { # heard <JavaScript expression on r, the result of the hub's steps, in ASCII>: prints its value
  node -e "const r = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))"     "$(native "$scratch/columns-heard.json")" "$1" | tr -d '
'
}

# Mara's board and Petru's, both of PRM, listen over WebSockets as a browser does; from her tab Mara adds,
# moves, renames and deletes a column, and Petru's 403 comes in between. Petru's board hears ColumnsChanged
# four times, by Mara from her connection, and nothing of the 403; hers hears the same, as her own.
columns_changed_reaches_another_board() {
  a_project_whose_columns_a_member_manages || return 1
  from_mara="\"token\": \"$mara_token\", \"as\": \"mara\""
  cat >"$scratch/columns-steps.json" <<JSON
[
  { "connect": "mara", "token": "$mara_token" },
  { "connect": "petru", "token": "$petru_token" },
  { "join": "mara", "project": "$prm" },
  { "join": "petru", "project": "$prm" },
  { "request": "/api/projects/$prm/board-columns", "method": "POST", $from_mara, "keep": "added", "body": { "name": "Auzită" } },
  { "request": "/api/projects/$prm/board-columns/{added}/position", "method": "PATCH", $from_mara, "body": { "position": 3 } },
  { "request": "/api/projects/$prm/board-columns", "method": "POST", "token": "$petru_token", "as": "petru", "body": { "name": "Petru" } },
  { "request": "/api/projects/$prm/board-columns/{added}", "method": "PUT", $from_mara, "body": { "name": "Auzită de toți" } },
  { "request": "/api/projects/$prm/board-columns/{added}", "method": "DELETE", $from_mara }
]
JSON
  hub_client || return 1
  node "$(native "$scratch/hub.mjs")" "$API_URL" "$(native "$scratch/columns-steps.json")" >"$scratch/columns-heard.json" 2>"$scratch/columns-error.log" || {
    cat "$scratch/columns-error.log"
    return 1
  }
  is 'connected|connected|joined|joined|201|200|403|200|204' "$(heard "r.steps.join('|')")" || return 1
  for listener in mara petru; do
    is 'ColumnsChanged,ColumnsChanged,ColumnsChanged,ColumnsChanged' "$(heard "r.heard.$listener.map((h) => h.event).join(',')")" || return 1
    is true "$(heard "r.heard.$listener.every((h) => Object.keys(h.payload).join(',') === 'projectId,byUserId,originConnectionId' && h.payload.projectId === '$prm' && h.payload.byUserId === '$mara_id' && h.payload.originConnectionId === r.connections.mara)")" || return 1
  done
}

# The board counts the archived tasks for tasks.archive only: CRS has one, archived by the CrossesInProgress
# checks; Petru, without tasks.archive, gets null on PRM.
the_board_counts_the_archived_for_tasks_archive() {
  [ -n "${crs_reviewed:-}" ] || an_archived_task_holds_it_and_an_empty_one_goes_anywhere || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$crs/board")" || return 1
  is 'true 1' "$(json "b.canSeeArchived + ' ' + b.archivedCount")" || return 1
  is "$(in_db "SELECT count(*) FROM task_items WHERE project_id = '$crs' AND archived_at IS NOT NULL")" "$(json "b.archivedCount")" || return 1
  a_project_whose_columns_a_member_manages || return 1
  status_is 200 "$(as "$petru_token" GET "/api/projects/$prm/board")" || return 1
  is 'false null' "$(json "b.canSeeArchived + ' ' + b.archivedCount")"
}

# Arhivate's band counts with the board's archivedCount; the column reads its cards only while open.
the_archived_column_reads_when_it_opens() {
  in_file '[total]="store.archivedCount()"' "$board/board-page.html" &&
    in_file "readonly archivedCount = computed(() => this.board()?.archivedCount ?? 0);" "$board/board-store.ts" &&
    in_file 'if (projectId === null || !this.open()) {' "$board/archived-store.ts" &&
    specs_named "$board/board-page.spec.ts" \
      'reads no card while closed, whatever it hears, and its cards each time it opens' \
      'counts on its band with the board, which reads it again when someone archives' \
      'reads its cards again after a reconnection, while it is open'
}

# SPEC §4.4 (group 23, an explicit check): REI-1, created with 30 minutes, goes into În lucru, on pause after
# two seconds, and back into În lucru without an estimate: 200, the same 30 minutes and no history of them, the
# counter where the pause left it, then going on. Every hour of every day worked, as in task 3.
a_task_with_an_estimate_goes_back_into_in_progress_unasked() {
  the_admin_signs_in || return 1
  every_hour_is_worked || return 1
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Reintră în lucru","key":"REI"}')" || return 1
  rei=$(body_field id)
  rei_in_progress=$(in_db "SELECT id FROM board_columns WHERE project_id = '$rei' AND kind = 'InProgress'")
  rei_paused=$(in_db "SELECT id FROM board_columns WHERE project_id = '$rei' AND kind = 'Paused'")
  status_is 201 "$(as "$admin_token" POST "/api/projects/$rei/tasks" '{"title":"Reintră","description":"","priority":"Low","estimatedMinutes":30}')" || return 1
  rei_task=$(body_field id)
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$rei_task/position" "{\"columnId\":\"$rei_in_progress\"}")" || return 1
  sleep 2
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$rei_task/position" "{\"columnId\":\"$rei_paused\",\"pauseReason\":\"O pauză scurtă\"}")" || return 1
  paused_at=$(json "b.workedSeconds")
  at_least 1 "$paused_at" || return 1
  status_is 200 "$(as "$admin_token" PATCH "/api/tasks/$rei_task/position" "{\"columnId\":\"$rei_in_progress\"}")" || return 1
  is "30 true $paused_at null" "$(json "[b.estimatedMinutes, b.isWorking, b.workedSeconds, String(b.pauseReason)].join(' ')")" || return 1
  sleep 2
  set -- $(worked_of "$rei_task")
  is true "$1" || return 1
  at_least $((paused_at + 1)) "$2" || return 1
  is '' "$(entries_of "$rei_task" | tr ' ' '\n' | grep '^EstimatedMinutes:')"
}

# The refresh tokens' cleanup (group 19) in compose.yaml word for word as docs/SPEC.md §13.3 has it, after the
# overdue check's (group 17) and before the time zone's (group 22), as there.
compose_says_the_cleanup_as_the_spec_does() {
  count=$(grep -cF '# The refresh token cleanup runs once a day by default' docs/SPEC.md)
  [ "$count" -eq 1 ] || {
    echo "docs/SPEC.md has the comment $count times"
    return 1
  }
  line=$(grep -F '# The refresh token cleanup runs once a day by default' docs/SPEC.md)
  [ "$(grep -cxF -- "$line" compose.yaml)" -eq 1 ] || {
    echo "compose.yaml doesn't have, once and as it is, the line: $line"
    return 1
  }
  is 'group 17|group 19|group 22' "$(grep -oE '[(]group (17|19|22)[)]$' compose.yaml | tr -d '()' | paste -sd'|' -)"
}

# Back from În pauză, a card or task with an estimate goes into În lucru without the sheet: the board by the mouse
# and the keyboard, the panel's Stare select.
the_estimate_is_asked_only_once() {
  in_file "if (column.kind === 'InProgress' && task.estimatedMinutes === null) {" "$board/board-page.ts" &&
    in_file "if (column.kind === 'InProgress' && task.estimatedMinutes === null) {" "$app/features/tasks/task-panel.ts" &&
    specs_named "$board/board-page.spec.ts" \
      'moves a paused card with an estimate back into În lucru without asking, by the mouse' \
      'moves a paused card with an estimate back into În lucru without asking, by the keyboard' &&
    specs_named "$app/features/tasks/task-panel.spec.ts" \
      'moves a paused task with an estimate back into În lucru from Stare without asking' &&
    specs_pass "src/app/features/tasks/task-panel.spec.ts" 30
}

the_columns_permission_reaches_the_ui() {
  in_file "columnsManage: 'columns.manage'," "$app/core/auth/permissions.ts" &&
    in_file 'PERMISSIONS.columnsManage,' "$app/features/roles/permission-groups.ts" &&
    in_file '$localize`Administrează coloanele board-ului, în proiectele în care e membru`' "$app/features/roles/permission-groups.ts" &&
    in_file '[canManage]="store.canManageColumns()"' "$board/board-page.html" &&
    in_file '@if (store.canManageColumns()) {' "$board/board-page.html" &&
    in_file 'if (!board?.canManageColumns) {' "$board/board-store.ts" &&
    specs_named "$app/features/roles/permission-groups.spec.ts" 'says what columns.manage lets someone do, among the projects' &&
    specs_named "$board/board-page.spec.ts" 'gives a member with columns.manage the menu and a new column' \
      'gives one who manages the project but not its columns neither the menu nor a new column' &&
    specs_named "$board/board-store.spec.ts" 'changes the columns for a member with columns.manage' \
      'changes nothing for one who manages the project but not its columns'
}

the_board_listens_to_columns_changed() {
  in_file 'ColumnsChanged: ColumnsChangedMessage;' "$app/core/realtime/models.ts" &&
    in_file "this.realtime.on('ColumnsChanged'" "$board/board-store.ts" &&
    specs_named "$board/board-store.spec.ts" 'reads itself again quietly on ColumnsChanged from another tab, and not from its own'
}

group_23_specs_pass() {
  specs_pass "src/app/features/board/**/*.spec.ts" 120 &&
    specs_pass "src/app/features/roles/**/*.spec.ts" 25
}

task_7() {
  use_database "$api_db"
  echo "23.7 the columns' permission, dragging the columns and the fixes of group 22"
  check "Docker is running" docker_is_running
  check "the tests pass: columns.manage, the board's canManageColumns and the column handlers" group_23_tests_pass
  check "the API builds from this code, whatever the tests built" api_builds
  check "the real API in Production seeds columns.manage, which the Admin has" columns_manage_is_seeded_and_the_admins
  check "the board's canManageColumns: the owner and Mara (columns.manage) yes, Petru and Vera (outside) no" the_board_says_who_manages_the_columns
  check "Mara, a member with columns.manage, adds, renames, moves and deletes columns" a_member_with_columns_manage_changes_the_columns
  check "Petru without it and Vera outside the project get 403 Project.OwnerRequired, nothing changed" the_others_change_no_column
  check "columns.manage is read from the database: Mara's token outlives her role's permission" columns_manage_is_read_from_the_database
  check "a column holding tasks doesn't cross În lucru, either way (409 Column.CrossesInProgress), but moves on its side" a_column_with_tasks_stays_on_its_side_of_in_progress
  check "an archived task holds its column there too, and an empty column crosses În lucru both ways" an_archived_task_holds_it_and_an_empty_one_goes_anywhere
  check "Arhivate is a reserved name, in any case: 400 on name to POST and PUT, nothing changed" arhivate_is_a_reserved_name
  check "ColumnsChanged: Petru's board hears each of Mara's four changes from her tab, nothing of his 403" columns_changed_reaches_another_board
  check "the board's archivedCount: the archived tasks of CRS for tasks.archive, null for Petru" the_board_counts_the_archived_for_tasks_archive
  check "compose.yaml has the refresh tokens' cleanup comment of §13.3 word for word, between groups 17 and 22" compose_says_the_cleanup_as_the_spec_does
  check "a task with an estimate goes back from În pauză into În lucru unasked, its counter going on" a_task_with_an_estimate_goes_back_into_in_progress_unasked
  check "the UI: columns.manage on the roles screen, the column's menu and + Coloană on canManageColumns" the_columns_permission_reaches_the_ui
  check "the board reads itself again on ColumnsChanged from another tab" the_board_listens_to_columns_changed
  check "Arhivate's band counts with archivedCount, and its cards are read only when it opens" the_archived_column_reads_when_it_opens
  check "back from În pauză a task with an estimate asks nothing: the board's mouse and keyboard, the panel's Stare" the_estimate_is_asked_only_once
  check "the board's and the roles screen's specs pass" group_23_specs_pass
}

tasks=${*:-1 2 3 4 5 6 7}
for task in $tasks; do
  case "$task" in
    1) task_1 ;;
    2) task_2 ;;
    3) task_3 ;;
    4) task_4 ;;
    5) task_5 ;;
    6) task_6 ;;
    7) task_7 ;;
    *)
      echo "No task $task."
      failures=$((failures + 1))
      ;;
  esac
done

if [ "$failures" -eq 0 ]; then
  echo "All checks passed."
else
  echo "$failures check(s) failed."
  exit 1
fi
