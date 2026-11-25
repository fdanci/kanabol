#!/bin/sh
# Acceptance checks for pgvector and the vectorization (group 25 in the project's Tasks.md; docs/SPEC.md §19,
# §13.5, §14.7). The tests and specs hold the rules, so these checks run them, and add what they can't see:
# the real API process on a real PostgreSQL with pgvector, its Azure client talking HTTP to a stand-in for
# Azure, its worker and its logs, and scripts/search-data.sh on it.
#
# Usage: scripts/check-search.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  the database and the switch: the tests of the vectors' table, the calls' table, the migration of the
#      extension and the settings, then the real API in Production on a new database: pgvector turned on,
#      task_embeddings with a vector of no fixed length, deleted with its task, and no index on the vectors
#      (§19.1), embedding_calls with no text; the vectorization off unless turned on, saying so, the search
#      answering 409 Search.TurnedOff; turned on without a model, the API stops at start naming the
#      setting, never the key it was given. Docker, PostgreSQL.
#   2  the vectorization and the search, end to end: the tests of the command, the worker, the Azure model,
#      the fake model, the search and its endpoint, then the real API in Production with its real Azure
#      client sent to a stand-in for Azure, in Node, whose vectors count a few words: at start the worker
#      vectorizes every task, archived ones and those of an archived project too, in batches of 2, one row
#      in embedding_calls per call with the tokens the stand-in answered, the key in its header; the search
#      answers the nearest first, archived ones only when asked, with a project, a person and a limit, the
#      question's tokens, and a row for its call; a task renamed is vectorized again at the next run, alone;
#      when the stand-in fails, 503 Search.ModelFailed and no row; the API's log holds no text and no key.
#      Docker, PostgreSQL, Node.
#   3  scripts/search-data.sh on the real API in Production: tracked as executable, read by dash, its usage
#      without passwords; then demo-data.sh and the 262 tasks of scripts/search-data.txt, each project with
#      as many as the file, every title, description, priority and assignee as the file has them, the
#      archived tasks and the archived project; run again it makes nothing; a task and the project restored
#      since are archived again, and nothing else is made; a project of its keys under another name stops
#      it, adding nothing; nothing it prints holds a password. PostgreSQL.
#   4  the group's documents and CI: docs/SPEC.md §19, backend/CLAUDE.md, the CI's PostgreSQL with
#      postgresql-17-pgvector, this script in a shard of scripts/ci-plan.sh, compose.yaml and .env.example
#      with the vectorization off unless set, the architecture tests (Domain without Pgvector, Application
#      without Azure and OpenAI), and the probe of the real Azure skipped without KANABOL_AZURE_PROBE, so no
#      run of these checks or of CI calls Azure. Only the packages.
#
# PostgreSQL means one with the pgvector extension installed, whose user may create databases, with its
# connection string in KANABOL_CHECK_DB or else in the API's user secrets (ConnectionStrings:Default); each
# run works in its own database, kanabol_search_check_<pid>, and drops it. Docker runs the tests' own
# PostgreSQL. The real API listens on port 5292, the stand-in for Azure on 5296. Nothing here calls Azure.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
# In the full gate, the build, tests and lint scripts/gate-prepare.sh did once (docs/SPEC.md §14.7).
[ -n "${KANABOL_GATE_CACHE:-}" ] && . scripts/gate-reuse.sh

failures=0
scratch=$(mktemp -d)
check_db="kanabol_search_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5292
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
api_environment=
admin_connection=
stand_in_port=5296
stand_in_pid=
stand_in_dir="$scratch/stand-in"

admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'
demo_password='parola colegilor de demonstratie'
# The key the API sends the stand-in, which no log may hold.
probe_key=probe-key-that-no-log-may-hold

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

in_file() { # in_file <text> <file>
  grep -qF -- "$1" "$2"
}

lacks() { # lacks <fixed text> <file...>: none of them holds it
  text=$1
  shift
  ! grep -lF -- "$text" "$@" || {
    echo "holds $text"
    return 1
  }
}

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
}

file_has() { # file_has <file> <fixed text...>: each text is in the file
  file=$1
  shift
  for text in "$@"; do
    grep -qF -- "$text" "$file" || {
      echo "$file doesn't say: $text"
      return 1
    }
  done
}

tracked_and_executable() { # tracked_and_executable <file>
  is 100755 "$(git ls-files -s -- "$1" | cut -c1-6)"
}

