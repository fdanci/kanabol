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
#
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database,
# kanabol_collation_check_<pid>, and drops it. Docker runs the tests' own PostgreSQL. The real API listens
# on port 5279.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

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
migrations=backend/src/Kanabol.Infrastructure/Persistence/Migrations
configurations=backend/src/Kanabol.Infrastructure/Persistence/Configurations
before_collation=20261107000923_AddTaskAssignees

admin_email=admin@kanabol.local
initial_password='o parola initiala de verificare'
admin_password='parola adminului pentru colatie'
jwt_secret=collation-check-secret-with-at-least-32-characters

cleanup() {
  stop_api
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

lacks() { # lacks <extended regex> <file or folder...>
  ! grep -rnE -- "$@" || return 1
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

drop_database() {
  [ -n "$admin_connection" ] || return 0
  sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null 2>&1
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

api_migrates_the_old_database() {
  api_is_up || return 1
  is AddRomanianCollation "$(in_db 'SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1' | sed 's/^[0-9]*_//')"
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

tasks=${*:-1}
for task in $tasks; do
  case "$task" in
    1) task_1 ;;
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
