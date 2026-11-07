#!/bin/sh
# Acceptance checks for comments and attachments (group 15 in the project's Tasks.md; docs/SPEC.md §4.4,
# §10). The tests and specs hold the rules, so these checks run them, and add what they can't see: the real
# API process on a real PostgreSQL, its answers, what it keeps in the database and on disk, and the
# frontend's production build.
#
# Usage: scripts/check-comments.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. What each task does, and needs:
#   1  comments on a task, their notification and CommentAdded on the hub: the tests, then the real API in
#      Production. Docker, PostgreSQL.
#   2  files on a task, LocalFileStorage and TaskUpdated with Attachments: the tests, then the real API in
#      Production, with what it keeps on disk. Docker, PostgreSQL.
# PostgreSQL means one whose user may create databases, with its connection string in KANABOL_CHECK_DB
# or else in the API's user secrets (ConnectionStrings:Default); each run works in its own database and
# drops it. Docker runs the tests' own PostgreSQL.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
check_db="kanabol_comments_check_$$"
api_dir=backend/src/Kanabol.Api
api_port=5295
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

# Project CMT, owned by the admin, with Dan and Ioana as members; Eva is from outside. The admin creates
# task CMT-1, "Login SSO", for Dan.
team_is_ready() {
  colleague_is_ready dan dan@example.com 'Dan Ionescu' || return 1
  colleague_is_ready ioana ioana@example.com 'Ioana Marin' || return 1
  colleague_is_ready eva eva@example.com 'Eva Din Afara' || return 1
  [ -n "${cmt_task:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST /api/projects '{"name":"Comentarii","key":"CMT"}')" || return 1
  cmt_id=$(body_field id)
  for member in "$dan_id" "$ioana_id"; do
    status_is 201 "$(as "$admin_token" POST "/api/projects/$cmt_id/members" "{\"userId\":\"$member\",\"role\":\"Member\"}")" || return 1
  done
  status_is 201 "$(as "$admin_token" POST "/api/projects/$cmt_id/tasks" "{\"title\":\"Login SSO\",\"priority\":\"Medium\",\"assigneeIds\":[\"$dan_id\"]}")" || return 1
  cmt_task=$(body_field id)
}

# ---- 15.1: comments on a task, their notification and CommentAdded on the hub.

comments_app=backend/src/Kanabol.Application/Features/Comments

comment_domain_tests_pass() {
  tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskCommentTests' 7
}

comment_handler_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~Kanabol.Application.Tests.Features.Comments' 27
}

comment_notification_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~CommentAddedNotificationTests' 7
}

comment_hub_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    BoardEventsHandlerTests.Comment_OnATask_TellsTheProjectCommentAddedWithTheCommentAndTheTab \
    BoardEventsHandlerTests.EditOfAComment_TellsTheBoardNothing
}

comment_endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~CommentsEndpointTests' 10
}

# Ioana comments on CMT-1, which the admin created for Dan: 201, where it is, and the comment as written.
a_member_comments() {
  team_is_ready || return 1
  [ -n "${ioana_comment:-}" ] && return 0
  status_is 201 "$(send POST "/api/tasks/$cmt_task/comments" -D "$scratch/headers" -H "Authorization: Bearer $ioana_token" \
    -H 'Content-Type: application/json' --data-binary '{"body":"Merge cu **Azure AD**"}')" || return 1
  ioana_comment=$(body_field id)
  is "Merge cu **Azure AD**" "$(json b.body)" || return 1
  is true "$(json b.canEdit)" || return 1
  location=$(tr -d '\r' <"$scratch/headers" | sed -n 's/^[Ll]ocation: //p')
  is "/api/comments/$ioana_comment" "$location"
}

# Dan's answer comes after it, and each reads both, the oldest first, only their own editable.
the_comments_come_from_the_oldest() {
  a_member_comments || return 1
  status_is 201 "$(as "$dan_token" POST "/api/tasks/$cmt_task/comments" '{"body":"Și la mine"}')" || return 1
  dan_comment=$(body_field id)
  status_is 200 "$(as "$dan_token" GET "/api/tasks/$cmt_task/comments")" || return 1
  is "$ioana_comment:false,$dan_comment:true" "$(json "b.map(c => c.id + ':' + c.canEdit).join()")"
}