docker_is_running() {
  docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
  "$docker_cli" info --format '{{.ServerVersion}}'
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

classes() { # classes <class...>: a filter of those test classes
  filter=
  for class in "$@"; do
    filter="${filter:+$filter|}FullyQualifiedName~.$class."
  done
  printf '%s' "$filter"
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
      Alerts__OverdueCheckEnabled=false \
      Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:9 \
      "$@" dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$log" 2>&1 &
  started_pid=$!
}

# The API with the settings given as NAME=value. Other settings than the running one's start it again.
start_api() { # start_api [NAME=value...]
  wanted="$*"
  [ -n "$api_pid" ] && [ "$api_environment" = "$wanted" ] && return 0
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  launch_api "$api_log" Production "$@"
  api_pid=$started_pid
  api_environment=$wanted
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

# As a server's .env leaves it: no Embeddings setting at all, so the vectorization is off.
api_is_up() {
  start_api
}

stop_api() {
  [ -n "${api_pid:-}" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
  # The next start writes its log over this one, which log_holds_no_text still reads.
  cat "$api_log" >>"$scratch/api-earlier.log" 2>/dev/null
}

cleanup() {
  stop_api
  [ -z "$stand_in_pid" ] || kill "$stand_in_pid" 2>/dev/null
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
  printf '10.86.%s.%s' "$((used / 256))" "$((used % 256))"
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  rm -f "$scratch/body"
  curl -sS --max-time 60 -o "$scratch/body" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
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
  printf '{"email":"%s","password":"%s"}' "$1" "$2" >"$scratch/login.json"
  send POST /api/auth/login -H 'Content-Type: application/json' -H "X-Forwarded-For: $(unused_address)" \
    --data-binary "@$(native "$scratch/login.json")"
}

body_field() { # body_field <name>: the value of the first text field with that name in the last answer
  sed -n "s/^[^\"]*\"$1\":\"\([^\"]*\)\".*/\1/p; t; s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body" | head -n 1
}

json() { # json <JavaScript expression on b, the last answer>: prints its value
  node -e "const b = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8')); console.log(eval(process.argv[2]))" \
    "$(native "$scratch/body")" "$1" | tr -d '\r'
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

# The first admin past the forced password change. The token, signed with the same secret, holds across
# restarts of the API. The Admin role has chat.use, as every permission.
admin_is_ready() {
  [ -n "${api_pid:-}" ] || api_is_up || return 1
  [ -n "${admin_token:-}" ] && return 0
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
  status_is 200 "$(as "$first_token" POST /api/auth/change-password \
    "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || return 1
  admin_token=$(body_field accessToken)
  admin_id=$(in_db "SELECT id FROM users WHERE email = '$admin_email'")
  [ -n "$admin_token" ] && [ -n "$admin_id" ]
}

# ---- Task 1: the database and the switch.

database_tests_pass() {
  tests_pass Kanabol.Domain.Tests "$(classes TaskEmbeddingTests EmbeddingCallTests)" 14 || return 1
  tests_pass Kanabol.Application.Tests \
    "$(classes AddVectorExtensionMigrationTests TaskEmbeddingPersistenceTests EmbeddingsOptionsTests)" 28
}

pgvector_is_on() {
  api_is_up || return 1
  is vector "$(in_db "SELECT extname FROM pg_extension WHERE extname = 'vector'")"
}

vectors_have_no_fixed_length() {
  is vector "$(in_db "SELECT format_type(atttypid, atttypmod) FROM pg_attribute
    WHERE attrelid = 'task_embeddings'::regclass AND attname = 'embedding'")"
}

vectors_go_with_their_task() {
  is c "$(in_db "SELECT confdeltype FROM pg_constraint
    WHERE conrelid = 'task_embeddings'::regclass AND contype = 'f' AND confrelid = 'task_items'::regclass")"
}

no_index_on_the_vectors() {
  is 0 "$(in_db "SELECT count(*) FROM pg_indexes WHERE tablename = 'task_embeddings' AND indexdef ~* '(hnsw|ivfflat)'")" || return 1
  is 'pk_task_embeddings ux_task_embeddings_task_id_source' \
    "$(in_db "SELECT string_agg(indexname, ' ' ORDER BY indexname) FROM pg_indexes WHERE tablename = 'task_embeddings'")"
}

calls_hold_no_text() {
  is 'id,purpose,model,text_count,tokens,called_at' "$(in_db "SELECT string_agg(column_name, ',' ORDER BY ordinal_position)
    FROM information_schema.columns WHERE table_name = 'embedding_calls'")"
}

vectorization_is_off() {
  api_is_up || return 1
  in_file 'The vectorization is turned off' "$api_log"
}

search_is_turned_off() {
  admin_is_ready || return 1
  code_is 409 Search.TurnedOff "$(as "$admin_token" POST /api/search/tasks '{"question":"copii de siguranță"}')"
}

# Turned on, the settings are checked at start: one missing stops the API, and its message names the setting.
turned_on_without_a_model_stops() {
  stop_api
  need_database || return 1
  launch_api "$scratch/api-no-model.log" Production Embeddings__Enabled=true \
    Embeddings__Azure__Endpoint="http://127.0.0.1:$stand_in_port" Embeddings__Azure__Deployment=probe-deployment \
    Embeddings__Azure__ApiKey=$probe_key
  waited=0
  while kill -0 "$started_pid" 2>/dev/null && [ "$waited" -lt 60 ]; do
    sleep 1
    waited=$((waited + 1))
  done
  if kill -0 "$started_pid" 2>/dev/null; then
    kill "$started_pid"
    echo "the API is still running after 60 s"
    return 1
  fi
  in_file 'Embeddings:Model is needed' "$scratch/api-no-model.log" || {
    tail -n 20 "$scratch/api-no-model.log"
    return 1
  }
  lacks "$probe_key" "$scratch/api-no-model.log"
}

task_1() {
  echo "25.1 The database and the switch"
  check "Docker is running" docker_is_running
  check "the tests of the vectors, the calls, the extension's migration and the settings pass" database_tests_pass
  check "the real API in Production turns pgvector on in a new database" pgvector_is_on
  check "task_embeddings holds a vector of no fixed length" vectors_have_no_fixed_length
  check "a task's vectors go with it" vectors_go_with_their_task
  check "there's no index on the vectors (docs/SPEC.md §19.1)" no_index_on_the_vectors
  check "embedding_calls has no column for a text" calls_hold_no_text
  check "without settings the vectorization is off, and the log says so" vectorization_is_off
  check "the search answers 409 Search.TurnedOff" search_is_turned_off
  check "turned on without a model, the API stops at start, naming the setting and not the key" \
    turned_on_without_a_model_stops
}

# ---- Task 2: the vectorization and the search, against a stand-in for Azure.

search_tests_pass() {
  tests_pass Kanabol.Application.Tests \
    "$(classes EmbedTasksTests EmbeddingsWorkerTests AzureEmbeddingModelTests FakeEmbeddingModelTests SearchTasksTests SearchTasksTurnedOffTests)" 62 ||
    return 1
  tests_pass Kanabol.Api.Tests "$(classes SearchEndpointTests)" 6
}

# Azure's embeddings endpoint, as the SDK calls it: the vectors in base64, the usage, and one line per call
# in the requests file, with the path, the key and the counts but no text. A vector counts the words of four
# topics, plus a small constant, so texts of a topic are at distance 0 from each other and about 0.99 from
# the rest. With a file named fail in its folder, it answers 500.
stand_in_is_up() {
  [ -n "$stand_in_pid" ] && kill -0 "$stand_in_pid" 2>/dev/null && return 0
  mkdir -p "$stand_in_dir"
  cat >"$stand_in_dir/azure.js" <<'JS'
const http = require('http');
const fs = require('fs');
const path = require('path');
const [port, dir] = process.argv.slice(2);
const topics = [['backup', 'siguranta', 'restaur'], ['factur'], ['concedi'], ['parol', 'autentific']];
const plain = (text) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
const vector = (text) => [...topics.map((words) => words.reduce((n, w) => n + plain(text).split(w).length - 1, 0)), 0.1];
const note = (line) => fs.appendFileSync(path.join(dir, 'requests'), JSON.stringify(line) + '\n');
http.createServer((req, res) => {
  let body = '';
  req.on('data', (chunk) => (body += chunk));
  req.on('end', () => {
    if (req.method !== 'POST') {
      res.writeHead(200);
      return res.end('ok');
    }
    const seen = { path: req.url.split('?')[0], key: req.headers['api-key'] || '' };
    if (fs.existsSync(path.join(dir, 'fail'))) {
      note({ ...seen, failed: true });
      res.writeHead(500, { 'content-type': 'application/json' });
      return res.end('{"error":{"code":"500","message":"the stand-in fails"}}');
    }
    const request = JSON.parse(body);
    const texts = Array.isArray(request.input) ? request.input : [request.input];
    const tokens = texts.reduce((n, text) => n + text.split(/\s+/).filter(Boolean).length, 0);
    note({ ...seen, texts: texts.length, tokens });
    const data = texts.map((text, index) => ({
      object: 'embedding',
      index,
      embedding: request.encoding_format === 'base64' ? Buffer.from(new Float32Array(vector(text)).buffer).toString('base64') : vector(text),
    }));
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ object: 'list', data, model: 'probe-model', usage: { prompt_tokens: tokens, total_tokens: tokens } }));
  });
}).listen(Number(port), '127.0.0.1');
JS
  : >"$stand_in_dir/requests"
  node "$(native "$stand_in_dir/azure.js")" "$stand_in_port" "$(native "$stand_in_dir")" >"$stand_in_dir/node.log" 2>&1 &
  stand_in_pid=$!
  attempts=0
  until curl -s -o /dev/null "http://127.0.0.1:$stand_in_port/"; do
    attempts=$((attempts + 1))
    [ "$attempts" -le 40 ] && kill -0 "$stand_in_pid" 2>/dev/null || {
      cat "$stand_in_dir/node.log"
      return 1
    }
    sleep 0.25
  done
  : >"$stand_in_dir/requests"
}

# The API with the vectorization on, sent to the stand-in, in batches of 2. The interval is an hour, so the
# worker runs at start and not again while the checks look.
search_api_is_up() {
  stand_in_is_up || return 1
  start_api Embeddings__Enabled=true Embeddings__Model=probe-model \
    Embeddings__Azure__Endpoint="http://127.0.0.1:$stand_in_port" Embeddings__Azure__Deployment=probe-deployment \
    Embeddings__Azure__ApiKey=$probe_key Embeddings__Interval=01:00:00 Embeddings__BatchSize=2
}

# The worker's run at start, done: its last line in the log.
worker_ran() {
  attempts=0
  until grep -qE 'The vectorization (made|stopped|failed)' "$api_log"; do
    attempts=$((attempts + 1))
    [ "$attempts" -le 60 ] || {
      echo "the worker didn't finish a run in 30 s"
      tail -n 20 "$api_log"
      return 1
    }
    sleep 0.5
  done
}

make_task() { # make_task <project id> <title> <description JSON> [assignee id]: its id in task_id
  assignees=
  [ -z "${4:-}" ] || assignees="\"$4\""
  status_is 201 "$(as "$admin_token" POST "/api/projects/$1/tasks" \
    "{\"title\":\"$2\",\"description\":$3,\"priority\":\"Medium\",\"assigneeIds\":[$assignees]}")" || return 1
  task_id=$(body_field id)
}

make_project() { # make_project <key> <name>: its id in project_id
  status_is 201 "$(as "$admin_token" POST /api/projects "{\"name\":\"$2\",\"key\":\"$1\",\"description\":null}")" || return 1
  project_id=$(body_field id)
}

# Seven tasks made with the vectorization off, so the worker finds them all at its start: ALFA-4 archived,
# and GAMA archived with its task. ALFA-1, ALFA-4, BETA-1 and GAMA-1 are of the backup topic, ALFA-1 the
# admin's.
search_work_is_there() {
  [ -n "${beta_id:-}" ] && return 0
  api_is_up && admin_is_ready || return 1
  make_project ALFA 'Proba Alfa' && alfa_id=$project_id || return 1
  make_task "$alfa_id" 'Backup-ul de noapte a eșuat' '"Discul s-a umplut la 02:00."' "$admin_id" || return 1
  make_task "$alfa_id" 'Factura lunară nu a plecat' null || return 1
  make_task "$alfa_id" 'Cerere de concediu pentru august' '"Două săptămâni."' || return 1
  make_task "$alfa_id" 'Restaurarea de probă' '"Din copia de aseară."' || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$task_id")" || return 1
  make_project BETA 'Proba Beta' && beta_id=$project_id || return 1
  make_task "$beta_id" 'Copii de siguranță în altă locație' null || return 1
  make_task "$beta_id" 'Resetarea parolei' '"Linkul expiră după o oră."' && renamed_id=$task_id || return 1
  make_project GAMA 'Proba Gama' && gama_id=$project_id || return 1
  make_task "$gama_id" 'Backup pe bandă' null || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/projects/$gama_id")"
}

requests_seen() { # requests_seen <JavaScript expression on r, the stand-in's lines>
  node -e "const r = require('fs').readFileSync(process.argv[1], 'utf8').trim().split('\n').filter(Boolean).map(JSON.parse);
    console.log(eval(process.argv[2]))" "$(native "$stand_in_dir/requests")" "$1" | tr -d '\r'
}

# In Production the log is Serilog's compact JSON: the template, then each value as a property.
worker_made() { # worker_made <vectors> <batches>: the line of the worker's last run says so
  grep -E '"@mt":"The vectorization made ' "$api_log" | tail -n 1 | grep -qF "\"TaskCount\":$1,\"BatchCount\":$2,"
}

worker_vectorizes_everything() {
  search_work_is_there || return 1
  search_api_is_up && worker_ran || return 1
  worker_made 7 4 || {
    grep 'The vectorization' "$api_log"
    return 1
  }
  is 7 "$(in_db "SELECT count(*) FROM task_embeddings WHERE model = 'probe-model' AND source = 'Task'")"
}

stand_in_got_the_key() {
  is "4 /openai/deployments/probe-deployment/embeddings $probe_key 2,2,2,1" \
    "$(requests_seen "r.length + ' ' + [...new Set(r.map(x => x.path))].join() + ' ' + [...new Set(r.map(x => x.key))].join() + ' ' + r.map(x => x.texts).join()")"
}

calls_are_recorded() {
  is "4|7|$(requests_seen 'r.reduce((n, x) => n + x.tokens, 0)')" \
    "$(in_db "SELECT count(*), sum(text_count), sum(tokens) FROM embedding_calls WHERE purpose = 'Vectorization' AND model = 'probe-model'")"
}

search() { # search <JSON fields after the question's>: as the admin, the status printed
  as "$admin_token" POST /api/search/tasks "{\"question\":\"copii de siguranță\"$1}"
}

keys() { # The keys of the last answer, nearest first
  json "b.tasks.map(t => t.key).join(' ')"
}

nearest_first() {
  worker_vectorizes_everything || return 1
  status_is 200 "$(search '')" || return 1
  is 'ALFA-1 BETA-1' "$(json "b.tasks.slice(0, 2).map(t => t.key).sort().join(' ')")" || return 1
  is 'ALFA-2 ALFA-3 BETA-2' "$(json "b.tasks.slice(2).map(t => t.key).sort().join(' ')")" || return 1
  is true "$(json "b.tasks.every((t, i, a) => i === 0 || a[i - 1].distance <= t.distance)")" || return 1
  is 'true true' "$(json "[b.tasks[1].distance < 1e-6, b.tasks[2].distance > 0.9].join(' ')")" || return 1
  is 3 "$(json b.tokens)"
}

archived_only_when_asked() {
  status_is 200 "$(search ',"includeArchived":true')" || return 1
  is 'ALFA-1 ALFA-4 BETA-1 GAMA-1' "$(json "b.tasks.slice(0, 4).map(t => t.key).sort().join(' ')")" || return 1
  is 'ALFA-4 GAMA-1' "$(json "b.tasks.filter(t => t.archived).map(t => t.key).sort().join(' ')")" || return 1
  is 7 "$(json b.tasks.length)"
}

filters_and_limit_hold() {
  status_is 200 "$(search ",\"projectId\":\"$beta_id\"")" && is 'BETA-1 BETA-2' "$(keys)" || return 1
  status_is 200 "$(search ",\"assigneeId\":\"$admin_id\"")" && is 'ALFA-1' "$(keys)" || return 1
  status_is 200 "$(search ',"includeArchived":true,"limit":1')" && is 1 "$(json b.tasks.length)"
}

