#!/bin/sh
# Acceptance checks for the collation and group 19b's leftovers (group 20 in the project's Tasks.md;
# docs/SPEC.md §4.4, §5.3, §13.3, §14.7; docs/UI_UX_SPEC.md §2.1–§2.3, §3.2, §9, §15). The tests hold the
# rules, so these checks run them, and add what they can't see: the migration over an old database on
# the local PostgreSQL, the real API process on it, and its answers.
#
# Usage: scripts/check-collation.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  the ro-x-icu collation: the tests on postgres:17-alpine (the five columns and only they, their
#      indexes rebuilt with it, the migration over old data and back, the list by title and by assignee,
#      the assignees of a task, the search, the projects, the members, the users and the roles in the
#      Romanian order); the five configurations naming Collations.Romanian, no CREATE COLLATION and no
#      Collate() in a query; then, on the local PostgreSQL, a database migrated up to AddTaskAssignees
#      with names and titles starting with Ă, Â, Î, Ș and Ț, which it sorts byte by byte and whose upper()
#      leaves ș alone; the real API in Production migrates it, keeping every row, with the collation on
#      the five columns and their indexes; then its answers: the list by title both ways, by assignee, a
#      task's assignees, the search for ș finding Ș, the projects, the users by email and the roles by
#      name, all in the Romanian order, and the check scripts' own ORDER BY upper(...) giving the API's
#      order, on the collated columns only. Docker, PostgreSQL.
#   2  the retry after a restart of PostgreSQL: the tests on postgres:17-alpine (TaskNumbers run again with
#      the same number when the connection drops in its transaction or its commit fails, a commit whose
#      answer was lost not run again, the board and the assignees told once; the API answering 200 and
#      201 after every connection of its pool was ended with 57P01, as a restart does); Npgsql's
#      EnableRetryOnFailure, TaskNumbers' transaction through the strategy and no other transaction
#      opened by hand; docs/deploy.md asking for no restart of api; then the real API in Production on a
#      database of its own on the local PostgreSQL, its pool holding connections, PostgreSQL stopped and
#      started under it with pg_ctl (from the server's data_directory and BINDIR, or KANABOL_PGDATA and
#      KANABOL_PGBIN): the first request answers 200, and after another restart two tasks get the next
#      numbers, the counter after them, with EF's retries in the log and no error. Docker, PostgreSQL.
#   3  the contrast tokens: --kb-text-500 #697078 in light and #8692ae in dark, --kb-sky-300 #3b6cc7 in dark
#      and --kb-chart-2 still #3d6fc9 there, in _tokens.scss and in the production build's CSS (light once,
#      dark in both of its rules), the old values nowhere; the 30 PNGs of docs/screens/ with text in
#      text-500 or on sky-300 taken again since v0.1.0 (the login page's two have none, and come out the
#      same); and docs/ui-review.md telling of axe-core run again on them in both themes. Only the packages.
#
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database,
# kanabol_collation_check_<pid> and kanabol_collation_retry_<pid>, and drops them. Docker runs the tests' own PostgreSQL. The real API listens
# on port 5279.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
# In the full gate, the build, tests and lint scripts/gate-prepare.sh did once (docs/SPEC.md §14.7).
[ -n "${KANABOL_GATE_CACHE:-}" ] && . scripts/gate-reuse.sh

failures=0
scratch=$(mktemp -d)
check_db="kanabol_collation_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5279
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
base_connection=
created_databases=
pg_bin=
pg_data=
pg_stopped=
migrations=backend/src/Kanabol.Infrastructure/Persistence/Migrations
configurations=backend/src/Kanabol.Infrastructure/Persistence/Configurations
before_collation=20261107000923_AddTaskAssignees

admin_email=admin@kanabol.local
initial_password='o parola initiala de verificare'
admin_password='parola adminului pentru colatie'
jwt_secret=collation-check-secret-with-at-least-32-characters

