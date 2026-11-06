#!/bin/sh
# Acceptance checks for the kanban board (group 13 in the project's Tasks.md).
# The tests and specs hold the rules, so these checks run them, and add what they can't see: the real
# API on a real PostgreSQL (through check-tasks.sh), the routes, the texts and the production build.
#
# Usage: scripts/check-board.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Task 1 needs Docker running and, for the real API that
# check-tasks.sh 3 starts, a PostgreSQL whose user may create databases, with its connection string in
# KANABOL_CHECK_DB or else in the API's user secrets (ConnectionStrings:Default).

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
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

lacks() { # lacks <extended regex> <file...>
  ! grep -nE -- "$@" || return 1
}

docker_is_running() {
  docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
  "$docker_cli" info --format '{{.ServerVersion}}'
}

solution_builds() {
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
}

formatting_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
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

test_exists() { # test_exists <project> <name>: the test is there, so its filter can't run nothing
  dotnet test "backend/tests/$1/$1.csproj" --nologo --no-build --list-tests 2>/dev/null | tr -d '\r' | grep -qF "$2" || {
    echo "no test $2 in $1"
    return 1
  }
}

named_tests_pass() { # named_tests_pass <project> <test name...>: each exists, and all of them pass
  project=$1
  shift
  dotnet build "backend/tests/$project/$project.csproj" --nologo -v q >/dev/null 2>&1 || return 1
  filter=
  for name in "$@"; do
    test_exists "$project" "$name" || return 1
    filter="${filter:+$filter|}FullyQualifiedName~$name"
  done
  tests_pass "$project" "$filter" "$#"
}

# ---- 13.1: the task number from one UPDATE … RETURNING, in the transaction of the creation.

numbers=backend/src/Kanabol.Infrastructure/Persistence/TaskNumbers.cs
create_handler=backend/src/Kanabol.Application/Features/Tasks/Commands/CreateTask/CreateTaskCommandHandler.cs

# The UPDATE comes after the transaction begins, and the save and the commit after it.
the_counter_moves_in_the_transaction() {
  node -e "
    const s = require('fs').readFileSync('$numbers', 'utf8');
    const at = (text) => { const i = s.indexOf(text); if (i < 0) { console.log('missing: ' + text); process.exit(1); } return i; };
    const begin = at('BeginTransactionAsync(ct)');
    const update = at('UPDATE projects SET next_task_number = next_task_number + 1');
    at('WHERE id = {projectId} AND archived_at IS NULL');
    at('RETURNING next_task_number - 1');
    const save = s.indexOf('SaveChangesAsync(ct)', update);
    const commit = at('transaction.CommitAsync(ct)');
    if (!(begin < update && update < save && save < commit)) { console.log('not begin, UPDATE, save, commit, in that order'); process.exit(1); }
    if (s.indexOf('SaveChangesAsync(ct)', commit) < 0) { console.log('no save after the commit, which sends the held events'); process.exit(1); }
  "
}

no_retry_left() {
  lacks 'MaxRetries|ReloadAsync|DbUpdateException|ChangedMeanwhile|for \(' "$create_handler" &&
    lacks 'TakeTaskNumber|NextTaskNumber\+\+' backend/src/Kanabol.Domain/Projects/Project.cs
}

the_events_wait_for_the_commit() {
  in_file 'context?.Database.CurrentTransaction is not null' \
    backend/src/Kanabol.Infrastructure/Persistence/Interceptors/DomainEventsInterceptor.cs
}

creation_tests_pass() {
  named_tests_pass Kanabol.Application.Tests \
    CreateTaskCommandHandlerTests.Create_TwoAtOnce_GetDifferentNumbersAndNoConflict \
    CreateTaskCommandHandlerTests.Create_TenAtOnce_GetTenNumbersAndTenPlacesWithoutAConflict \
    CreateTaskCommandHandlerTests.Create_WhileAnotherOneHasTakenItsNumber_WaitsForItsCommitThenTakesTheNextOne \
    CreateTaskCommandHandlerTests.Create_WhileAMemberIsAdded_StillSucceeds \
    CreateTaskCommandHandlerTests.Create_WhenTheProjectIsArchivedMeanwhile_ReturnsArchived \
    CreateTaskCommandHandlerTests.Create_WhenTheProjectIsRenamedMeanwhile_StillSucceedsAndKeepsTheNewName \
    CreateTaskCommandHandlerTests.AddMember_WhileATaskIsCreated_Returns409ChangedMeanwhile
}

ten_posts_at_once_pass() {
  named_tests_pass Kanabol.Api.Tests TasksEndpointTests.Post_TenAtOnce_Return201WithTenDifferentKeys
}

check_tasks_3_passes() {
  sh scripts/check-tasks.sh 3 >"$scratch/check-tasks-3.log" 2>&1 || {
    grep -E 'FAIL|failed' -A 6 "$scratch/check-tasks-3.log" | head -n 30
    return 1
  }
}

task_1() {
  echo "13.1 The task number from one UPDATE … RETURNING, without a retry"
  check "Docker is running" docker_is_running
  check "the abstraction is tracked" tracked backend/src/Kanabol.Application/Abstractions/ITaskNumbers.cs
  check "its implementation is tracked" tracked "$numbers"
  check "the solution builds without warnings" solution_builds
  check "the formatting is clean" formatting_is_clean
  check "the counter moves in one UPDATE … RETURNING, inside the transaction that saves the task" the_counter_moves_in_the_transaction
  check "no retry is left, and the entity no longer moves the counter" no_retry_left
  check "the domain events of a save inside a transaction wait for its commit" the_events_wait_for_the_commit
  check "the domain tests of the creation pass" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TaskCreationTests' 10
  check "the creations at once, the waits and the side effect on the members pass" creation_tests_pass
  check "every creation test passes, and the notification after one" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~CreateTaskCommandHandlerTests|FullyQualifiedName~TaskAssignedNotificationTests' 36
  check "ten POSTs at once get ten 201s with ten keys" ten_posts_at_once_pass
  check "check-tasks.sh 3 passes: on the real API ten creations at once get ten numbers" check_tasks_3_passes
}

if [ "$#" -eq 0 ]; then
  set -- 1
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
