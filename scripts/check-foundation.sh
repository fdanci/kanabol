#!/bin/sh
# Acceptance checks for the repo foundation (group 0 in the project's Tasks.md).
# Usage: scripts/check-foundation.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0

check() { # check <description> <command...>
  description=$1
  shift
  if "$@" >/dev/null 2>&1; then
    printf '  ok    %s\n' "$description"
  else
    printf '  FAIL  %s\n' "$description"
    failures=$((failures + 1))
  fi
}

# True when a clean clone would contain this folder: git only tracks files.
is_tracked_folder() {
  [ -d "$1" ] && [ -n "$(git ls-files -- "$1")" ]
}

task_1() {
  echo "0.1 missing folders"
  for folder in scripts docs docs/adr .husky; do
    check "$folder/ exists and is tracked" is_tracked_folder "$folder"
  done
}

[ $# -gt 0 ] || set -- 1
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