cleanup() {
  stop_api
  # Task 2 stops the local PostgreSQL under the API: whatever happened, it runs again after.
  [ -z "$pg_stopped" ] || postgres_starts >/dev/null 2>&1
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

collation_tests_pass() {
  classes_pass Kanabol.Application.Tests 15 RomanianOrderTests AddRomanianCollationMigrationTests
}

# The five columns of SPEC §5.3, each with Collations.Romanian, and nothing else with a collation.
configurations_name_the_collation() {
  grep -qF 'public const string Romanian = "ro-x-icu";' backend/src/Kanabol.Infrastructure/Persistence/Collations.cs || {
    echo "Collations.Romanian isn't ro-x-icu"
    return 1
  }
  is 'ProjectConfiguration.cs project.Name
RoleConfiguration.cs role.Name
TaskItemConfiguration.cs task.Title
UserConfiguration.cs user.DisplayName
UserConfiguration.cs user.Email' "$(grep -o 'Property([a-z]* => [a-z]*\.[A-Za-z]*).*UseCollation(Collations\.Romanian)' "$configurations"/*.cs |
    sed 's|^.*/\([A-Za-z]*\.cs\):Property([a-z]* => \([a-z]*\.[A-Za-z]*\)).*|\1 \2|' | sort)" || return 1
  is 5 "$(grep -c 'UseCollation' "$configurations"/*.cs | awk -F: '{ n += $2 } END { print n }')"
}

# initdb makes it, so no migration creates it, and the columns carry it, so no query names it.
nothing_else_names_a_collation() {
  lacks -i 'CREATE COLLATION' "$migrations" || return 1
  lacks 'Collate\(|EF\.Functions\.Collate|COLLATE ' backend/src/Kanabol.Application backend/src/Kanabol.Infrastructure \
    --include='*.cs' --exclude-dir=Migrations --exclude-dir=bin --exclude-dir=obj
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
// The names have ă, Ș and Ț, which the console would turn into a, S and T.
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

# Each task works in a database of its own, check_db, made on first use and dropped at the end.
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

as() { # as <token> <method> <path>: the path in ASCII, Romanian letters percent-encoded, since curl reads its arguments in the ANSI code page
  send "$2" "$3" -H "Authorization: Bearer $1"
}

body_field() { # body_field <name>: the value of the first text field with that name in the last answer
  sed -n "s/^[^\"]*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body" | head -n 1
}

json() { # json <JavaScript expression on b, the last answer, in ASCII>: prints its value
  node -e "const b = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/body")" "$1" | tr -d '\r'
}

status_is() { # status_is <expected status> <actual status>: shows the answer when it differs
  is "$1" "$2" || {
    head -c 600 "$scratch/body" 2>/dev/null
    return 1
  }
}

listed() { # listed <token> <path> <field>: that field of each item of the page, separated by commas
  status_is 200 "$(as "$1" GET "$2")" || return 1
  json "b.items.map(item => item.$3).join(',')"
}

# ---- Task 1: the collation.

old_role=00000000-0000-7000-8000-0000000000a1
old_project=00000000-0000-7000-8000-000000000001
old_column=00000000-0000-7000-8000-0000000000c1
shared_task=00000000-0000-7000-8000-0000000000d0

# In the order of the Romanian alphabet, whatever the case of the first letter.
romanian_titles='Acoperiș,Ăla,Âncă,Inel,îmbinare,Sanie,șanț,ȘTAMPILĂ,Tablă,Țiglă,Zid'
romanian_names='Ana Ene,ăla Mic,Ina Pop,Îon Rus,Sara Dinu,ștefan Pop,Tudor Ilie,Țone Matei,Zoe Ene'
romanian_projects='Acasă,Ărie,Inventar,Îmbunătățiri,Sat,șantier,Teren,Țară,Zonă'
romanian_roles='Admin,Inginer,Îngrijitor,Membru,Supervizor,Șef de echipă,Țesător,Zidar'
romanian_emails='admin@kanabol.local,ana@example.com,ăla@example.com,ina@example.com,îon@example.com,sara@example.com,ștefan@example.com,tudor@example.com,țone@example.com,zoe@example.com'

reversed() { # reversed <comma-separated list>
  printf '%s\n' "$1" | tr ',' '\n' | awk '{ line[NR] = $0 } END { for (i = NR; i > 0; i--) printf "%s%s", line[i], (i > 1 ? "," : "\n") }'
}