# SPEC §4.3: the assignees and the creator, each once, not the author, with the English message.
each_follower_is_notified_once() {
  a_member_comments || return 1
  expected=$(printf '%s|1|Ioana Marin commented on CMT-1: Login SSO\n%s|1|Ioana Marin commented on CMT-1: Login SSO\n' "$admin_id" "$dan_id" | sort)
  is "$expected" "$(in_db "SELECT user_id, count(*), min(message) FROM notifications WHERE type = 'CommentAdded' AND task_id = '$cmt_task' AND message LIKE 'Ioana%' GROUP BY user_id" | sort)"
}

editing_anothers_comment_is_refused() {
  a_member_comments || return 1
  code_is 403 Comment.NotYours "$(as "$admin_token" PUT "/api/comments/$ioana_comment" '{"body":"Schimbat de admin"}')" || return 1
  status_is 200 "$(as "$ioana_token" GET "/api/comments/$ioana_comment")" || return 1
  is "Merge cu **Azure AD**" "$(json b.body)"
}

its_author_edits_it_and_nobody_hears() {
  a_member_comments || return 1
  before=$(in_db "SELECT count(*) FROM notifications WHERE type = 'CommentAdded'")
  status_is 200 "$(as "$ioana_token" PUT "/api/comments/$ioana_comment" '{"body":"Merge cu **Azure AD**, testat"}')" || return 1
  is "Merge cu **Azure AD**, testat" "$(json b.body)" || return 1
  [ "$(json b.updatedAt)" != null ] || return 1
  is "$before" "$(in_db "SELECT count(*) FROM notifications WHERE type = 'CommentAdded'")"
}

a_body_over_5000_characters_is_refused() {
  team_is_ready || return 1
  long=$(node -e "process.stdout.write('a'.repeat(5001))")
  status_is 400 "$(as "$ioana_token" POST "/api/tasks/$cmt_task/comments" "{\"body\":\"$long\"}")" || return 1
  [ "$(json "Object.keys(b.errors).join()")" = body ] || return 1
  exact=$(node -e "process.stdout.write('a'.repeat(5000))")
  status_is 201 "$(as "$ioana_token" POST "/api/tasks/$cmt_task/comments" "{\"body\":\"$exact\"}")"
}

an_outsider_reads_and_writes_nothing() {
  a_member_comments || return 1
  code_is 403 Project.MemberRequired "$(as "$eva_token" GET "/api/tasks/$cmt_task/comments")" || return 1
  code_is 403 Project.MemberRequired "$(as "$eva_token" GET "/api/comments/$ioana_comment")" || return 1
  code_is 403 Project.MemberRequired "$(as "$eva_token" POST "/api/tasks/$cmt_task/comments" '{"body":"Din afara"}')"
}

nothing_deletes_a_comment() {
  a_member_comments || return 1
  status_is 405 "$(as "$ioana_token" DELETE "/api/comments/$ioana_comment")" || return 1
  is 1 "$(in_db "SELECT count(*) FROM comments WHERE id = '$ioana_comment'")"
}

# Last, since it archives CMT-2: a comment on it is a 409, and only tasks.archive still reads them.
an_archived_task_takes_no_comment() {
  team_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$cmt_id/tasks" '{"title":"De arhivat","priority":"Low"}')" || return 1
  archived_task=$(body_field id)
  status_is 201 "$(as "$dan_token" POST "/api/tasks/$archived_task/comments" '{"body":"Inainte"}')" || return 1
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$archived_task")" || return 1
  code_is 409 Task.Archived "$(as "$dan_token" POST "/api/tasks/$archived_task/comments" '{"body":"Dupa"}')" || return 1
  code_is 404 Task.NotFound "$(as "$dan_token" GET "/api/tasks/$archived_task/comments")" || return 1
  status_is 200 "$(as "$admin_token" GET "/api/tasks/$archived_task/comments")" || return 1
  is "Inainte:false" "$(json "b.map(c => c.body + ':' + c.canEdit).join()")"
}

