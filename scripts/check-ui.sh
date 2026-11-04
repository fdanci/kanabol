#!/bin/sh
# Acceptance checks for the visual foundation (group 2a in the project's Tasks.md).
# Usage: scripts/check-ui.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Run npm ci in frontend/ first.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

VAULT=${KANABOL_VAULT:-"$HOME/Documents/Obsidian Vault/Efforts/Projects/Kanban pe steroizi"}
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

absent() {
  [ ! -e "$1" ] && [ -z "$(git ls-files -- "$1")" ]
}

in_file() { # in_file <text> <file>
  grep -qF -- "$1" "$2"
}

has_vault() {
  [ -d "$VAULT" ]
}

# The repo's copy is the vault's note, with Obsidian's embeds as plain Markdown images.
spec_matches_the_vault() {
  has_vault || return 0
  tr -d '\r' <"$VAULT/Atasamente/UI_UX_SPEC.md" |
    sed -e 's#^!\[\[kanabol-palette\.png\]\]$#![Paleta Kanabol](images/kanabol-palette.png)#' \
      -e 's#^!\[\[logo/brand-preview\.png\]\]$#![Logo Kanabol](images/brand-preview.png)#' >"$scratch/expected-spec.md"
  tr -d '\r' <docs/UI_UX_SPEC.md | cmp - "$scratch/expected-spec.md"
}

image_matches_the_vault() { # image_matches_the_vault <file in the vault's Atasamente> <file in the repo>
  has_vault || return 0
  cmp "$VAULT/Atasamente/$1" "$2"
}

spec_has_no_obsidian_embeds() {
  ! grep -qF '![[' docs/UI_UX_SPEC.md
}

vault_note_mentions() { # vault_note_mentions <text> <note>
  has_vault || return 0
  grep -qF -- "$1" "$VAULT/$2"
}

task_1() {
  echo "2a.1 The UI spec is in the repo"
  check "docs/UI_UX_SPEC.md is tracked" tracked docs/UI_UX_SPEC.md
  check "the palette image is tracked" tracked docs/images/kanabol-palette.png
  check "the brand preview image is tracked" tracked docs/images/brand-preview.png
  check "it's the vault's note" spec_matches_the_vault
  check "it has no Obsidian embeds left" spec_has_no_obsidian_embeds
  check "the palette image is the vault's" image_matches_the_vault kanabol-palette.png docs/images/kanabol-palette.png
  check "the brand preview image is the vault's" image_matches_the_vault logo/brand-preview.png docs/images/brand-preview.png
  check "CLAUDE.md points to it" in_file 'docs/UI_UX_SPEC.md' CLAUDE.md
  check "CLAUDE.md says it wins on visuals" in_file 'wins over `docs/SPEC.md` on anything visual' CLAUDE.md
  check "frontend/CLAUDE.md points to it" in_file 'docs/UI_UX_SPEC.md' frontend/CLAUDE.md
  check "frontend/CLAUDE.md has the tokens rule" in_file 'var(--kb-*)' frontend/CLAUDE.md
  check "frontend/CLAUDE.md has the ng-deep rule" in_file '::ng-deep' frontend/CLAUDE.md
  check "frontend/CLAUDE.md has the no-CDN rule" in_file 'self-hosted' frontend/CLAUDE.md
  check "the frontend notes pass Prettier" sh -c 'cd frontend && npx prettier --check CLAUDE.md'
  check "Tasks.md points to it" vault_note_mentions 'docs/UI_UX_SPEC.md' Tasks.md
  check "_Architecture.md links to it" vault_note_mentions 'Atasamente/UI_UX_SPEC' _Architecture.md
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