local_postgres_has_the_collation() {
  need_database || return 1
  is 'ro-x-icu|i|True' "$(sql "$admin_connection" "SELECT collname, collprovider, collisdeterministic FROM pg_collation WHERE collname = 'ro-x-icu'" | tr -d '\r')"
}

# Every row is made from the end of the alphabet back, so neither ids nor numbers give the order away.
an_old_database_with_romanian_names() {
  need_database || return 1
  api_builds || return 1
  ef database update "$before_collation" --connection "$(connection_to "$check_db")" >"$scratch/old-update.log" 2>&1 || {
    tail -n 15 "$scratch/old-update.log"
    return 1
  }
  # unnest gives each name its place n in the list; ids count down from it. lower() of the email is ICU's,
  # since C would leave Î and Ț in capitals.
  in_db "INSERT INTO roles (id, name, is_system, holders_version, created_at)
    SELECT CASE WHEN name = 'Membru' THEN '$old_role'::uuid ELSE ('00000000-0000-7000-8000-0000000001' || lpad((99 - n)::text, 2, '0'))::uuid END,
      name, false, 0, now()
    FROM unnest(string_to_array('$romanian_roles', ',')) WITH ORDINALITY AS role(name, n) WHERE name <> 'Admin'" || return 1
  in_db "INSERT INTO users (id, email, display_name, password_hash, role_id, is_active, failed_login_attempts, must_change_password, created_at)
    SELECT ('00000000-0000-7000-8000-0000000002' || lpad((99 - n)::text, 2, '0'))::uuid,
      lower(split_part(name, ' ', 1) COLLATE \"ro-x-icu\") || '@example.com', name, 'x', '$old_role', true, 0, false, now()
    FROM unnest(string_to_array('$romanian_names', ',')) WITH ORDINALITY AS person(name, n)" || return 1
  in_db "INSERT INTO projects (id, name, key, next_task_number, created_at)
    SELECT CASE WHEN name = 'Zonă' THEN '$old_project'::uuid ELSE ('00000000-0000-7000-8000-0000000003' || lpad((99 - n)::text, 2, '0'))::uuid END,
      name, 'P' || chr(90 - n::int), 200, now()
    FROM unnest(string_to_array('$romanian_projects', ',')) WITH ORDINALITY AS project(name, n)" || return 1
  in_db "INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES ('$old_column', '$old_project', 'De făcut', 0, false, now())" || return 1
  # Everyone a member, Zoe Ene the owner, so each can be on a task.
  in_db "INSERT INTO project_members (project_id, user_id, role, joined_at)
    SELECT '$old_project', id, CASE WHEN display_name = 'Zoe Ene' THEN 'Owner' ELSE 'Member' END, now() FROM users" || return 1
  # The titles numbered 1 to 49, a task for each person, titled after them, from 50, and one with everyone on it, 99.
  in_db "INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at)
    SELECT ('00000000-0000-7000-8000-0000000004' || lpad((99 - n)::text, 2, '0'))::uuid, '$old_project', '$old_column', 49 - n::int, title, 'Low', n, now()
    FROM unnest(string_to_array('$romanian_titles', ',')) WITH ORDINALITY AS task(title, n)" || return 1
  in_db "INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at)
    SELECT ('00000000-0000-7000-8000-0000000005' || substr(id::text, 35, 2))::uuid, '$old_project', '$old_column',
      50 + row_number() OVER (ORDER BY id), display_name, 'Low', 50 + row_number() OVER (ORDER BY id), now() FROM users" || return 1
  in_db "INSERT INTO task_assignees (task_id, user_id, assigned_at)
    SELECT ('00000000-0000-7000-8000-0000000005' || substr(id::text, 35, 2))::uuid, id, now() FROM users" || return 1
  in_db "INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at)
    VALUES ('$shared_task', '$old_project', '$old_column', 99, 'Al tuturor', 'Low', 99, now())" || return 1
  in_db "INSERT INTO task_assignees (task_id, user_id, assigned_at) SELECT '$shared_task', id, now() FROM users" || return 1
  old_rows >"$scratch/rows-before" || return 1
  is 9 "$(in_db 'SELECT count(*) FROM users')" || return 1
  is "${romanian_emails#"$admin_email",}" "$(in_db "SELECT string_agg(email, ',' ORDER BY email COLLATE \"ro-x-icu\") FROM users")" || return 1
  is 21 "$(in_db 'SELECT count(*) FROM task_items')"
}

old_rows() { # Every row of the tables the migration changes, but the admin's, in an order that doesn't depend on the collation.
  in_db "SELECT 'role', id, name, '' FROM roles WHERE name <> 'Admin'
    UNION ALL SELECT 'user', id, display_name, email FROM users WHERE email <> '$admin_email'
    UNION ALL SELECT 'project', id, name, key FROM projects
    UNION ALL SELECT 'task', id, title, number::text FROM task_items
    UNION ALL SELECT 'assignee', task_id, user_id::text, '' FROM task_assignees ORDER BY 1, 2, 3"
}

# Without the collation the local PostgreSQL, in C, puts every Romanian letter after Z and upper() leaves ș as it is.
old_database_sorts_byte_by_byte() {
  sorted=$(in_db "SELECT string_agg(title, ',' ORDER BY upper(title), id) FROM task_items WHERE number < 50")
  [ "$sorted" != "$romanian_titles" ] || {
    echo "the old database already sorts the Romanian way: $sorted"
    return 1
  }
  echo "before the migration: $sorted"
  is 0 "$(in_db "SELECT count(*) FROM task_items WHERE strpos(upper(title), upper('ștampilă')) > 0")"
}

# AddRomanianCollation applied, and every migration after it up to the newest of the code (group 22 added some).
api_migrates_the_old_database() {
  api_is_up || return 1
  newest=$(ls backend/src/Kanabol.Infrastructure/Persistence/Migrations | sed -n 's/^\([0-9]*_[A-Za-z0-9]*\)\.cs$/\1/p' | sort | tail -n 1)
  is "1|$newest" "$(in_db "SELECT count(*) FILTER (WHERE migration_id LIKE '%_AddRomanianCollation'), max(migration_id) FROM \"__EFMigrationsHistory\"")"
}

