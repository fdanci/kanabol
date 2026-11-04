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

tracked() {
  [ -f "$1" ] && [ -n "$(git ls-files -- "$1")" ]
}

absent() {
  [ ! -e "$1" ]
}

not_in_file() { # not_in_file <text> <file>
  ! grep -qF -- "$1" "$2"
}

# The Obsidian vault only exists on the author's machine; its checks are skipped elsewhere.
VAULT=${KANABOL_VAULT:-"$HOME/Documents/Obsidian Vault/Efforts/Projects/Kanban pe steroizi"}

has_vault() {
  if [ -d "$VAULT" ]; then
    return 0
  fi
  echo "  skip  vault checks: $VAULT not found"
  return 1
}

# A decision record is its vault note, line endings aside, under a numbered title.
same_as_note() { # same_as_note <record> <note name>
  [ "$(head -n 1 "$1" | sed 's/^# [0-9]*\. //')" = "$2" ] &&
    [ "$(tail -n +3 "$1" | tr -d '\r')" = "$(tr -d '\r' <"$VAULT/Decisions/$2.md")" ]
}

task_1() {
  echo "0.1 missing folders"
  for folder in scripts docs docs/adr .husky; do
    check "$folder/ exists and is tracked" is_tracked_folder "$folder"
  done
}

task_2() {
  echo "0.2 spec and decision records in the repo"
  check "docs/SPEC.md is tracked" tracked docs/SPEC.md
  check "docs/SPEC.md is the Kanabol spec" grep -q '^# SPEC — Kanabol' docs/SPEC.md
  check "docs/adr/0001 is the RBAC decision" tracked docs/adr/0001-rbac-pe-doua-niveluri.md
  check "docs/adr/0002 is the single repo decision" tracked docs/adr/0002-un-singur-repo.md
  check "docs/adr has no leftover .gitkeep" absent docs/adr/.gitkeep

  if has_vault; then
    check "the spec no longer sits on the Desktop" absent "$HOME/Desktop/SPEC.md"
    check "Tasks.md links to docs/SPEC.md" grep -q 'kanabol/docs/SPEC.md' "$VAULT/Tasks.md"
    check "Tasks.md no longer links to the Desktop" not_in_file 'Desktop/SPEC.md' "$VAULT/Tasks.md"
    check "0001 matches its vault note" same_as_note docs/adr/0001-rbac-pe-doua-niveluri.md "RBAC pe două niveluri"
    check "0002 matches its vault note" same_as_note docs/adr/0002-un-singur-repo.md "Un singur repo"
  fi
}

[ $# -gt 0 ] || set -- 1 2
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
