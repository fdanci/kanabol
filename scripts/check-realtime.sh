#!/bin/sh
# Acceptance checks for real time over SignalR (group 14 in the project's Tasks.md; docs/SPEC.md §9).
# The tests and specs hold the rules, so these checks run them, and add what they can't see: the real API
# process on a real PostgreSQL, reached over a real WebSocket as a browser reaches it, with the token only
# in the query string, its logs and what each connection hears, and the frontend's production build.
#
# Usage: scripts/check-realtime.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The backend tasks need Docker running and a PostgreSQL whose user
# may create databases, with its connection string in KANABOL_CHECK_DB or else in the API's user secrets
# (ConnectionStrings:Default); each run works in its own database and drops it. The frontend task needs
# only the packages that scripts/setup.sh installs.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_realtime_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5294
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

# The API in Production unless asked otherwise, with any other settings given as NAME=value. A start in
# another environment, or with settings, stops the one running first; the next plain start goes back.
start_api() { # start_api <environment> [NAME=value...]
  wanted="$*"
  [ -n "$api_pid" ] && [ "$api_environment" = "$wanted" ] && return 0
  stop_api
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  environment=$1
  shift
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
  ) >"$api_log" 2>&1 &
  api_pid=$!
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

api_is_up() {
  start_api Production
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
  printf '10.83.%s.%s' "$((used / 256))" "$((used % 256))"
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

# The first admin, whose first token still has to change the password: it goes in first_token.
first_admin_token() {
  api_is_up || return 1
  [ -n "${first_token:-}" ] && return 0
  status_is 200 "$(login "$admin_email" "$admin_password")" || return 1
  first_token=$(body_field accessToken)
}

# The first admin past the forced password change. The API may have stopped since the token was taken:
# it comes back first, and the token, signed with the same secret, still holds.
admin_is_ready() {
  api_is_up || return 1
  [ -n "${admin_token:-}" ] && return 0
  first_admin_token || return 1
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

# Project RT, owned by the admin, with Dan as a member; Eva is from outside.
team_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready eva eva@example.com 'Eva Din Afara' || return 1
  [ -n "${rt_id:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Timp real","key":"RT"}')" || return 1
  rt_id=$(body_field id)
  status_is 201 "$(as "$admin_token" POST "/api/projects/$rt_id/members" "{\"userId\":\"$dan_id\",\"role\":\"Member\"}")"
}

# ---- A SignalR client as a browser is one: the JSON protocol over a WebSocket, the token only in the
# query string. Built once, on first use.

hub_client() {
  [ -f "$scratch/hub.mjs" ] && return 0
  cat >"$scratch/hub.mjs" <<'JS'
// node hub.mjs <API address> <script file>: runs the script's steps and writes what happened as JSON.
// A step connects a name with a token, joins a project's board, or sends a request to the API, with the
// connection id of one of the names in X-Connection-Id when asked. What each connection heard comes last.
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
    } else if (step.join) {
      try {
        await hubs[step.join].invoke('JoinProject', step.project);
        result.steps.push('joined');
      } catch (error) {
        result.steps.push(`refused: ${error.message}`);
      }
    } else if (step.leave) {
      await hubs[step.leave].invoke('LeaveProject', step.project);
      result.steps.push('left');
    } else if (step.request) {
      const headers = { Authorization: `Bearer ${step.token}`, 'Content-Type': 'application/json' };
      if (step.as) headers['X-Connection-Id'] = hubs[step.as].connectionId;
      const answer = await fetch(`${base}${step.request}`, {
        method: step.method,
        headers,
        body: step.body === undefined ? undefined : JSON.stringify(step.body),
      });
      result.steps.push(String(answer.status));
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

# ---- 14.1: BoardHub at /hubs/board, the JWT from the query string, the groups, X-Connection-Id.

realtime=backend/src/Kanabol.Api/Realtime

hub_tests_pass() {
  named_tests_pass Kanabol.Api.Tests \
    BoardHubTests.Negotiate_WithoutAToken_Returns401 \
    BoardHubTests.Negotiate_WithTheTokenInTheQueryStringOrTheHeader_Returns200 \
    BoardHubTests.Negotiate_BeforeThePasswordChange_Returns403PasswordChangeRequired \
    BoardHubTests.AccessTokenInTheQueryString_OnAnApiPath_IsNotAccepted \
    BoardHubTests.Connect_WithTheTokenInTheQueryString_ConnectsOverAWebSocket \
    BoardHubTests.Connect_WithoutAToken_IsRefused \
    BoardHubTests.Connect_PutsTheConnectionInItsUsersOwnGroupOnly \
    BoardHubTests.JoinProject_AsAMember_HearsTheProjectsGroup \
    BoardHubTests.JoinProject_WithProjectsViewAll_HearsTheProjectsGroup \
    BoardHubTests.JoinProject_OfAProjectTheyDontSee_IsRefusedWithMemberRequiredAndHearsNothing \
    BoardHubTests.JoinProject_AsAMemberDeactivatedSinceTheirToken_IsRefused \
    BoardHubTests.LeaveProject_AfterJoining_StopsTheProjectsMessages \
    BoardHubTests.Connection_WhenItsTokenExpires_IsClosedByTheServer \
    BoardHubTests.JoinProject_IsLoggedWithTheUserTheProjectAndTheConnection
}

request_context_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~HttpRequestContextTests' 9
}

negotiate() { # negotiate [query string after negotiateVersion]: prints the status
  send POST "/hubs/board/negotiate?negotiateVersion=1${1:-}"
}

negotiation_needs_a_token() {
  api_is_up || return 1
  status_is 401 "$(negotiate)"
}

the_token_opens_the_hub_from_the_query_string() {
  admin_is_ready || return 1
  status_is 200 "$(negotiate "&access_token=$admin_token")"
}

the_query_string_opens_nothing_under_api() {
  admin_is_ready || return 1
  status_is 401 "$(send GET "/api/auth/me?access_token=$admin_token")" || return 1
  status_is 200 "$(as "$admin_token" GET /api/auth/me)"
}

the_hub_waits_for_the_password_change() {
  first_admin_token || return 1
  code_is 403 Auth.PasswordChangeRequired "$(negotiate "&access_token=$first_token")"
}

a_websocket_connects_with_the_token_in_the_address() {
  admin_is_ready || return 1
  hub "[{\"connect\":\"admin\",\"token\":\"$admin_token\"}]" || return 1
  steps_were connected || return 1
  [ -n "$(heard "r.connections.admin")" ]
}

a_member_joins_and_an_outsider_is_refused() {
  team_is_ready || return 1
  hub "[{\"connect\":\"dan\",\"token\":\"$dan_token\"},{\"join\":\"dan\",\"project\":\"$rt_id\"},
        {\"connect\":\"eva\",\"token\":\"$eva_token\"},{\"join\":\"eva\",\"project\":\"$rt_id\"}]" || return 1
  steps_were "connected|joined|connected|refused: An unexpected error occurred invoking 'JoinProject' on the server. HubException: Project.MemberRequired"
}

# One entry per join and per refusal, each with the user, the project and the connection (SPEC §8.2).
the_joins_are_logged_with_the_connection() {
  [ -f "$scratch/hub-result.json" ] || a_member_joins_and_an_outsider_is_refused >/dev/null || return 1
  dan_connection=$(heard "r.connections.dan")
  eva_connection=$(heard "r.connections.eva")
  grep -F '"@mt":"User {UserId} joined the board of project {ProjectId} on {ConnectionId}"' "$api_log" |
    grep -F "\"ConnectionId\":\"$dan_connection\"" | grep -qF "\"ProjectId\":\"$rt_id\"" || {
    echo "no join of $dan_connection in the log"
    return 1
  }
  grep -F '"@mt":"User {UserId} refused the board of project {ProjectId} on {ConnectionId}: {ErrorCode}"' "$api_log" |
    grep -F "\"ConnectionId\":\"$eva_connection\"" | grep -F "\"UserId\":\"$eva_id\"" | grep -qF '"@l":"Warning"'
}

no_token_in_the_log() { # no_token_in_the_log <token...>: none of their signatures is in the API's log
  for token in "$@"; do
    signature=${token##*.}
    [ -n "$signature" ] || return 1
    if grep -qF -- "$signature" "$api_log"; then
      grep -F -- "$signature" "$api_log" | cut -c1-200 | head -n 3
      return 1
    fi
  done
}

the_production_log_holds_no_token() {
  [ -n "${dan_token:-}" ] || a_member_joins_and_an_outsider_is_refused >/dev/null || return 1
  no_token_in_the_log "$admin_token" "$dan_token" "$eva_token"
}

# Development logs more: "Request starting" of ASP.NET's hosting writes the whole address, query included
# (docs/SPEC.md §8.1), so AccessTokenRedaction hides the value of access_token in every log entry.
connects_in() { # connects_in <environment>: the admin connects, with a token of its own
  start_api "$1" || return 1
  status_is 200 "$(login "$admin_email" "$new_admin_password")" || return 1
  logged_token=$(body_field accessToken)
  hub "[{\"connect\":\"admin\",\"token\":\"$logged_token\"}]" || return 1
  steps_were connected
}

the_development_log_holds_no_token() {
  admin_is_ready || return 1
  connects_in Development || return 1
  no_token_in_the_log "$logged_token"
}

# Or the check above would pass on a log that never had the address.
the_development_log_has_the_address_without_the_token() {
  [ "$api_environment" = Development ] || the_development_log_holds_no_token >/dev/null || return 1
  grep -F 'Request starting' "$api_log" | grep -qF '/hubs/board/negotiate?negotiateVersion=1&access_token=[hidden]' || {
    echo "no Request starting line for the negotiation, with the token hidden:"
    grep -F 'Request starting' "$api_log" | tail -n 3
    return 1
  }
}

redaction_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~AccessTokenRedactionTests' 3
}

task_1() {
  echo "14.1 BoardHub at /hubs/board, the JWT from the query string, the groups and X-Connection-Id"
  check "Docker is running" docker_is_running
  check "the hub is tracked" tracked "$realtime/BoardHub.cs"
  check "its groups are tracked" tracked "$realtime/BoardGroups.cs"
  check "the request context is tracked" tracked backend/src/Kanabol.Application/Abstractions/IRequestContext.cs
  check "the redaction of tokens in the logs is tracked" tracked backend/src/Kanabol.Api/Logging/AccessTokenRedaction.cs
  check "its implementation is tracked" tracked "$realtime/HttpRequestContext.cs"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the hub's tests pass: the token, the groups, the refusals, the expiry and the log" hub_tests_pass
  check "X-Connection-Id passes only as an id SignalR could make" request_context_tests_pass
  check "the real API refuses a negotiation without a token" negotiation_needs_a_token
  check "and opens it with the token in the query string" the_token_opens_the_hub_from_the_query_string
  check "a token in the query string opens nothing under /api" the_query_string_opens_nothing_under_api
  check "the hub waits for the forced password change" the_hub_waits_for_the_password_change
  check "a WebSocket with the token only in its address connects" a_websocket_connects_with_the_token_in_the_address
  check "a member joins the project's board, and an outsider is refused Project.MemberRequired" a_member_joins_and_an_outsider_is_refused
  check "the joins and refusals are logged with the user, the project and the connection" the_joins_are_logged_with_the_connection
  check "the Production log holds no token" the_production_log_holds_no_token
  check "a token in a logged address is hidden" redaction_tests_pass
  check "the Development log holds none either" the_development_log_holds_no_token
  check "and still has the hub's address, with the token hidden" the_development_log_has_the_address_without_the_token
}

# ---- 14.2: IBoardNotifier over the hub, fed by the domain event handlers, with small payloads.

realtime_app=backend/src/Kanabol.Application/Features/Realtime

board_events_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    BoardEventsHandlerTests.Create_OfATask_TellsTheProjectTaskCreatedWithWhoAndTheTab \
    BoardEventsHandlerTests.Create_ForAssignees_AlsoTellsTheProjectTheAssigneesChanged \
    BoardEventsHandlerTests.Move_ToAnotherColumn_TellsTheProjectTaskMovedWithTheColumnAndThePlace \
    BoardEventsHandlerTests.Move_WithinItsColumn_TellsOnlyOfTheTaskMovedNotOfThoseAround \
    BoardEventsHandlerTests.EachChangeOfAField_TellsTheProjectTaskUpdatedWithTheField \
    BoardEventsHandlerTests.Assign_TakingEveryoneOff_StillTellsTheProjectTheAssigneesChanged \
    BoardEventsHandlerTests.ArchiveAndRestore_TellTheProjectTaskArchivedThenTaskRestored \
    BoardEventsHandlerTests.AChange_WithoutAConnection_CarriesNoOrigin \
    BoardEventsHandlerTests.AChangeThatChangesNothing_TellsNobody \
    BoardEventsHandlerTests.Assign_OfSomeoneNew_TellsThemNotificationCreatedOnTheirOwnGroup \
    BoardEventsHandlerTests.AChange_WhenTheHubFails_IsSavedAllTheSameAndTheFailureLogged
}

step() { # step <name of the tab, or nothing> <method> <path> [JSON body]: one request of a hub run
  printf '{"request":"%s","method":"%s","token":"%s"%s%s}' "$3" "$2" "$admin_token" \
    "${1:+,\"as\":\"$1\"}" "${4:+,\"body\":$4}"
}

# One run on the real API: the admin's tab and Dan's on RT's board, Eva's connected but outside it. The
# admin's tab makes each change, with its connection id; the last one goes without. The answers are kept
# in order, and what each tab heard comes last.
changes_are_heard() {
  team_is_ready || return 1
  [ -n "${second_column:-}" ] && return 0
  status_is 200 "$(as "$admin_token" GET "/api/projects/$rt_id/board-columns")" || return 1
  column=$(json "b[1].id")
  status_is 201 "$(as "$admin_token" POST "/api/projects/$rt_id/tasks" '{"title":"Primul","priority":"Low"}')" || return 1
  first_task=$(body_field id)
  hub "[
    {\"connect\":\"admin\",\"token\":\"$admin_token\"},{\"join\":\"admin\",\"project\":\"$rt_id\"},
    {\"connect\":\"dan\",\"token\":\"$dan_token\"},{\"join\":\"dan\",\"project\":\"$rt_id\"},
    {\"connect\":\"eva\",\"token\":\"$eva_token\"},{\"join\":\"eva\",\"project\":\"$rt_id\"},
    $(step admin POST "/api/projects/$rt_id/tasks" '{"title":"Al doilea","priority":"High"}'),
    $(step admin PATCH "/api/tasks/$first_task/position" "{\"columnId\":\"$column\",\"position\":0}"),
    $(step admin PUT "/api/tasks/$first_task/title" '{"title":"Primul, redenumit"}'),
    $(step admin PUT "/api/tasks/$first_task/priority" '{"priority":"Critical"}'),
    $(step admin PUT "/api/tasks/$first_task/assignees" "{\"assigneeIds\":[\"$dan_id\"]}"),
    $(step admin DELETE "/api/tasks/$first_task"),
    $(step admin POST "/api/tasks/$first_task/restore"),
    $(step '' PUT "/api/tasks/$first_task/description" '{"description":"Fara tab"}')
  ]" || return 1
  steps_were "connected|joined|connected|joined|connected|refused: An unexpected error occurred invoking 'JoinProject' on the server. HubException: Project.MemberRequired|201|200|200|200|200|204|200|200" || return 1
  second_column=$column
}

# What a tab heard of the project, as event:field or event, in order, without NotificationCreated.
board_heard_by() { # board_heard_by <name>
  heard "r.heard.$1.filter(m => m.event !== 'NotificationCreated').map(m => m.event + (m.payload.field ? ':' + m.payload.field : '')).join(' ')"
}

both_boards_hear_every_change_in_order() {
  changes_are_heard || return 1
  expected='TaskCreated TaskMoved TaskUpdated:Title TaskUpdated:Priority TaskUpdated:Assignees TaskArchived TaskRestored TaskUpdated:Description'
  is "$expected" "$(board_heard_by admin)" || return 1
  is "$expected" "$(board_heard_by dan)"
}

an_outsider_hears_nothing() {
  changes_are_heard || return 1
  is 0 "$(heard "r.heard.eva.length")"
}

payload_keys_of() { # payload_keys_of <event>: the names in the first payload Dan heard of it
  heard "Object.keys(r.heard.dan.find(m => m.event === '$1').payload).join()"
}

the_payloads_are_small() {
  changes_are_heard || return 1
  is 'taskId,projectId,byUserId,originConnectionId' "$(payload_keys_of TaskCreated)" || return 1
  is 'taskId,projectId,byUserId,originConnectionId,columnId,position' "$(payload_keys_of TaskMoved)" || return 1
  is 'taskId,projectId,byUserId,originConnectionId,field' "$(payload_keys_of TaskUpdated)" || return 1
  is 'taskId,projectId,byUserId,originConnectionId' "$(payload_keys_of TaskArchived)" || return 1
  is 'taskId,projectId,byUserId,originConnectionId' "$(payload_keys_of TaskRestored)"
}

a_move_says_where_the_card_went() {
  changes_are_heard || return 1
  is "$first_task $rt_id $admin_id $second_column 0" \
    "$(heard "const p = r.heard.dan.find(m => m.event === 'TaskMoved').payload; [p.taskId, p.projectId, p.byUserId, p.columnId, p.position].join(' ')")"
}

# The admin's tab sent its own connection id, so it can tell its own changes; the last change came
# without one, and carries none.
each_event_carries_the_tab_that_made_it() {
  changes_are_heard || return 1
  tab=$(heard "r.connections.admin")
  [ -n "$tab" ] || return 1
  board="r.heard.dan.filter(m => m.event !== 'NotificationCreated')"
  is "$tab" "$(heard "[...new Set($board.slice(0, -1).map(m => m.payload.originConnectionId))].join()")" || return 1
  is null "$(heard "String($board.at(-1).payload.originConnectionId)")" || return 1
  is "$admin_id" "$(heard "[...new Set($board.map(m => m.payload.byUserId))].join()")"
}

only_the_new_assignee_hears_of_their_notification() {
  changes_are_heard || return 1
  notification=$(in_db "SELECT id FROM notifications WHERE user_id = '$dan_id' AND task_id = '$first_task'")
  [ -n "$notification" ] || return 1
  is "{\"notificationId\":\"$notification\"}" "$(heard "JSON.stringify(r.heard.dan.filter(m => m.event === 'NotificationCreated').map(m => m.payload)[0])")" || return 1
  is 1 "$(heard "r.heard.dan.filter(m => m.event === 'NotificationCreated').length")" || return 1
  is 0 "$(heard "r.heard.admin.filter(m => m.event === 'NotificationCreated').length")"
}

no_handler_failed() {
  changes_are_heard || return 1
  if grep -qF 'failed on domain event' "$api_log"; then
    grep -F 'failed on domain event' "$api_log" | cut -c1-300 | head -n 3
    return 1
  fi
}

task_2() {
  echo "14.2 IBoardNotifier over the hub, fed by the domain event handlers"
  check "Docker is running" docker_is_running
  check "the notifier is tracked" tracked "$realtime/HubBoardNotifier.cs"
  check "the handler of the task events is tracked" tracked "$realtime_app/BoardEventsHandler.cs"
  check "the handler of the notifications is tracked" tracked "$realtime_app/NotificationCreatedEventHandler.cs"
  check "the payloads are tracked" tracked "$realtime_app/TaskMovedDto.cs"
  check "the solution builds without warnings" solution_builds
  check "the handlers' tests pass: each event, its payload, its tab, and a hub that fails" board_events_tests_pass
  check "every change reaches both boards of the project, in order" both_boards_hear_every_change_in_order
  check "a connection outside the project hears none of it" an_outsider_hears_nothing
  check "the payloads carry only the task, the project, who, the tab, and the move or the field" the_payloads_are_small
  check "TaskMoved says the column and the place" a_move_says_where_the_card_went
  check "each event carries the tab that made it, and none without X-Connection-Id" each_event_carries_the_tab_that_made_it
  check "the new assignee alone hears NotificationCreated, on their own group" only_the_new_assignee_hears_of_their_notification
  check "no handler failed" no_handler_failed
}

if [ "$#" -eq 0 ]; then
  set -- 1 2
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