every_old_row_is_kept() {
  old_rows >"$scratch/rows-after" || return 1
  diff "$scratch/rows-before" "$scratch/rows-after"
}

columns_and_indexes_have_it() {
  is 'projects.name ro-x-icu
roles.name ro-x-icu
task_items.title ro-x-icu
users.display_name ro-x-icu
users.email ro-x-icu' "$(in_db "SELECT table_name || '.' || column_name || ' ' || collation_name FROM information_schema.columns
    WHERE table_schema = 'public' AND collation_name IS NOT NULL ORDER BY 1")" || return 1
  is 'ux_roles_lower_name ro-x-icu
ux_roles_name ro-x-icu
ux_users_email ro-x-icu' "$(in_db "SELECT i.relname || ' ' || c.collname FROM pg_index entry JOIN pg_class i ON i.oid = entry.indexrelid
    JOIN pg_collation c ON c.oid = entry.indcollation[0] WHERE i.relname IN ('ux_users_email', 'ux_roles_name', 'ux_roles_lower_name') ORDER BY 1")"
}

the_admin_signs_in() {
  api_is_up || return 1
  status_is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.79.0.1' \
    -d "{\"email\":\"$admin_email\",\"password\":\"$initial_password\"}")" || return 1
  status_is 200 "$(send POST /api/auth/change-password -H "Authorization: Bearer $(body_field accessToken)" \
    -H 'Content-Type: application/json' -d "{\"currentPassword\":\"$initial_password\",\"newPassword\":\"$admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

titles_in_romanian_order() {
  is "$romanian_titles" "$(titles_of_the_letters asc)" || return 1
  is "$(reversed "$romanian_titles")" "$(titles_of_the_letters desc)"
}

titles_of_the_letters() { # titles_of_the_letters <asc|desc>: the list by title, without the people's tasks
  status_is 200 "$(as "$admin_token" GET "/api/projects/$old_project/tasks?sort=title:$1&pageSize=100")" || return 1
  json "b.items.filter(t => t.number < 50).map(t => t.title).join(',')"
}

assignees_in_romanian_order() {
  status_is 200 "$(as "$admin_token" GET "/api/projects/$old_project/tasks?sort=assignee:asc&pageSize=100")" || return 1
  is "$romanian_names" "$(json "b.items.filter(t => t.number > 50 && t.number < 99).map(t => t.title).join(',')")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$old_project/tasks?sort=assignee:desc&pageSize=100")" || return 1
  is "$(reversed "$romanian_names")" "$(json "b.items.filter(t => t.number > 50 && t.number < 99).map(t => t.title).join(',')")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/tasks/$shared_task")" || return 1
  is "$romanian_names" "$(json "b.assignees.map(a => a.displayName).join(',')")"
}