task_1() {
  echo "15.1 Comments on a task, their notification and CommentAdded on the hub"
  check "Docker is running" docker_is_running
  check "the comment is tracked" tracked backend/src/Kanabol.Domain/Tasks/Comment.cs
  check "its event is tracked" tracked backend/src/Kanabol.Domain/Tasks/Events/CommentAddedEvent.cs
  check "its handlers are tracked" tracked "$comments_app/Commands/AddComment/AddCommentCommandHandler.cs"
  check "its notification is tracked" tracked backend/src/Kanabol.Application/Features/Notifications/CommentAddedEventHandler.cs
  check "its controller is tracked" tracked backend/src/Kanabol.Api/Controllers/CommentsController.cs
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the domain's tests pass: an open task only, markdown kept, no history" comment_domain_tests_pass
  check "the handlers' tests pass: who reads, who writes, who edits, the limits and the codes" comment_handler_tests_pass
  check "the notification's tests pass: assignees and creator once, not the author nor who left" comment_notification_tests_pass
  check "a comment sends CommentAdded with its id, and an edit nothing" comment_hub_tests_pass
  check "the endpoints' tests pass" comment_endpoint_tests_pass
  check "the real API takes a member's comment, with where it is" a_member_comments
  check "it gives every comment from the oldest, only the caller's own editable" the_comments_come_from_the_oldest
  check "the creator and the assignee are notified once each, the author not at all" each_follower_is_notified_once
  check "editing someone else's comment is 403 Comment.NotYours, and changes nothing" editing_anothers_comment_is_refused
  check "its author edits it, and nobody is notified" its_author_edits_it_and_nobody_hears
  check "a body of 5,001 characters is 400 on body, one of 5,000 goes in" a_body_over_5000_characters_is_refused
  check "an outsider reads and writes nothing, 403 Project.MemberRequired" an_outsider_reads_and_writes_nothing
  check "nothing deletes a comment" nothing_deletes_a_comment
  check "an archived task takes no comment, 409 Task.Archived, and only tasks.archive reads its own" an_archived_task_takes_no_comment
  check "backend/CLAUDE.md says how comments work" in_file '`GET /api/tasks/{id}/comments`' backend/CLAUDE.md
}

# ---- 15.2: files on a task, LocalFileStorage, and TaskUpdated with Attachments on the hub.

attachments_app=backend/src/Kanabol.Application/Features/Attachments
megabyte=1048576

attachment_domain_tests_pass() {
  tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskAttachmentTests' 5
}

attachment_handler_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~AttachmentHandlerTests' 33
}

storage_tests_pass() {
  tests_pass Kanabol.Application.Tests 'FullyQualifiedName~LocalFileStorageTests' 9
}

attachment_hub_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    AttachmentHandlerTests.Add_TellsTheBoardTheAttachmentsChanged \
    AttachmentHandlerTests.Delete_ByWhoeverUploadedIt_TakesTheRowAndTheFile
}

attachment_endpoint_tests_pass() {
  tests_pass Kanabol.Api.Tests 'FullyQualifiedName~AttachmentsEndpointTests' 31
}

# docs/SPEC.md §10: the default list, written out here, so a change to appsettings.json shows.
the_default_extensions_are_the_specs() {
  is 'pdf,png,jpg,jpeg,gif,txt,md,csv,xlsx,docx,zip' "$(node -e "
    const settings = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'));
    console.log(settings.Storage.AllowedExtensions.join());" "$(native backend/src/Kanabol.Api/appsettings.json)")"
}

a_file_of() { # a_file_of <name> <bytes>: a file of zeros in the scratch folder, its path printed
  head -c "$2" /dev/zero >"$scratch/$1"
  printf '%s' "$scratch/$1"
}

upload() { # upload <token> <task id> <file> [name it goes as]: prints the status
  send POST "/api/tasks/$2/attachments" -D "$scratch/headers" -H "Authorization: Bearer $1" \
    -F "file=@$(native "$3");filename=${4:-$(basename "$3")}"
}

download() { # download <token> <attachment id>: the file in the scratch folder, its headers apart; prints the status
  rm -f "$scratch/downloaded"
  curl -sS --max-time 60 -o "$scratch/downloaded" -D "$scratch/headers" -w '%{http_code}' \
    -H "Authorization: Bearer $1" "$API_URL/api/attachments/$2"
}

header_of() { # header_of <name>: its value in the last answer's headers
  tr -d '\r' <"$scratch/headers" | grep -i "^$1:" | head -n 1 | sed 's/^[^:]*: //'
}

stored_file() { # stored_file <attachment id>: where the API keeps it
  printf '%s/attachments/%s' "$scratch" "$(in_db "SELECT storage_key FROM attachments WHERE id = '$1'")"
}

# CMT gets task "Fisiere", by the admin, for the files.
files_task_is_ready() {
  team_is_ready || return 1
  [ -n "${files_task:-}" ] && return 0
  status_is 201 "$(as "$admin_token" POST "/api/projects/$cmt_id/tasks" '{"title":"Fisiere","priority":"Low"}')" || return 1
  files_task=$(body_field id)
  printf '%%PDF-1.7 un brief de proba\n' >"$scratch/brief.pdf"
}