# The five searches above, each its own call of one text, of the question's 3 tokens.
search_calls_are_recorded() {
  is '5|5|3' "$(in_db "SELECT count(*), sum(text_count), min(tokens) FROM embedding_calls WHERE purpose = 'Search'")" || return 1
  is 3 "$(in_db "SELECT max(tokens) FROM embedding_calls WHERE purpose = 'Search'")"
}

renamed_task_is_vectorized_again() {
  status_is 200 "$(as "$admin_token" PUT "/api/tasks/$renamed_id/title" '{"title":"Resetarea parolei uitate"}')" || return 1
  before=$(requests_seen 'r.length')
  stop_api
  search_api_is_up && worker_ran || return 1
  worker_made 1 1 || {
    grep 'The vectorization' "$api_log"
    return 1
  }
  is "$((before + 1)) 1" "$(requests_seen "r.length + ' ' + r[r.length - 1].texts")" || return 1
  is 'Resetarea parolei uitate' "$(in_db "SELECT split_part(content, E'\n', 1) FROM task_embeddings WHERE task_id = '$renamed_id'")"
}

model_failure_is_503() {
  touch "$stand_in_dir/fail"
  status=$(search '')
  rm -f "$stand_in_dir/fail"
  code_is 503 Search.ModelFailed "$status" || return 1
  is 5 "$(in_db "SELECT count(*) FROM embedding_calls WHERE purpose = 'Search'")"
}

