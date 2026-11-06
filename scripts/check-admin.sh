#!/bin/sh
# Acceptance checks for the administration of users and roles (group 10 in the project's Tasks.md).
# The specs and tests hold the rules, so these checks run them, and add what they can't see: the
# routes, the texts as extracted for translation, the production build, and for the browser's logs
# the real API process in Production on a real PostgreSQL, with its log.
#
# Usage: scripts/check-admin.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Run scripts/setup.sh first, which installs the packages. Task 3
# also needs Docker running, for its tests, and a PostgreSQL whose user may create databases, with its
# connection string in KANABOL_CHECK_DB or else in the API's user secrets (ConnectionStrings:Default).
# It works in a database of its own and drops it.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
web=frontend
app=$web/src/app

cleanup() {
  stop_api
  drop_database
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

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

tracked() {
  [ -f "$1" ] && [ -n "$(git ls-files -- "$1")" ]
}

in_file() { # in_file <text> <file>
  grep -qF -- "$1" "$2"
}

lacks() { # lacks <text> <file>
  ! grep -qF -- "$1" "$2" || {
    grep -nF -- "$1" "$2" | head -n 5
    return 1
  }
}

plain() { # Without the colors the tools print
  sed 's/\x1b\[[0-9;]*m//g' "$1"
}

specs_pass() { # specs_pass <spec file or glob, from frontend/> <minimum passed>
  log="$scratch/specs-$(printf '%s' "$1" | tr '/*' '__').log"
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

# The production build, made once per run.
built() {
  [ -f "$scratch/built" ] && return 0
  (cd "$web" && npx ng build --output-path "$scratch/dist") >"$scratch/build.log" 2>&1 || {
    plain "$scratch/build.log" | tail -n 25
    return 1
  }
  cat "$scratch"/dist/browser/*.js >"$scratch/bundle.js" && touch "$scratch/built"
}

bundle_has() { # bundle_has <text>
  built && in_file "$1" "$scratch/bundle.js"
}

css_has() { # css_has <text>: compares without whitespace, which the minifier keeps in places
  built || return 1
  tr -d ' 
' <"$(ls "$scratch"/dist/browser/styles-*.css | head -n 1)" | grep -qF -- "$1" || {
    echo "not in the built CSS: $1"
    return 1
  }
}

# The texts as ng extract-i18n writes them for translation, made once per run.
extracted() {
  [ -f "$scratch/locale/messages.xlf" ] && return 0
  (cd "$web" && npx ng extract-i18n --output-path "$scratch/locale") >"$scratch/extract.log" 2>&1 || {
    plain "$scratch/extract.log" | tail -n 25
    return 1
  }
}

messages_have() { # messages_have <text>
  extracted && in_file "$1" "$scratch/locale/messages.xlf"
}

# A page of the shell that loads with its first visit, behind its permission: withPermission in
# app.routes.ts puts the 403 page after it on the same path.
lazy_behind() { # lazy_behind <path> <PERMISSIONS key> <routes file, from app/>
  node -e "
    const source = require('fs').readFileSync('$app/app.routes.ts', 'utf8');
    const route = new RegExp('withPermission\\\\(PERMISSIONS\\\\.$2, \\\\{\\\\s*path: \\'$1\\',[^}]*loadChildren: \\\\(\\\\) =>\\\\s*import\\\\(\\'./$3\\'\\\\)');
    if (!route.test(source)) { console.log('no lazy route for $1 behind $2 in app.routes.ts'); process.exit(1); }
    if (source.includes(\"placeholder('$1'\")) { console.log('the placeholder for $1 is still there'); process.exit(1); }
  "
}

# ---- The real API, for 10.3: in Production, on a database of its own, dropped at the end.

check_db="kanabol_admin_check_$"
api_dir=backend/src/Kanabol.Api
api_port=5284
API_URL="http://127.0.0.1:$api_port"
api_log="$scratch/api.log"
api_pid=
admin_connection=
admin_email=admin@kanabol.local
admin_password=only-for-these-checks
new_admin_password='alta parola pentru verificari'

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

tests_pass() { # tests_pass <project> <filter> <minimum passed>
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

api_is_up() {
  [ -n "$api_pid" ] && return 0
  need_database || return 1
  dotnet build "$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  (
    cd "$api_dir" || exit 1
    ASPNETCORE_ENVIRONMENT=Production \
      ConnectionStrings__Default=$(connection_to "$check_db") \
      Jwt__Secret=only-for-these-checks-and-long-enough-to-pass \
      Storage__Root="$scratch/attachments" \
      Admin__InitialPassword=$admin_password \
      exec dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL"
  ) >"$api_log" 2>&1 &
  api_pid=$!

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

stop_api() {
  [ -n "$api_pid" ] || return 0
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
}

drop_database() {
  [ -n "$admin_connection" ] || return 0
  sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null
}

send() { # send <method> <path> [curl arguments...]: leaves the answer in the scratch folder, prints the status
  method=$1
  path=$2
  shift 2
  curl -sS --max-time 30 -o "$scratch/body" -w '%{http_code}' -X "$method" "$@" "$API_URL$path"
}

as() { # as <token> <method> <path> <JSON body> [curl arguments...]
  token=$1
  method=$2
  path=$3
  body=$4
  shift 4
  send "$method" "$path" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' -d "$body" "$@"
}

body_field() { # body_field <name>: the value of a text field in the last answer
  sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$scratch/body"
}

admin_is_ready() {
  [ -n "${admin_token:-}" ] && return 0
  api_is_up || return 1
  is 200 "$(send POST /api/auth/login -H 'Content-Type: application/json' -H 'X-Forwarded-For: 10.84.0.1' \
    -d "{\"email\":\"$admin_email\",\"password\":\"$admin_password\"}")" || return 1
  first_token=$(body_field accessToken)
  is 200 "$(as "$first_token" POST /api/auth/change-password \
    "{\"currentPassword\":\"$admin_password\",\"newPassword\":\"$new_admin_password\"}")" || {
    cat "$scratch/body"
    return 1
  }
  admin_token=$(body_field accessToken)
  [ -n "$admin_token" ]
}

# ---- 10.1

task_1() {
  echo "10.1 Roles"
  for file in models.ts permission-groups.ts roles-api.ts roles-store.ts role-editor.ts role-editor.html \
    roles-page.ts roles-page.html roles.routes.ts; do
    check "features/roles/$file is tracked" tracked "$app/features/roles/$file"
  done
  check "shared/models/page.ts is tracked" tracked "$app/shared/models/page.ts"
  check "the specs of the roles pass" specs_pass "src/app/features/roles/*.spec.ts" 24
  check "a form keeps to itself the codes it shows under a field" specs_pass src/app/core/api/error-interceptor.spec.ts 16
  check "/roles loads with its first visit, behind roles.manage" lazy_behind roles rolesManage features/roles/roles.routes
  check "the system role says why it can't change" messages_have 'Rolul de sistem nu se poate modifica'
  check "the permissions have Romanian names" messages_have 'Arhivează și restaurează task-uri'
  check "user counts follow the Romanian plural" messages_have ' de utilizatori}'
  check "the build reads the permissions the API knows" bundle_has '/permissions'
}

# ---- 10.2

# The chip of a deactivated account is text on the neutral chip background, color-mix(navy 6%,
# surface-0) (UI spec 6.5). Its text has to pass AA at 12px in both themes, computed from the tokens.
inactive_chip_reads() {
  node -e "
    const fs = require('fs');
    const chip = fs.readFileSync('$app/shared/ui/chip.ts', 'utf8');
    const rule = chip.match(/((?::host\\([a-z-]+\\),?\\s*)*:host\\(\\.inactive\\)[^{]*)\\{([^}]*)\\}/);
    const text = rule && (rule[0].match(/--_fg:\\s*var\\(--kb-([a-z0-9-]+)\\)/) || [])[1];
    if (!text) { console.log('no text color for .inactive in chip.ts'); process.exit(1); }
    const source = fs.readFileSync('$web/src/styles/_tokens.scss', 'utf8');
    const split = source.indexOf('@mixin dark');
    const tokens = (part) => Object.fromEntries([...part.matchAll(/--kb-([a-z0-9-]+)\\s*:\\s*(#[0-9a-fA-F]{6})\\s*;/g)].map((m) => [m[1], m[2]]));
    const light = tokens(source.slice(0, split));
    const dark = { ...light, ...tokens(source.slice(split)) };
    const channels = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
    const mix = (a, b, share) => channels(a).map((c, i) => c * share + channels(b)[i] * (1 - share));
    const luminance = (rgb) => {
      const [r, g, b] = rgb.map((c) => c / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    let bad = 0;
    for (const [mode, t] of [['light', light], ['dark', dark]]) {
      const [high, low] = [luminance(channels(t[text])), luminance(mix(t['navy-900'], t['surface-0'], 0.06))].sort((x, y) => y - x);
      const ratio = (high + 0.05) / (low + 0.05);
      if (ratio < 4.5) { console.log(mode + ': ' + text + ' on the neutral chip is ' + ratio.toFixed(2) + ':1'); bad++; }
    }
    process.exit(bad ? 1 : 0);
  "
}

task_2() {
  echo "10.2 Users"
  for file in models.ts users-api.ts users-store.ts user-fields.ts user-sheet.ts user-sheet.html role-sheet.ts     password-sheet.ts sheet.scss users-page.ts users-page.html users.routes.ts; do
    check "features/users/$file is tracked" tracked "$app/features/users/$file"
  done
  check "shared/ui/paginator-labels.ts is tracked" tracked "$app/shared/ui/paginator-labels.ts"
  check "the specs of the users pass" specs_pass "src/app/features/users/*.spec.ts" 23
  check "a sheet asks before it throws changes away" specs_pass src/app/shared/ui/confirm-dialog.spec.ts 7
  check "a locked account and a password to change have chips with icons" specs_pass src/app/shared/ui/chip.spec.ts 3
  check "the paginators speak Romanian" specs_pass src/app/app.config.spec.ts 8
  check "/users loads with its first visit, behind users.manage" lazy_behind users usersManage features/users/users.routes
  check "table rows are 40px" css_has '--mat-table-row-item-container-height:40px'
  check "under a 28px header" css_has '--mat-table-header-container-height:28px'
  check "with no lines between them" css_has '--mat-table-row-item-outline-width:0'
  check "but zebra stripes" css_has '.kb-table-row:nth-child('
  check "the ⋮ of a row shows on hover" css_has '.kb-row-actions{opacity:0'
  check "the paginator is a 36px bar" css_has '--mat-paginator-container-size:36px'
  check "a destructive menu item is red" css_has '--mat-menu-item-label-text-color:var(--kb-danger-600)'
  check "the states of an account are named" messages_have 'Parolă de schimbat'
  check "a deactivated account's chip passes AA, in both themes" inactive_chip_reads
  check "a deactivation says what follows from it" messages_have 'Rămâne în istoric și îl poți activa oricând.'
}

# ---- 10.3

batch_marker="check-admin-$"

anonymous_batch_is_refused() {
  api_is_up || return 1
  is 401 "$(send POST /api/client-logs -H 'Content-Type: application/json' \
    -d '{"entries":[{"level":"error","message":"no session"}]}')"
}

batch_is_taken() {
  admin_is_ready || return 1
  is 204 "$(as "$admin_token" POST /api/client-logs \
    "{\"entries\":[{\"level\":\"error\",\"message\":\"$batch_marker\",\"url\":\"https://kanabol.local/users\",\"correlationId\":\"req-$batch_marker\"}],\"dropped\":2}" \
    -A 'check-admin browser')" || {
    cat "$scratch/body"
    return 1
  }
}

batch_is_in_the_log() {
  sleep 1
  grep -F "$batch_marker" "$api_log" | grep -F "req-$batch_marker" | grep -F 'check-admin browser' |
    grep -qF '"Error"' || {
    echo "no error line with the message, the request's correlation id and the user agent:"
    grep -F "$batch_marker" "$api_log" | head -n 3
    return 1
  }
}

drops_are_in_the_log() {
  grep -F 'dropped' "$api_log" | grep -qF '"DroppedCount":2'
}

big_batch_is_refused() {
  admin_is_ready || return 1
  entries=$(node -e "console.log(JSON.stringify(Array.from({ length: 11 }, () => ({ level: 'warn', message: 'x' }))))")
  is 400 "$(as "$admin_token" POST /api/client-logs "{\"entries\":$entries}")"
}

logger_sends_in_production() {
  in_file 'inject(Injector)' "$app/core/logging/logger.ts" && in_file '.get(ClientLogs).add(' "$app/core/logging/logger.ts"
}

task_3() {
  echo "10.3 The browser's warnings and errors in the API's logs"
  check "Docker is running" docker_is_running
  check "the controller is tracked" tracked backend/src/Kanabol.Api/Controllers/ClientLogsController.cs
  check "core/logging/client-logs.ts is tracked" tracked "$app/core/logging/client-logs.ts"
  check "the endpoint's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ClientLogs' 10
  check "the specs of the Logger and of ClientLogs pass" specs_pass "src/app/core/logging/*.spec.ts" 15
  check "the Logger hands its warnings and errors to ClientLogs" logger_sends_in_production
  check "the build sends them to /client-logs" bundle_has '/client-logs'
  check "the real API refuses a batch without a session" anonymous_batch_is_refused
  check "and takes one from a signed-in user" batch_is_taken
  check "whose error it logs with the request's correlation id and the user agent" batch_is_in_the_log
  check "and how many entries the browser dropped" drops_are_in_the_log
  check "a batch of more than 10 entries is refused" big_batch_is_refused
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3
fi

for task in "$@"; do
  if command -v "task_$task" >/dev/null 2>&1; then
    "task_$task"
  else
    echo "There is no task $task."
    failures=$((failures + 1))
  fi
done

echo
if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
