#!/bin/sh
# Acceptance checks for the commit pipeline (group 5 in the project's Tasks.md).
# Usage: scripts/check-pipeline.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Needs the packages scripts/setup.sh installs.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

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

json_is() { # json_is <file> <dotted.path> <expected>
  node -e '
    const [file, path, expected] = process.argv.slice(1);
    const value = path.split(">").reduce((at, key) => at?.[key], JSON.parse(require("fs").readFileSync(file, "utf8")));
    if (String(value) !== expected) { console.error(path + " is " + value + ", expected " + expected); process.exit(1); }
  ' "$@"
}

ignored() {
  git check-ignore -q "$1"
}

prettier_matches_the_frontend() {
  root=$(node -p "require('./package.json').devDependencies.prettier")
  frontend=$(node -p "require('./frontend/package.json').devDependencies.prettier")
  [ "$root" = "$frontend" ] || {
    echo "root has $root, frontend has $frontend"
    return 1
  }
}

message_accepted() {
  printf '%s\n' "$1" | npx --no -- commitlint
}

message_refused() {
  if output=$(printf '%s\n' "$1" | npx --no -- commitlint 2>&1); then
    echo "commitlint accepted: $1"
    return 1
  fi
  # Its own verdict, so a commitlint that fails to run doesn't count as a refusal.
  printf '%s\n' "$output" | grep -q 'found [1-9][0-9]* problems' || {
    printf '%s\n' "$output"
    return 1
  }
}

task_1() {
  echo "Task 1: root package.json and commitlint"
  check "package.json is tracked" tracked package.json
  check "its name marks it as the repo's, not the app's" json_is package.json name kanabol-repo
  check "it's private" json_is package.json private true
  check "prepare installs the hooks" json_is package.json 'scripts>prepare' husky
  check "it has husky 9" json_is package.json 'devDependencies>husky' '^9'
  check "it has lint-staged 15" json_is package.json 'devDependencies>lint-staged' '^15'
  check "it has commitlint 19" json_is package.json 'devDependencies>@commitlint/cli' '^19'
  check "it has the conventional rules 19" json_is package.json 'devDependencies>@commitlint/config-conventional' '^19'
  check "prettier is the frontend's version" prettier_matches_the_frontend
  check "the lock file is tracked" tracked package-lock.json
  check "node_modules is ignored" ignored node_modules/
  check "commitlint.config.js is tracked" tracked commitlint.config.js
  for scope in api web db auth board tasks infra ci docs; do
    check "scope $scope is accepted" message_accepted "feat($scope): add something"
  done
  check "the spec's example is accepted" message_accepted 'feat(board): drag and drop between columns'
  check "a commit without a scope is accepted" message_accepted 'docs: add the FluentAssertions decision record'
  check "a merge commit is accepted" message_accepted "Merge branch 'feature/board' into main"
  check "an unknown scope is refused" message_refused 'feat(banana): add something'
  check "an unknown type is refused" message_refused 'stuff(api): add something'
  check "a message without a type is refused" message_refused 'added some things'
  check "an empty subject is refused" message_refused 'feat(api):'
}

if [ "$#" -eq 0 ]; then
  set -- 1
fi

for task in "$@"; do
  "task_$task"
done

echo
if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