# ș percent-encoded: curl reads its arguments in the ANSI code page.
search_finds_any_case() {
  status_is 200 "$(as "$admin_token" GET "/api/projects/$old_project/tasks?search=%C8%99tampil%C4%83")" || return 1
  is 'ȘTAMPILĂ' "$(json "b.items.map(t => t.title).join(',')")"
}

projects_users_and_roles_in_romanian_order() {
  is "$romanian_projects" "$(listed "$admin_token" '/api/projects?pageSize=100' name)" || return 1
  is "$romanian_emails" "$(listed "$admin_token" '/api/users?pageSize=100' email)" || return 1
  is "$romanian_roles" "$(listed "$admin_token" '/api/roles?pageSize=100' name)"
}

# What the check scripts sort with upper() in SQL is a collated column, so their order is the API's.
scripts_sort_as_the_api_does() {
  uses=$(grep -ohE 'ORDER BY upper\([a-z]+(\.[a-z_]+|_[a-z_]+)?\)' scripts/check-*.sh | sed 's/ORDER BY upper(\(.*\))/\1/; s/^[a-z]*\.//' | sort -u)
  [ -n "$uses" ] || return 1
  for column in $uses; do
    case "$column" in
      display_name | email | name | title) ;;
      *)
        echo "ORDER BY upper($column) sorts a column without the collation"
        return 1
        ;;
    esac
  done
  is "$romanian_names" "$(in_db "SELECT string_agg(u.display_name, ',' ORDER BY upper(u.display_name)) FROM task_assignees a JOIN users u ON u.id = a.user_id WHERE a.task_id = '$shared_task'")" || return 1
  is "$romanian_projects" "$(in_db "SELECT string_agg(name, ',' ORDER BY upper(name), key) FROM projects WHERE archived_at IS NULL")"
}

task_1() {
  echo "20.1 the ro-x-icu collation on the sorted text columns"
  check "Docker is running, for the tests' own PostgreSQL" docker_is_running
  check "the collation's tests pass on postgres:17-alpine" collation_tests_pass
  check "the five columns name Collations.Romanian, ro-x-icu, and nothing else has a collation" configurations_name_the_collation
  check "no migration creates the collation, and no query names one" nothing_else_names_a_collation
  check "the local PostgreSQL has ro-x-icu, ICU and deterministic" local_postgres_has_the_collation
  check "a database migrated up to $before_collation holds names and titles with Ă, Â, Î, Ș and Ț" an_old_database_with_romanian_names
  check "which it sorts byte by byte, and whose upper() leaves ș as it is" old_database_sorts_byte_by_byte
  check "the real API in Production migrates it at start" api_migrates_the_old_database
  check "every row of it is kept" every_old_row_is_kept
  check "the five columns, and their indexes, have ro-x-icu" columns_and_indexes_have_it
  check "the admin signs in and changes the first password" the_admin_signs_in
  check "the list by title, both ways, goes by the Romanian alphabet" titles_in_romanian_order
  check "the list by assignee, both ways, and a task's assignees too" assignees_in_romanian_order
  check "the search for ș finds Ș" search_finds_any_case
  check "the projects, the users by email and the roles in the Romanian order" projects_users_and_roles_in_romanian_order
  check "the check scripts' ORDER BY upper(...) is on collated columns, in the API's order" scripts_sort_as_the_api_does
  stop_api
}

