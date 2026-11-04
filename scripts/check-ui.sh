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

brand_file_is_the_vaults() { # brand_file_is_the_vaults <name>
  has_vault || return 0
  cmp "$VAULT/Atasamente/logo/$1" "frontend/public/brand/$1"
}

png_is() { # png_is <file> <width> <height>: reads the size from the PNG header
  node -e "
    const header = require('fs').readFileSync(process.argv[1]);
    const signature = header.subarray(0, 8).toString('latin1');
    if (signature !== '\x89PNG\r\n\x1a\n') throw new Error('not a PNG');
    const size = [header.readUInt32BE(16), header.readUInt32BE(20)].join('x');
    if (size !== process.argv[2] + 'x' + process.argv[3]) throw new Error('size is ' + size);
  " "$1" "$2" "$3"
}

svg_is_valid() { # svg_is_valid <file>
  grep -q '<svg' "$1" && grep -q '</svg>' "$1"
}

manifest_is_right() {
  node -e "
    const manifest = JSON.parse(require('fs').readFileSync('frontend/public/manifest.webmanifest', 'utf8'));
    const expect = (ok, what) => { if (!ok) throw new Error(what); };
    expect(manifest.name === 'Kanabol', 'the name');
    expect(manifest.lang === 'ro', 'the language');
    expect(manifest.start_url === '/', 'the start url');
    expect(manifest.background_color === '#f5f8ff', 'the background: surface-50');
    expect(manifest.theme_color === '#122d94', 'the theme color: brand-800');
    const sizes = Object.fromEntries(manifest.icons.map((icon) => [icon.sizes, icon.src]));
    expect(sizes['192x192'] === '/brand/kanabol-app-icon-192.png', 'the 192 icon');
    expect(sizes['512x512'] === '/brand/kanabol-app-icon-512.png', 'the 512 icon');
  "
}

build_frontend() {
  (cd frontend && npx ng build) >"$scratch/build.log" 2>&1 || {
    tail -n 20 "$scratch/build.log"
    return 1
  }
}

in_dist() { # in_dist <path>
  [ -f "frontend/dist/kanabol/browser/$1" ]
}

task_2() {
  echo "2a.2 Logo and identity"
  for name in apple-touch-icon.png favicon-16.png favicon-32.png kanabol-app-icon-192.png kanabol-app-icon-512.png \
    kanabol-favicon.svg kanabol-mark-mono.svg kanabol-mark-on-dark.svg kanabol-mark.svg; do
    check "$name is tracked" tracked "frontend/public/brand/$name"
    check "$name is the vault's" brand_file_is_the_vaults "$name"
  done
  check "favicon-16 is 16 by 16" png_is frontend/public/brand/favicon-16.png 16 16
  check "favicon-32 is 32 by 32" png_is frontend/public/brand/favicon-32.png 32 32
  check "the 192 icon is 192 by 192" png_is frontend/public/brand/kanabol-app-icon-192.png 192 192
  check "the 512 icon is 512 by 512" png_is frontend/public/brand/kanabol-app-icon-512.png 512 512
  check "the apple touch icon is 180 by 180" png_is frontend/public/brand/apple-touch-icon.png 180 180
  check "the SVG favicon is an SVG" svg_is_valid frontend/public/brand/kanabol-favicon.svg
  check "Angular's favicon is gone" absent frontend/public/favicon.ico
  html=frontend/src/index.html
  check "the page is in Romanian" in_file '<html lang="ro">' "$html"
  check "the title is Kanabol" in_file '<title>Kanabol</title>' "$html"
  check "the SVG favicon is linked" in_file '<link rel="icon" type="image/svg+xml" href="/brand/kanabol-favicon.svg" />' "$html"
  check "the 32 fallback is linked" in_file '<link rel="icon" type="image/png" sizes="32x32" href="/brand/favicon-32.png" />' "$html"
  check "the 16 fallback is linked" in_file '<link rel="icon" type="image/png" sizes="16x16" href="/brand/favicon-16.png" />' "$html"
  check "the apple touch icon is linked" in_file '<link rel="apple-touch-icon" href="/brand/apple-touch-icon.png" />' "$html"
  check "the manifest is linked" in_file '<link rel="manifest" href="/manifest.webmanifest" />' "$html"
  check "the manifest is tracked" tracked frontend/public/manifest.webmanifest
  check "the manifest is right" manifest_is_right
  check "the frontend builds" build_frontend
  check "the build has the favicon" in_dist brand/kanabol-favicon.svg
  check "the build has the manifest" in_dist manifest.webmanifest
  check "the build has the app icon" in_dist brand/kanabol-app-icon-512.png
  check "the built page is in Romanian" in_file '<html lang="ro"' frontend/dist/kanabol/browser/index.html
}

dependency_exists() { # dependency_exists <package>
  node -e "
    const { dependencies } = JSON.parse(require('fs').readFileSync('frontend/package.json', 'utf8'));
    if (!dependencies[process.argv[1]]) throw new Error(process.argv[1] + ' is not a dependency');
  " "$1"
}

same_major_as_angular() { # same_major_as_angular <package>
  node -e "
    const version = (name) => JSON.parse(require('fs').readFileSync('frontend/node_modules/' + name + '/package.json', 'utf8')).version;
    const major = (value) => value.split('.')[0];
    const actual = version(process.argv[1]);
    const angular = version('@angular/core');
    if (major(actual) !== major(angular)) throw new Error(process.argv[1] + ' ' + actual + ' with Angular ' + angular);
  " "$1"
}

dependencies_are_consistent() {
  (cd frontend && npm ls --all >/dev/null 2>"$scratch/npm-ls.log") || {
    grep -E 'invalid|missing|ERR' "$scratch/npm-ls.log" | head -n 10
    return 1
  }
}

no_google_fonts_added() {
  ! grep -rnE 'fonts\.(googleapis|gstatic)\.com' frontend/src frontend/angular.json
}


task_3() {
  echo "2a.3 Material, fonts and the generated palette"
  for package in @angular/material @angular/cdk @fontsource-variable/inter material-symbols; do
    check "$package is a dependency" dependency_exists "$package"
  done
  check "Material has Angular's major version" same_major_as_angular @angular/material
  check "the CDK has Angular's major version" same_major_as_angular @angular/cdk
  check "the dependency tree is consistent" dependencies_are_consistent
  theme=frontend/src/styles/theme/_theme-colors.scss
  check "the generated palette is tracked" tracked "$theme"
  check "it has the primary palette" in_file '$primary-palette' "$theme"
  check "it has the tertiary palette" in_file '$tertiary-palette' "$theme"
  check "Prettier leaves the generated file alone" in_file 'src/styles/theme/' frontend/.prettierignore
  check "nothing comes from Google Fonts" no_google_fonts_added
  check "the frontend still builds" build_frontend
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3
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