a_member_attaches_a_file() {
  files_task_is_ready || return 1
  [ -n "${dan_file:-}" ] && return 0
  status_is 201 "$(upload "$dan_token" "$files_task" "$scratch/brief.pdf" 'Brief Final.pdf')" || return 1
  dan_file=$(body_field id)
  is 'Brief Final.pdf' "$(json b.fileName)" || return 1
  is application/pdf "$(json b.contentType)" || return 1
  is "$(wc -c <"$scratch/brief.pdf" | tr -d ' ')" "$(json b.size)" || return 1
  is "/api/attachments/$dan_file" "$(header_of location)"
}

# docs/SPEC.md §10: {yyyy}/{MM}/{guid}{ext} under the root, the same bytes, and nothing of the name.
the_file_is_on_disk_under_a_key_of_its_own() {
  a_member_attaches_a_file || return 1
  key=$(in_db "SELECT storage_key FROM attachments WHERE id = '$dan_file'")
  printf '%s' "$key" | grep -qE "^$(date -u +%Y)/$(date -u +%m)/[0-9a-f]{32}\.pdf$" || {
    echo "the key is $key"
    return 1
  }
  cmp -s "$scratch/brief.pdf" "$(stored_file "$dan_file")" || return 1
  if find "$scratch/attachments" -name '*Brief*' | grep -q .; then
    echo "the original name reached the disk"
    return 1
  fi
}

the_task_lists_its_files() {
  a_member_attaches_a_file || return 1
  status_is 200 "$(as "$dan_token" GET "/api/tasks/$files_task")" || return 1
  is "$dan_file:Brief Final.pdf:true" "$(json "b.attachments.map(a => a.id + ':' + a.fileName + ':' + a.canDelete).join()")"
}

another_member_downloads_it_as_an_attachment() {
  a_member_attaches_a_file || return 1
  status_is 200 "$(download "$ioana_token" "$dan_file")" || return 1
  cmp -s "$scratch/brief.pdf" "$scratch/downloaded" || return 1
  header_of content-disposition | grep -q '^attachment;' || {
    echo "Content-Disposition: $(header_of content-disposition)"
    return 1
  }
  is nosniff "$(header_of x-content-type-options)" || return 1
  header_of content-type | grep -q '^application/pdf'
}

an_outsider_downloads_nothing() {
  a_member_attaches_a_file || return 1
  code_is 403 Project.MemberRequired "$(as "$eva_token" GET "/api/attachments/$dan_file")"
}

refused_on_file() { # refused_on_file <status>: a 400 whose only error is on file
  status_is 400 "$1" || return 1
  is file "$(json "Object.keys(b.errors).join()")"
}

# 25 MB is 25 × 1,048,576 bytes; the request may hold a little more, and above that Kestrel stops it.
twenty_five_megabytes_go_in_and_one_byte_more_does_not() {
  files_task_is_ready || return 1
  status_is 201 "$(upload "$dan_token" "$files_task" "$(a_file_of exact.zip $((25 * megabyte)))")" || return 1
  refused_on_file "$(upload "$dan_token" "$files_task" "$(a_file_of over.zip $((25 * megabyte + 1)))")"
}

a_file_far_over_the_limit_is_refused_on_file_too() {
  files_task_is_ready || return 1
  refused_on_file "$(upload "$dan_token" "$files_task" "$(a_file_of far-over.zip $((28 * megabyte)))")"
}

an_extension_outside_the_list_is_refused() {
  files_task_is_ready || return 1
  for name in program.exe pagina.html arhiva.tar.gz fara-extensie; do
    refused_on_file "$(upload "$dan_token" "$files_task" "$scratch/brief.pdf" "$name")" || return 1
  done
  is 1 "$(in_db "SELECT count(*) FROM attachments WHERE task_id = '$files_task' AND file_name = 'Brief Final.pdf'")"
}

two_files_in_one_request_are_refused() {
  files_task_is_ready || return 1
  refused_on_file "$(send POST "/api/tasks/$files_task/attachments" -H "Authorization: Bearer $dan_token" \
    -F "file=@$(native "$scratch/brief.pdf");filename=unu.pdf" -F "file=@$(native "$scratch/brief.pdf");filename=doi.pdf")"
}

# docs/SPEC.md §13.3: compose overrides the list as Storage__AllowedExtensions__0, __1 and so on.
the_list_comes_from_the_settings() {
  files_task_is_ready || return 1
  start_api Production Storage__AllowedExtensions__0=exe || return 1
  status_is 201 "$(upload "$dan_token" "$files_task" "$scratch/brief.pdf" unealta.exe)" || return 1
  refused_on_file "$(upload "$dan_token" "$files_task" "$scratch/brief.pdf" brief.pdf)" || return 1
  start_api Production
}