# ---- Task 2: a restart of PostgreSQL under the real API.

retry_tests_pass() {
  classes_pass Kanabol.Application.Tests 4 TaskNumbersRetryTests || return 1
  classes_pass Kanabol.Api.Tests 2 DatabaseRestartTests
}

npgsql_retries_and_task_numbers_go_through_it() {
  grep -qF 'npgsql => npgsql.EnableRetryOnFailure()' backend/src/Kanabol.Infrastructure/DependencyInjection.cs || {
    echo "DependencyInjection.cs doesn't turn on EnableRetryOnFailure()"
    return 1
  }
  grep -qF 'db.Database.CreateExecutionStrategy().ExecuteAsync(' backend/src/Kanabol.Infrastructure/Persistence/TaskNumbers.cs || {
    echo "TaskNumbers doesn't run its transaction through the execution strategy"
    return 1
  }
  # The strategy refuses a transaction opened outside it, so TaskNumbers' is the only one.
  is backend/src/Kanabol.Infrastructure/Persistence/TaskNumbers.cs \
    "$(grep -rlE 'BeginTransaction|UseTransaction|TransactionScope' backend/src --include='*.cs' | grep -v '/Migrations/' | tr -d '\r')"
}

deploy_says_no_restart_of_the_api() {
  lacks -iE '(reporn|restart)[^.]*`api`|`api`[^.]*(reporn|restart)' docs/deploy.md
}

# Where the local PostgreSQL keeps its data and its programs, from the server itself (its user creates
# databases, and here is a superuser), or from KANABOL_PGDATA and KANABOL_PGBIN.
postgres_home_is_known() {
  need_database || return 1
  pg_data=${KANABOL_PGDATA:-$(sql "$admin_connection" "SHOW data_directory" | tr -d '\r')}
  pg_bin=${KANABOL_PGBIN:-$(sql "$admin_connection" "SELECT setting FROM pg_config WHERE name = 'BINDIR'" | tr -d '\r')}
  [ -d "$pg_data" ] && { [ -x "$pg_bin/pg_ctl" ] || [ -x "$pg_bin/pg_ctl.exe" ]; } || {
    echo "no PostgreSQL in '$pg_data' with pg_ctl in '$pg_bin': set KANABOL_PGDATA and KANABOL_PGBIN"
    return 1
  }
  echo "$pg_data, $pg_bin"
}

# Never through a pipe: postgres would hold it open for ever. Its log goes where the portable one keeps it.
postgres_starts() {
  "$pg_bin/pg_ctl" -D "$pg_data" -l "$(dirname "$pg_data")/postgres.log" -w -t 60 start >/dev/null 2>&1 </dev/null
  attempts=0
  until sql "$admin_connection" "SELECT 1" >/dev/null 2>&1; do
    attempts=$((attempts + 1))
    [ "$attempts" -gt 60 ] && return 1
    sleep 1
  done
  pg_stopped=
}

postgres_restarts() {
  [ -n "$pg_bin" ] || postgres_home_is_known >/dev/null || return 1
  pg_stopped=yes
  "$pg_bin/pg_ctl" -D "$pg_data" -m fast -w -t 60 stop >"$scratch/pg-stop.log" 2>&1 </dev/null || {
    cat "$scratch/pg-stop.log"
    return 1
  }
  postgres_starts
}

retry_project=

a_project_and_its_first_task() {
  api_is_up || return 1
  status_is 201 "$(send POST /api/projects -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' \
    -d '{"name":"Reziliență","key":"RTR","description":null}')" || return 1
  retry_project=$(body_field id)
  status_is 201 "$(send POST "/api/projects/$retry_project/tasks" -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' \
    -d '{"title":"Inainte de restart","priority":"Low"}')" || return 1
  is RTR-1 "$(body_field key)"
}

# The pool keeps the API's connections open between requests: those are what a restart ends.
the_pool_holds_connections() {
  connections=$(sql "$admin_connection" "SELECT count(*) FROM pg_stat_activity WHERE datname = '$check_db'" | tr -d '\r')
  [ "${connections:-0}" -ge 1 ] || {
    echo "the API holds no connection to $check_db"
    return 1
  }
  echo "$connections connection(s) to $check_db"
}