# Every start's log, those before the last one kept by stop_api. The texts without their diacritics, which
# JSON may write as \u escapes.
log_holds_no_text() {
  touch "$scratch/api-earlier.log"
  for text in "$probe_key" 'copii de siguran' 'Backup-ul de noapte' 'Resetarea parolei' 'Discul s-a umplut'; do
    lacks "$text" "$api_log" "$scratch/api-earlier.log" || return 1
  done
}

task_2() {
  echo "25.2 The vectorization and the search, on the real API against a stand-in for Azure"
  check "Docker is running" docker_is_running
  check "the tests of the command, the worker, the models, the search and its endpoint pass" search_tests_pass
  check "at start the worker vectorizes the 7 tasks, archived ones too, in 4 batches of 2" \
    worker_vectorizes_everything
  check "the Azure client calls the deployment with the key in its header" stand_in_got_the_key
  check "each call has a row in embedding_calls, with the tokens the model answered" calls_are_recorded
  check "the search answers the nearest first, at distance 0 for a topic, with the question's tokens" nearest_first
  check "archived tasks and those of an archived project only when asked" archived_only_when_asked
  check "a project, a person and the limit narrow it" filters_and_limit_hold
  check "each search has a row in embedding_calls, of one text" search_calls_are_recorded
  check "a task renamed is vectorized again at the next run, alone" renamed_task_is_vectorized_again
  check "when the model fails the search answers 503 Search.ModelFailed, with no row" model_failure_is_503
  check "the API's log holds neither a text sent to the model nor the key" log_holds_no_text
}