someone_else_deletes_nothing() {
  a_member_attaches_a_file || return 1
  code_is 403 Attachment.NotYours "$(as "$ioana_token" DELETE "/api/attachments/$dan_file")" || return 1
  [ -f "$(stored_file "$dan_file")" ]
}

its_uploader_deletes_it_file_and_all() {
  a_member_attaches_a_file || return 1
  file=$(stored_file "$dan_file")
  status_is 204 "$(as "$dan_token" DELETE "/api/attachments/$dan_file")" || return 1
  is 0 "$(in_db "SELECT count(*) FROM attachments WHERE id = '$dan_file'")" || return 1
  [ ! -e "$file" ] || {
    echo "$file is still there"
    return 1
  }
  code_is 404 Attachment.NotFound "$(as "$dan_token" GET "/api/attachments/$dan_file")"
}

an_archived_task_takes_no_file() {
  files_task_is_ready || return 1
  status_is 201 "$(as "$admin_token" POST "/api/projects/$cmt_id/tasks" '{"title":"Arhivat cu fisiere","priority":"Low"}')" || return 1
  closed_task=$(body_field id)
  status_is 204 "$(as "$admin_token" DELETE "/api/tasks/$closed_task")" || return 1
  code_is 409 Task.Archived "$(upload "$dan_token" "$closed_task" "$scratch/brief.pdf")"
}

task_2() {
  echo "15.2 Files on a task: LocalFileStorage, upload, download, delete, and TaskUpdated with Attachments"
  check "Docker is running" docker_is_running
  check "the attachment is tracked" tracked backend/src/Kanabol.Domain/Tasks/Attachment.cs
  check "its event is tracked" tracked backend/src/Kanabol.Domain/Tasks/Events/AttachmentsChangedEvent.cs
  check "LocalFileStorage is tracked" tracked backend/src/Kanabol.Infrastructure/Storage/LocalFileStorage.cs
  check "the rules of the extensions are tracked" tracked backend/src/Kanabol.Infrastructure/Storage/StorageAttachmentRules.cs
  check "its handlers are tracked" tracked "$attachments_app/Commands/AddAttachment/AddAttachmentCommandHandler.cs"
  check "its controller is tracked" tracked backend/src/Kanabol.Api/Controllers/AttachmentsController.cs
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the domain's tests pass: an open task only, no history" attachment_domain_tests_pass
  check "the handlers' tests pass: the limit, the extensions, the name, who uploads, reads and deletes" attachment_handler_tests_pass
  check "LocalFileStorage's tests pass: the key, the bytes, the root" storage_tests_pass
  check "a file attached or taken off sends TaskUpdated with Attachments" attachment_hub_tests_pass
  check "the endpoints' tests pass" attachment_endpoint_tests_pass
  check "appsettings.json allows the spec's extensions" the_default_extensions_are_the_specs
  check "the real API takes a member's file, with where it is" a_member_attaches_a_file
  check "it keeps the bytes under {yyyy}/{MM}/{guid}{ext}, without the name" the_file_is_on_disk_under_a_key_of_its_own
  check "the task lists its files" the_task_lists_its_files
  check "another member downloads it as an attachment, the same bytes" another_member_downloads_it_as_an_attachment
  check "an outsider downloads nothing, 403 Project.MemberRequired" an_outsider_downloads_nothing
  check "25 MB go in, and one byte more is 400 on file" twenty_five_megabytes_go_in_and_one_byte_more_does_not
  check "a file of 28 MB is 400 on file too" a_file_far_over_the_limit_is_refused_on_file_too
  check "an extension outside the list is 400 on file" an_extension_outside_the_list_is_refused
  check "two files in one request are 400 on file" two_files_in_one_request_are_refused
  check "the list comes from Storage__AllowedExtensions" the_list_comes_from_the_settings
  check "another member deletes nothing, 403 Attachment.NotYours" someone_else_deletes_nothing
  check "its uploader deletes it, row and file" its_uploader_deletes_it_file_and_all
  check "an archived task takes no file, 409 Task.Archived" an_archived_task_takes_no_file
  check "backend/CLAUDE.md says how attachments work" in_file '`POST /api/tasks/{id}/attachments`' backend/CLAUDE.md
}

if [ "$#" -eq 0 ]; then
  set -- 1 2
fi
for task in "$@"; do
  "task_$task"
done

[ "$failures" -eq 0 ]