first_request_after_a_restart_answers_200() {
  postgres_restarts || return 1
  status_is 200 "$(as "$admin_token" GET "/api/projects/$retry_project")" || return 1
  is RTR "$(body_field key)"
}

task_created_after_a_restart_gets_the_next_number() {
  the_pool_holds_connections >/dev/null || return 1
  postgres_restarts || return 1
  keys=
  for title in 'Dupa restart' 'Inca unul'; do
    status_is 201 "$(send POST "/api/projects/$retry_project/tasks" -H "Authorization: Bearer $admin_token" -H 'Content-Type: application/json' \
      -d "{\"title\":\"$title\",\"priority\":\"Low\"}")" || return 1
    keys="${keys:+$keys }$(body_field key)"
  done
  is 'RTR-2 RTR-3' "$keys" || return 1
  is '1 Inainte de restart,2 Dupa restart,3 Inca unul|4' "$(in_db "SELECT string_agg(t.number || ' ' || t.title, ',' ORDER BY t.number), min(p.next_task_number)
    FROM task_items t JOIN projects p ON p.id = t.project_id WHERE p.key = 'RTR'")"
}

# EF logs each run it makes again, so the requests above did meet a dead connection.
the_log_shows_the_retries() {
  retries=$(grep -c 'will be retried after' "$api_log")
  [ "${retries:-0}" -ge 2 ] || {
    echo "the API's log has ${retries:-0} retries"
    return 1
  }
  echo "$retries retries in the API's log"
  # EF logs the command that failed, as an error, before it runs it again: anything else is a request that failed.
  others=$(grep '"@l":"Error"' "$api_log" | grep -vF '"Name":"Microsoft.EntityFrameworkCore.Database.Command.CommandError"')
  [ -z "$others" ] || {
    printf '%s\n' "$others" | head -n 3
    return 1
  }
}

task_2() {
  echo "20.2 a restart of PostgreSQL under the real API"
  stop_api
  check_db="kanabol_collation_retry_$$"
  check "Docker is running, for the tests' own PostgreSQL" docker_is_running
  check "the retry's tests pass: TaskNumbers runs again with the same number, the API answers after a restart" retry_tests_pass
  check "Npgsql retries on failure, and TaskNumbers' transaction, the only one, goes through its strategy" npgsql_retries_and_task_numbers_go_through_it
  check "docs/deploy.md doesn't ask for a restart of api after one of PostgreSQL" deploy_says_no_restart_of_the_api
  check "the local PostgreSQL's data folder and pg_ctl are known" postgres_home_is_known
  check "the real API in Production starts on a database of its own" api_is_up
  check "the admin signs in and changes the first password" the_admin_signs_in
  check "a project and its first task, RTR-1" a_project_and_its_first_task
  check "the API's pool holds connections to its database" the_pool_holds_connections
  check "PostgreSQL stopped and started under it, the first request answers 200" first_request_after_a_restart_answers_200
  check "after another restart, two tasks get RTR-2 and RTR-3, the counter at 4" task_created_after_a_restart_gets_the_next_number
  check "the API's log shows the runs it made again, and no error" the_log_shows_the_retries
  stop_api
}

# ---- Task 3: the contrast tokens of docs/UI_UX_SPEC.md §2.1–§2.3 and §3.2.

tokens=frontend/src/styles/_tokens.scss

# The light rule is :root's; the dark one is the mixin both dark rules include.
token_in() { # token_in <light|dark> <name>: its value in that rule of _tokens.scss
  case "$1" in
    light) sed -n '/^:root {/,/^}/p' "$tokens" ;;
    dark) sed -n '/^@mixin dark {/,/^}/p' "$tokens" ;;
  esac | sed -n "s/^ *--kb-$2: *\(#[0-9a-f]*\);.*/\1/p"
}

