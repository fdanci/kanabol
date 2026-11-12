#!/bin/sh
# Acceptance checks for the repo foundation (group 0 in the project's Tasks.md).
# Usage: scripts/check-foundation.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2
# In the full gate, the build, tests and lint scripts/gate-prepare.sh did once (docs/SPEC.md §14.7).
[ -n "${KANABOL_GATE_CACHE:-}" ] && . scripts/gate-reuse.sh

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
  echo "0.3 the decision notes from SPEC section 17, and the ones added later"
  TMPDIR_CHECK=$(mktemp -d)
  check "docs/adr holds 19 records" record_count_is 19
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
0017|fluentassertions-7-2-2|FluentAssertions 7.2.2
0018|coloane-fixe-pe-board|Coloane fixe pe board
0019|timp-lucrat-in-programul-de-lucru|Timp lucrat în programul de lucru
EOF
  if has_vault; then
    check "_Architecture.md's links resolve" links_resolve "$VAULT/_Architecture.md"
  fi
  rm -rf "$TMPDIR_CHECK"
}

# No line of the file as it was before this task has been lost.
keeps_original_claude_md() {
  git show 13ec739:CLAUDE.md | tr -d '\r' | while IFS= read -r line; do
    [ -z "$line" ] || grep -qxF -- "$line" CLAUDE.md || exit 1
  done
}

task_4() {
  echo "0.4 root CLAUDE.md"
  check "keeps every original line (vault, git rules)" keeps_original_claude_md
  check "still forbids AI attribution" grep -q 'Never mention Claude' CLAUDE.md
  check "points to docs/SPEC.md" grep -q 'docs/SPEC.md' CLAUDE.md
  check "has a naming section" grep -q '^## Naming' CLAUDE.md
  for convention in _camelCase 'Command`' 'Handler' 'Validator' 'Dto' 'Request' 'Event' 'Errors' \
    'Configuration' '_<Scenario>_' snake_case 'ix_' 'ux_' timestamptz '/api' 'kebab-case' \
    'kb-task-card' 'board-store.ts' 'tasks-api.ts' 'auth-guard.ts' \
    'Conventional Commits' 'feature/<kebab>' '`infra`'; do
    check "naming covers $convention" grep -qF -- "$convention" CLAUDE.md
  done
}

has_setting() {
  grep -qxF -- "$1" .editorconfig
}

# The official format has no inline comments: a trailing "# ..." would become part of the value.
has_no_inline_comments() {
  ! grep -qE '^[^#]*=.*[#;]' .editorconfig
}

# Prove the rules reach the backend, on a throwaway copy of it. Clean code must pass; a file
# breaking the rules must be rejected by both dotnet format and the build.
rejects_rule_breakers() {
  [ -f .editorconfig ] || return 1
  work=$(mktemp -d)
  cp .editorconfig "$work/"
  git archive "$(git write-tree)" backend | tar -x -C "$work"
  (
    cd "$work/backend" || exit 1
    dotnet restore Kanabol.sln --nologo -v q || exit 1
    dotnet format style Kanabol.sln --no-restore --verify-no-changes || exit 1
    cat >src/Kanabol.Api/RuleBreaker.cs <<'CS'
using Microsoft.Extensions.Logging;

namespace Kanabol.Api
{
    internal sealed class RuleBreaker(ILogger logger)
    {
        private readonly ILogger badlyNamedField = logger;

        public void Log(int value) => badlyNamedField.LogInformation($"value {value}");
    }
}
CS
    style=$(dotnet format style Kanabol.sln --no-restore --verify-no-changes 2>&1)
    build=$(dotnet build Kanabol.sln --no-restore --nologo 2>&1)
    printf '%s\n' "$style" | grep -q IDE0161 || exit 1 # file-scoped namespaces
    printf '%s\n' "$style" | grep -q IDE1006 || exit 1 # _camelCase private fields
    printf '%s\n' "$build" | grep -q 'error CA2254' || exit 1 # log templates
  )
  status=$?
  rm -rf "$work"
  return "$status"
}

task_5() {
  echo "0.5 single .editorconfig in the root"
  check ".editorconfig is tracked in the root" tracked .editorconfig
  check "frontend/.editorconfig is gone" absent frontend/.editorconfig
  check "values carry no inline comments" has_no_inline_comments
  while IFS= read -r setting; do
    check "has: $setting" has_setting "$setting"
  done <<'EOF'
root = true
[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 2
[*.cs]
indent_size = 4
csharp_style_namespace_declarations = file_scoped:error
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_prefer_braces = true:error
csharp_using_directive_placement = outside_namespace:error
dotnet_sort_system_directives_first = true
dotnet_style_qualification_for_field = false:error
dotnet_naming_rule.private_fields_underscore.severity = error
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_style.underscore_camel.capitalization = camel_case
dotnet_diagnostic.CA2254.severity = error
dotnet_diagnostic.CA1848.severity = none
dotnet_diagnostic.CA1062.severity = none
[*.{ts,html,scss,json,yaml,yml,md}]
[*.ts]
quote_type = single
[*.md]
trim_trailing_whitespace = false
EOF
  check "rules reach the backend and reject rule breakers" rejects_rule_breakers
}

is_ignored() {
  git check-ignore -q --no-index -- "$1"
}

is_not_ignored() {
  ! is_ignored "$1"
}

checks_out_as_lf() {
  [ "$(git check-attr eol -- "$1" | sed 's/.*: //')" = lf ]
}

# Worktree files must be LF even with core.autocrlf=true, or .editorconfig rejects them.
worktree_has_no_crlf() {
  [ -z "$(git ls-files --eol | grep -E 'w/(crlf|mixed)')" ]
}

stays_binary() {
  git ls-files --eol -- "$1" | grep -q 'i/-text'
}

backend_format_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
}

task_6() {
  echo "0.6 .gitignore and .gitattributes"
  check "certs/ is ignored" is_ignored certs/ca.key
  check ".gitattributes is tracked" tracked .gitattributes
  check "scripts/ checks out as LF" checks_out_as_lf scripts/any.sh
  check ".husky/ checks out as LF" checks_out_as_lf .husky/pre-commit
  check "source files check out as LF" checks_out_as_lf backend/src/Kanabol.Api/Program.cs
  check "no tracked file is CRLF in the worktree" worktree_has_no_crlf
  check "the favicon stays binary" stays_binary frontend/public/brand/favicon-32.png
  check "dotnet format accepts the backend" backend_format_is_clean
}

has_empty_variable() { # has_empty_variable <name>
  grep -qx "$1=" .env.example
}

# Every assignment must be empty: a filled-in value would be a secret, or a default that
# someone forgets to change.
has_no_values() {
  ! grep -E '^[A-Za-z_][A-Za-z0-9_]*=.+' .env.example
}

# A copy of the example, once filled in, is what compose reads: it must stay out of git.
real_env_is_ignored() {
  is_ignored .env
}

task_7() {
  echo "0.7 .env.example"
  check ".env.example is tracked" tracked .env.example
  check ".env.example isn't ignored" is_not_ignored .env.example
  check ".env is ignored" real_env_is_ignored
  for variable in POSTGRES_PASSWORD JWT_SECRET SEQ_ADMIN_HASH ADMIN_INITIAL_PASSWORD; do
    check "declares $variable" has_empty_variable "$variable"
  done
  check "holds no values" has_no_values
}

[ $# -gt 0 ] || set -- 1 2 3 4 5 6 7
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