# ---- Task 3: search-data.sh on the real API.

search_data() { # search_data <log> [VARIABLE=value...]: search-data.sh on the real API, never with a terminal
  log=$1
  shift
  env -u KANABOL_ADMIN_EMAIL -u KANABOL_ADMIN_PASSWORD -u KANABOL_DEMO_PASSWORD -u KANABOL_CACERT "$@" \
    dash scripts/search-data.sh "$API_URL" </dev/null >"$log" 2>&1
}

run_search_data() { # run_search_data <log>
  search_data "$1" KANABOL_ADMIN_PASSWORD="$new_admin_password" KANABOL_DEMO_PASSWORD="$demo_password"
}

search_data_needs_its_passwords() {
  search_data "$scratch/search-usage.log"
  is 2 "$?" || return 1
  grep -q '^Usage: ' "$scratch/search-usage.log" || return 1
  search_data "$scratch/search-usage.log" KANABOL_ADMIN_PASSWORD=x KANABOL_DEMO_PASSWORD=short
  is 2 "$?"
}

file_keys='FAC MAG RU INF SUP SITE'
in_file_keys="'FAC','MAG','RU','INF','SUP','SITE'"

file_tasks() { # The file's tasks, as KEY-n|field...
  grep -v '^#' scripts/search-data.txt | grep . | awk -F'|' '{ n[$1]++; print $1 "-" n[$1] "|" $0 }' | cut -d'|' -f1,3-
}

