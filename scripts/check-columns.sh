#!/bin/sh
# Acceptance checks for the fixed columns, the estimate, the worked time and the general settings (group 22
# in the project's Tasks.md; docs/SPEC.md §4.4, §5.1, §5.1.1, §13.3, §14.7; docs/UI_UX_SPEC.md §5, §7.4,
# §7.5, §7.8, §7.9). The tests hold the rules, so these checks run them, and add what they can't see: the
# migration over an old database on the local PostgreSQL, the real API process on it, and its answers.
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
  [ -n "$api_pid" ] && return 0
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

the_admin_signs_in() {
  [ -n "$admin_token" ] && return 0
  api_is_up || return 1
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

# Every hour of every day counts first, then none of today's: an open interval goes by the schedule of now.
the_counter_runs_in_in_progress_by_the_schedule() {
  a_board_to_move_on || return 1
  set -- $(bucharest_now)
  if [ "$2" -ge 1437 ]; then
    sleep $(((1440 - $2) * 60 + 5)) # past midnight, so the next seconds stay inside 00:00-23:59
    set -- $(bucharest_now)
  fi
  today=$1
  status_is 200 "$(as "$admin_token" PUT /api/settings/work-schedule '{"days":[1,2,3,4,5,6,7],"start":"00:00","end":"23:59"}')" || return 1
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

tasks=${*:-1 2 3}
for task in $tasks; do
  case "$task" in
    1) task_1 ;;
    2) task_2 ;;
    3) task_3 ;;
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