tokens_have_the_new_values() {
  is '#697078' "$(token_in light text-500)" || return 1
  is '#8692ae' "$(token_in dark text-500)" || return 1
  is '#91c4fb' "$(token_in light sky-300)" || return 1
  is '#3b6cc7' "$(token_in dark sky-300)" || return 1
  # The charts keep theirs: chart-2 had sky-300's old value in dark, and keeps it.
  is '#135bf7' "$(token_in light chart-2)" || return 1
  is '#3d6fc9' "$(token_in dark chart-2)" || return 1
  is '--kb-chart-2: #3d6fc9;' "$(grep -o -- '--kb-[a-z0-9-]*: #3d6fc9;' "$tokens")"
}

# Nothing in the app keeps the values they had: #787f86 and #6f7b96 for text-500.
old_values_are_gone() {
  lacks -i '#787f86|#6f7b96' frontend/src --include='*.scss' --include='*.ts' --include='*.html' --include='*.css'
}

built_css_has_the_tokens() {
  (cd frontend && npx ng build --configuration production >"$scratch/build.log" 2>&1) || {
    tail -n 20 "$scratch/build.log"
    return 1
  }
  css=$(ls frontend/dist/kanabol/browser/styles-*.css 2>/dev/null | head -n 1)
  [ -n "$css" ] || return 1
  # Light once, dark twice: the media query and data-theme include the same mixin.
  for expected in '--kb-text-500:#697078:1' '--kb-text-500:#8692ae:2' '--kb-sky-300:#91c4fb:1' '--kb-sky-300:#3b6cc7:2' '--kb-chart-2:#3d6fc9:2'; do
    pair=${expected%:*}
    count=${expected##*:}
    is "$count" "$(tr -d ' ' <"$css" | grep -o -- "$pair" | wc -l | tr -d ' ')" || {
      echo "for $pair in $css"
      return 1
    }
  done
  tr -d ' ' <"$css" >"$scratch/styles.css"
  lacks -i '#787f86|#6f7b96|--kb-sky-300:#3d6fc9' "$scratch/styles.css"
}

# Each PNG of docs/screens/ taken again since v0.1.0, whose were made with the old tokens: every screen
# with text in text-500 or on sky-300 is another picture now. The login page has none, and with the same
# data and the same Edge its two come out the same, byte for byte, which tells that nothing else moved.
screenshots_are_retaken() {
  git rev-parse -q --verify 'v0.1.0^{commit}' >/dev/null || {
    echo "no tag v0.1.0 to compare the screenshots with"
    return 1
  }
  changed=0
  for file in $(git ls-files 'docs/screens/*.png'); do
    # Group 22's own screens came after v0.1.0, with the new tokens.
    case "$file" in
      docs/screens/7.8-rapoarte-estimare-* | docs/screens/7.9-setari-generale-*) continue ;;
    esac
    old=$(git rev-parse -q --verify "v0.1.0:$file") || {
      echo "$file wasn't in v0.1.0"
      return 1
    }
    case "$file" in
      docs/screens/7.1-login-*) continue ;;
    esac
    [ "$old" != "$(git hash-object "$file")" ] || {
      echo "$file is still the picture of v0.1.0"
      return 1
    }
    changed=$((changed + 1))
  done
  is 30 "$changed"
}

ui_review_is_up_to_date() {
  for words in 'grupul 20' '#697078' '#8692ae' '#3b6cc7' 'color-contrast' '7.3-membri' 'light' 'dark'; do
    grep -qF -- "$words" docs/ui-review.md || {
      echo "docs/ui-review.md doesn't say $words"
      return 1
    }
  done
  # What 19b left for the tokens is no longer waiting.
  lacks 'Ce rămâne \(în Blocked' docs/ui-review.md
}

task_3() {
  echo "20.3 the contrast tokens, the screenshots and docs/ui-review.md"
  check "_tokens.scss has text-500 #697078 and #8692ae, sky-300 #3b6cc7 in dark, chart-2 still #3d6fc9" tokens_have_the_new_values
  check "nothing in the app keeps text-500's old values" old_values_are_gone
  check "the production CSS has them, light once and dark in both rules, and none of the old ones" built_css_has_the_tokens
  check "every screenshot in docs/screens/ with text in text-500 or on sky-300 was taken again since v0.1.0" screenshots_are_retaken
  check "docs/ui-review.md tells of group 20's tokens and axe-core run again on them" ui_review_is_up_to_date
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