first_run_makes_the_data() {
  admin_is_ready || return 1
  run_search_data "$scratch/search-1.log" || {
    tail -n 20 "$scratch/search-1.log"
    return 1
  }
  grep -q '^Done: [0-9]* new\.$' "$scratch/search-1.log" || {
    cat "$scratch/search-1.log"
    return 1
  }
  for key in $file_keys; do
    grep -q "^  $key .*of them new$" "$scratch/search-1.log" || {
      cat "$scratch/search-1.log"
      return 1
    }
  done
}

each_project_has_the_files_tasks() {
  is "$(file_tasks | cut -d- -f1 | sort | uniq -c | awk '{ print $2 "|" $1 }')" \
    "$(in_db "SELECT p.key, count(*) FROM task_items t JOIN projects p ON p.id = t.project_id
      WHERE p.key IN ($in_file_keys) GROUP BY p.key ORDER BY p.key")" || return 1
  is 262 "$(file_tasks | wc -l | tr -d ' ')"
}

tasks_are_the_files() {
  file_tasks | awk -F'|' '{ print $1 "|" $2 "|" $5 "|" $6 }' | sort >"$scratch/file-tasks"
  in_db "SELECT p.key || '-' || t.number, t.priority, t.title, coalesce(replace(t.description, E'\n', '\n'), '')
    FROM task_items t JOIN projects p ON p.id = t.project_id WHERE p.key IN ($in_file_keys)" | sort >"$scratch/db-tasks"
  diff "$scratch/file-tasks" "$scratch/db-tasks"
}

