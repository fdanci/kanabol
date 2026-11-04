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

has_decision_sections() {
  [ "$(grep '^## ' "$1" | tr -d '\r' | tr '\n' ' ')" = "## Decizie ## Motiv ## Alternative ## Legate " ]
}

# Every [[Kanban pe steroizi/...]] wikilink in the file points at a note that exists.
links_resolve() {
  grep -o '\[\[Kanban pe steroizi/[^]|]*' "$1" | sed 's/^\[\[Kanban pe steroizi\///' >"$TMPDIR_CHECK/links"
  [ -s "$TMPDIR_CHECK/links" ] || return 1
  while IFS= read -r target; do
    [ -f "$VAULT/$target.md" ] || return 1
  done <"$TMPDIR_CHECK/links"
}

links_note() { # links_note <note name>
  grep -qF "[[Kanban pe steroizi/Decisions/$1|$1]]" "$VAULT/_Architecture.md"
}

record_count_is() {
  [ "$(find docs/adr -name '*.md' | wc -l)" -eq "$1" ]
}

task_3() {
  echo "0.3 the 14 decision notes from SPEC section 17"
  TMPDIR_CHECK=$(mktemp -d)
  check "docs/adr holds 16 records" record_count_is 16
  while IFS='|' read -r number slug name; do
    record="docs/adr/$number-$slug.md"
    check "$number is tracked" tracked "$record"
    check "$number has the four sections" has_decision_sections "$record"
    if [ -d "$VAULT" ]; then
      check "$number note: $name" has_decision_sections "$VAULT/Decisions/$name.md"
      check "$number note's links resolve" links_resolve "$VAULT/Decisions/$name.md"
      check "$number matches its vault note" same_as_note "$record" "$name"
      check "$number is linked from _Architecture.md" links_note "$name"
    fi
  done <<'EOF'
0003|clean-architecture-si-cqrs-light|Clean Architecture și CQRS light
0004|utilizatori-proprii-si-jwt-cu-refresh-rotit|Utilizatori proprii și JWT cu refresh rotit
0005|angular-signals-si-material-fara-ngrx|Angular Signals și Material, fără NgRx
0006|serilog-si-seq-pentru-loguri|Serilog și Seq pentru loguri
0007|postgres-real-in-teste|Postgres real în teste
0008|husky-si-lint-staged-la-commit|Husky și lint-staged la commit
0009|signalr-din-v1-prin-domain-events|SignalR din v1 prin domain events
0010|atasamente-pe-disc-in-spatele-ifilestorage|Atașamente pe disc în spatele IFileStorage
0011|fara-mediatr-automapper-moq-si-karma|Fără MediatR, AutoMapper, Moq și Karma
0012|un-singur-rol-per-utilizator|Un singur rol per utilizator
0013|arhivare-in-loc-de-stergere|Arhivare în loc de ștergere
0014|alerte-doar-in-app|Alerte doar in-app
0015|tls-cu-ca-proprie|TLS cu CA proprie
0016|productivitate-fara-story-points|Productivitate fără story points
EOF
  if has_vault; then
    check "_Architecture.md's links resolve" links_resolve "$VAULT/_Architecture.md"
  fi
  rm -rf "$TMPDIR_CHECK"
}

[ $# -gt 0 ] || set -- 1 2 3
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