assignees_are_the_files() {
  file_tasks | awk -F'|' '{ print $1 "|" $3 }' | while IFS='|' read -r task names; do
    printf '%s|%s\n' "$task" "$(printf '%s' "$names" | tr ',' '\n' | sort | paste -sd, -)"
  done | sort >"$scratch/file-assignees"
  in_db "SELECT p.key || '-' || t.number, coalesce(string_agg(split_part(split_part(u.email, '@', 1), '.', 1), ','
      ORDER BY split_part(split_part(u.email, '@', 1), '.', 1)), '-')
    FROM task_items t JOIN projects p ON p.id = t.project_id
    LEFT JOIN task_assignees a ON a.task_id = t.id LEFT JOIN users u ON u.id = a.user_id
    WHERE p.key IN ($in_file_keys) GROUP BY p.key, t.number" | sort >"$scratch/db-assignees"
  diff "$scratch/file-assignees" "$scratch/db-assignees"
}

archived_ones_are_the_files() {
  is "$(file_tasks | awk -F'|' '$4 == "archived" { print $1 }' | sort | paste -sd' ' -)" \
    "$(in_db "SELECT p.key || '-' || t.number FROM task_items t JOIN projects p ON p.id = t.project_id
      WHERE p.key IN ($in_file_keys) AND t.archived_at IS NOT NULL ORDER BY 1" | paste -sd' ' -)" || return 1
  is SITE "$(in_db "SELECT string_agg(key, ' ') FROM projects WHERE key IN ($in_file_keys) AND archived_at IS NOT NULL")"
}

search_counts() {
  in_db "SELECT (SELECT count(*) FROM task_items), (SELECT count(*) FROM task_items WHERE archived_at IS NOT NULL),
    (SELECT count(*) FROM projects WHERE archived_at IS NOT NULL), (SELECT count(*) FROM task_assignees),
    (SELECT count(*) FROM project_members)"
}

second_run_makes_nothing() {
  search_counts >"$scratch/counts-1"
  run_search_data "$scratch/search-2.log" || {
    tail -n 20 "$scratch/search-2.log"
    return 1
  }
  grep -q '^Nothing new: the search data was already there\.$' "$scratch/search-2.log" || {
    cat "$scratch/search-2.log"
    return 1
  }
  search_counts >"$scratch/counts-2"
  diff "$scratch/counts-1" "$scratch/counts-2"
}

restored_ones_are_archived_again() {
  restored=$(in_db "SELECT t.id FROM task_items t JOIN projects p ON p.id = t.project_id WHERE p.key = 'FAC' AND t.number = 45")
  site=$(in_db "SELECT id FROM projects WHERE key = 'SITE'")
  status_is 200 "$(as "$admin_token" POST "/api/tasks/$restored/restore")" || return 1
  status_is 200 "$(as "$admin_token" POST "/api/projects/$site/restore")" || return 1
  run_search_data "$scratch/search-3.log" || {
    tail -n 20 "$scratch/search-3.log"
    return 1
  }
  grep -q '^Done: 2 new\.$' "$scratch/search-3.log" && in_file 'FAC-45 archived, as in the file' "$scratch/search-3.log" &&
    in_file 'SITE archived, as in the file' "$scratch/search-3.log" || {
    cat "$scratch/search-3.log"
    return 1
  }
  search_counts >"$scratch/counts-3"
  diff "$scratch/counts-1" "$scratch/counts-3"
}

someone_elses_project_stops_it() {
  mag=$(in_db "SELECT id FROM projects WHERE key = 'MAG'")
  status_is 200 "$(as "$admin_token" PUT "/api/projects/$mag" '{"name":"Magazinul altcuiva","description":null}')" || return 1
  run_search_data "$scratch/search-4.log"
  status=$?
  as "$admin_token" PUT "/api/projects/$mag" \
    '{"name":"Magazin online","description":"Magazinul online: catalogul, coșul, plata și livrarea."}' >/dev/null
  is 1 "$status" || return 1
  in_file "the project MAG isn't Magazin online" "$scratch/search-4.log" || {
    cat "$scratch/search-4.log"
    return 1
  }
  search_counts >"$scratch/counts-4"
  diff "$scratch/counts-1" "$scratch/counts-4"
}

no_password_printed() {
  for log in "$scratch"/search-*.log; do
    for secret in "$admin_password" "$new_admin_password" "$demo_password"; do
      lacks "$secret" "$log" || return 1
    done
  done
}

task_3() {
  echo "25.3 scripts/search-data.sh on the real API"
  check "search-data.sh is tracked as executable" tracked_and_executable scripts/search-data.sh
  check "dash reads it" dash -n scripts/search-data.sh
  check "without its passwords it gives its usage, asking nothing" search_data_needs_its_passwords
  check "on the real API in Production, it makes the data after demo-data.sh's" first_run_makes_the_data
  check "each project has as many tasks as the file, 262 in all" each_project_has_the_files_tasks
  check "every title, description and priority is the file's" tasks_are_the_files
  check "every task has the file's assignees" assignees_are_the_files
  check "the file's archived tasks are archived, and SITE" archived_ones_are_the_files
  check "run again, it makes nothing" second_run_makes_nothing
  check "a task and the project restored since are archived again, and nothing else made" \
    restored_ones_are_archived_again
  check "a project of its keys under another name stops it, adding nothing" someone_elses_project_stops_it
  check "nothing it printed holds a password" no_password_printed
}

# ---- Task 4: the group's documents and CI.

spec_has_the_step() {
  for section in '## 19. Chat cu RAG — pasul 1: `pgvector` și vectorizarea' '### 19.1 Baza de date' '### 19.2 Tabela vectorilor' '### 19.3 Modelul de vectorizare' \
    '### 19.4 Configurare și secrete' '### 19.5 Vectorizarea' '### 19.6 Căutarea de probă' '### 19.7 Pentru pașii următori'; do
    grep -qxF -- "$section" docs/SPEC.md || {
      echo "docs/SPEC.md has no line: $section"
      return 1
    }
  done
  file_has docs/SPEC.md chat.use scripts/search-data.sh scripts/check-search.sh '`check-search.sh` au nevoie'
}

backend_guide_is_current() {
  grep -qx '## Search' backend/CLAUDE.md || {
    echo "backend/CLAUDE.md has no Search section"
    return 1
  }
  file_has backend/CLAUDE.md chat.use scripts/check-search.sh POST /api/search/tasks
}

ci_has_pgvector() {
  file_has .github/actions/setup/action.yml postgresql-17-pgvector && file_has docs/ci.md postgresql-17-pgvector
}

this_script_has_a_shard() {
  sh scripts/ci-plan.sh push refs/heads/main '' | sed -n 's/^shards=//p' | grep -q '"check-search[ "]'
}

off_unless_set() {
  file_has compose.yaml 'Embeddings__Enabled: ${EMBEDDINGS_ENABLED:-false}' && file_has .env.example '# EMBEDDINGS_ENABLED='
}

architecture_tests_pass() {
  tests_pass Kanabol.Api.Tests \
    'FullyQualifiedName~Domain_WhenInspected_DoesNotDependOnPgvector|FullyQualifiedName~Application_WhenInspected_DoesNotDependOnAzureOrOpenAi' 2
}

# The probe of the real Azure is there, and skipped, so it calls nothing without KANABOL_AZURE_PROBE.
azure_probe_is_skipped() {
  log="$scratch/tests-probe.log"
  env -u KANABOL_AZURE_PROBE dotnet test backend/tests/Kanabol.Api.Tests/Kanabol.Api.Tests.csproj --nologo \
    --filter 'FullyQualifiedName~AzureEmbeddingModelProbe' >"$log" 2>&1 || {
    tail -n 20 "$log"
    return 1
  }
  grep -qE 'Skipped: *1,' "$log" && grep -qE 'Passed: *0,' "$log" || {
    tail -n 5 "$log"
    return 1
  }
}

task_4() {
  echo "25.4 The group's documents and CI"
  check "docs/SPEC.md has §19.1 to §19.7, chat.use and the group's scripts" spec_has_the_step
  check "backend/CLAUDE.md has the search, chat.use and this script" backend_guide_is_current
  check "the CI's PostgreSQL has postgresql-17-pgvector" ci_has_pgvector
  check "scripts/ci-plan.sh puts this script in a shard of the full gate" this_script_has_a_shard
  check "compose.yaml and .env.example leave the vectorization off unless set" off_unless_set
  check "Domain has no Pgvector, Application no Azure or OpenAI" architecture_tests_pass
  check "the probe of the real Azure is skipped without KANABOL_AZURE_PROBE" azure_probe_is_skipped
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4
fi
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed."
  exit 1
fi
