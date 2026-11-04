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

built_css() { # prints the path of the built global stylesheet
  ls frontend/dist/kanabol/browser/styles-*.css 2>/dev/null | head -n 1
}

css_has() { # css_has <text>: compares without whitespace, which the minifier keeps in places
  stylesheet=$(built_css)
  [ -n "$stylesheet" ] && tr -d ' \n' <"$stylesheet" | grep -qF -- "$1" || {
    echo "not in the built CSS: $1"
    return 1
  }
}

# The tokens file has to say what the spec's section 2.4 says, for light and for dark.
tokens_match_the_spec() {
  node -e "
    const fs = require('fs');
    const spec = fs.readFileSync('docs/UI_UX_SPEC.md', 'utf8').replace(/\r/g, '');
    const start = spec.indexOf('\`src/styles/_tokens.scss\`:');
    const open = spec.indexOf('\`\`\`scss', start);
    const close = spec.indexOf('\`\`\`', open + 7);
    const fromSpec = spec.slice(open + 7, close);
    const fromFile = fs.readFileSync('frontend/src/styles/_tokens.scss', 'utf8').replace(/\r/g, '');
    const normalize = (value) => value.replace(/\s+/g, '').replace(/([(,\s:])\./g, '\$10.').replace(/(\.\d*?[1-9])0+(?!\d)/g, '\$1').replace(/\"/g, \"'\").toLowerCase();
    const parts = (source) => {
      const split = source.indexOf('@mixin dark');
      return [source.slice(0, split), source.slice(split)];
    };
    const tokens = (source) => {
      const found = {};
      for (const match of source.matchAll(/(--kb-[a-z0-9-]+)\s*:\s*([^;]+);/g)) found[match[1]] = normalize(match[2]);
      return found;
    };
    const [specLight, specDark] = parts(fromSpec).map(tokens);
    const [fileLight, fileDark] = parts(fromFile).map(tokens);
    let bad = 0;
    for (const [name, specTokens, fileTokens] of [['light', specLight, fileLight], ['dark', specDark, fileDark]]) {
      if (Object.keys(specTokens).length < 10) { console.log('too few tokens read from the spec: ' + name); bad++; }
      for (const key of new Set([...Object.keys(specTokens), ...Object.keys(fileTokens)])) {
        if (specTokens[key] !== fileTokens[key]) { console.log(name + ' ' + key + ': spec ' + specTokens[key] + ', file ' + fileTokens[key]); bad++; }
      }
    }
    process.exit(bad ? 1 : 0);
  "
}

# WCAG AA for normal text, computed from the tokens themselves, for the pairs the UI uses.
# Three pairs from the spec fall short. They stay as the spec has them until it changes, and
# this fails as soon as one of them passes, so the list can't outlive the problem.
contrast_is_aa() {
  node -e "
    const fs = require('fs');
    const source = fs.readFileSync('frontend/src/styles/_tokens.scss', 'utf8');
    const split = source.indexOf('@mixin dark');
    const tokens = (part) => Object.fromEntries([...part.matchAll(/(--kb-[a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{6})\s*;/g)].map((m) => [m[1].slice(5), m[2]]));
    const light = tokens(source.slice(0, split));
    const dark = { ...light, ...tokens(source.slice(split)) };
    const luminance = (hex) => {
      const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
        .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    const ratio = (a, b) => {
      const [high, low] = [luminance(a), luminance(b)].sort((x, y) => y - x);
      return (high + 0.05) / (low + 0.05);
    };
    const white = '#ffffff';
    const pairs = [
      ['light', 'navy on white', light['navy-900'], white],
      ['light', 'accent on white', light['accent-600'], white],
      ['light', 'accent on tint', light['accent-600'], light['tint-100']],
      ['light', 'white on accent (default button)', white, light['accent-600']],
      ['light', 'white on danger (danger button)', white, light['danger-600']],
      ['light', 'body text on white', light['text-700'], white],
      ['light', 'secondary text on white', light['text-600'], white],
      ['light', 'info text on info', light['info-text'], light['info-bg']],
      ['light', 'success on white', light['success-600'], white],
      ['light', 'warning on white', light['warning-600'], white],
      ['light', 'danger on white', light['danger-600'], white],
      ['light', 'success on its background', light['success-600'], light['success-bg']],
      ['light', 'warning on its background', light['warning-600'], light['warning-bg']],
      ['light', 'danger on its background', light['danger-600'], light['danger-bg']],
      ['dark', 'titles on cards', dark['text-900'], dark['surface-0']],
      ['dark', 'body text on cards', dark['text-700'], dark['surface-0']],
      ['dark', 'secondary text on cards', dark['text-600'], dark['surface-0']],
      ['dark', 'accent on cards', dark['accent-600'], dark['surface-0']],
      ['dark', 'accent on tint', dark['accent-600'], dark['tint-100']],
      ['dark', 'white on accent (default button)', white, dark['accent-600']],
      ['dark', 'white on danger (danger button)', white, dark['danger-600']],
      ['dark', 'info text on info', dark['info-text'], dark['info-bg']],
      ['dark', 'success on its background', dark['success-600'], dark['success-bg']],
      ['dark', 'warning on its background', dark['warning-600'], dark['warning-bg']],
      ['dark', 'danger on its background', dark['danger-600'], dark['danger-bg']],
    ];
    const known = ['light success on its background', 'dark white on accent (default button)', 'dark white on danger (danger button)'];
    let bad = 0;
    for (const [mode, name, text, background] of pairs) {
      const value = ratio(text, background);
      const label = mode + ' ' + name;
      if (value >= 4.5 && known.includes(label)) { console.log(label + ' passes now, so take it off the list'); bad++; }
      if (value < 4.5 && !known.includes(label)) { console.log(label + ': ' + value.toFixed(2)); bad++; }
    }
    process.exit(bad ? 1 : 0);
  "
}

nothing_loads_from_a_cdn() {
  ! grep -rlE 'fonts\.(googleapis|gstatic)\.com|cdn\.jsdelivr|unpkg\.com' frontend/dist/kanabol/browser frontend/src
}

media_has() { # media_has <name pattern>
  ls frontend/dist/kanabol/browser/media/ 2>/dev/null | grep -qE "$1"
}

no_hex_in_components() {
  ! grep -rnE '#[0-9a-fA-F]{3,8}\b' frontend/src/app --include='*.scss' --include='*.html'
}

no_ng_deep() {
  ! grep -rn '::ng-deep' frontend/src --include='*.scss' --include='*.ts' --include='*.html'
}

frontend_tests_pass() {
  (cd frontend && npx ng test --no-watch) >"$scratch/test.log" 2>&1 || {
    tail -n 15 "$scratch/test.log"
    return 1
  }
}

frontend_lint_passes() {
  (cd frontend && npx ng lint) >"$scratch/lint.log" 2>&1 || {
    tail -n 15 "$scratch/lint.log"
    return 1
  }
}

task_4() {
  echo "2a.4 Tokens and the Material theme"
  check "_tokens.scss is tracked" tracked frontend/src/styles/_tokens.scss
  check "the tokens are the spec's, in light and dark" tokens_match_the_spec
  check "text on its background passes WCAG AA" contrast_is_aa
  check "the frontend builds" build_frontend
  check "the build has the light tokens" css_has '--kb-accent-600:#135bf7'
  check "the build has the dark tokens" css_has '--kb-accent-600:#5b93ff'
  check "dark follows the system" css_has 'prefers-color-scheme:dark'
  check "dark follows the manual toggle" css_has 'data-theme=dark'
  check "the manual dark toggle also switches Material" css_has 'data-theme=dark]{color-scheme:dark'
  check "the manual light toggle also switches Material" css_has 'data-theme=light]{color-scheme:light'
  check "the build has Material's system tokens" css_has '--mat-sys-primary'
  check "Material's primary is the accent" css_has '--mat-sys-primary:var(--kb-accent-600)'
  check "the default button is the accent, in white" css_has '--mat-button-filled-container-color:var(--kb-accent-600)'
  check "the page has the font" css_has "var(--kb-font)"
  check "Inter is self-hosted" media_has '^inter.*\.woff2$'
  check "Material Symbols are self-hosted" media_has '^material-symbols-outlined.*\.woff2$'
  check "nothing loads from a CDN" nothing_loads_from_a_cdn
  check "components have no hex colors" no_hex_in_components
  check "nothing uses ::ng-deep" no_ng_deep
  check "the frontend tests pass" frontend_tests_pass
  check "the frontend lint passes" frontend_lint_passes
}

task_5() {
  echo "2a.5 Typography and utility classes"
  check "_typography.scss is tracked" tracked frontend/src/styles/_typography.scss
  check "_utilities.scss is tracked" tracked frontend/src/styles/_utilities.scss
  check "the frontend builds" build_frontend

  check "kb-display is 24/32/600 in navy" css_has '.kb-display{font-size:24px;line-height:32px;font-weight:600;color:var(--kb-navy-900)'
  check "kb-title is 18/26/600 in navy" css_has '.kb-title{font-size:18px;line-height:26px;font-weight:600;color:var(--kb-navy-900)'
  check "kb-subtitle is 15/22/600 in body color" css_has '.kb-subtitle{font-size:15px;line-height:22px;font-weight:600;color:var(--kb-text-700)'
  check "kb-body is 14/21/400 in body color" css_has '.kb-body{font-size:14px;line-height:21px;font-weight:400;color:var(--kb-text-700)'
  check "kb-body-strong is 14/21/600" css_has '.kb-body-strong{font-size:14px;line-height:21px;font-weight:600'
  check "kb-caption is 12/16/400 in secondary color" css_has '.kb-caption{font-size:12px;line-height:16px;font-weight:400;color:var(--kb-text-600)'
  check "kb-mono is 13/20 in the mono font" css_has '.kb-mono{font-family:var(--kb-font-mono);font-size:13px;line-height:20px'

  check "kb-bezel exists, with its shadow and highlight" css_has 'box-shadow:var(--kb-shadow-bezel),var(--kb-inner-highlight)'
  check "kb-bezel uses the control radius" css_has 'border:1pxsolidvar(--kb-border-300);border-radius:var(--kb-radius-sm)'
  check "kb-default starts from the accent" css_has '--_base:var(--kb-accent-600)'
  check "kb-default has the subtle gradient" css_has 'linear-gradient(180deg,color-mix(insrgb,var(--_c)92%,white)0%,var(--_c)100%)'
  check "kb-danger starts from danger" css_has '--_base:var(--kb-danger-600)'
  check "hover darkens the button's own color" css_has '--_c:color-mix(insrgb,var(--_base)90%,var(--kb-navy-900))'
  check "press darkens the button's own color" css_has '--_c:color-mix(insrgb,var(--_base)80%,var(--kb-navy-900))'
  check "a danger button can't turn blue on hover" css_lacks '--_c:color-mix(insrgb,var(--kb-accent-600)'
  check "kb-default beats Material's button rules" css_has 'html:root.kb-default'
  check "kb-danger beats Material's button rules" css_has 'html:root.kb-danger'
  check "kb-bezel beats Material's button rules" css_has 'html:root.kb-bezel'
  check "a disabled button only fades" css_has 'opacity:.45;cursor:default'
  check "a disabled button doesn't react to hover" css_has '.kb-default:hover:not(:disabled)'
  check "the text button has the same height as the others" css_has '--mat-button-text-container-height:32px'
  check "kb-glass has the blur" css_has 'backdrop-filter:var(--kb-glass-blur)'
  check "kb-glass falls back to opaque" css_has '@supportsnot(backdrop-filter:blur(1px))'
  check "kb-floating is 12px with the popover shadow" css_has '.kb-floating{border-radius:var(--kb-radius-lg);box-shadow:var(--kb-shadow-popover)'
  check "kb-stack exists" css_has '.kb-stack{'
  check "kb-row exists" css_has '.kb-row{'
  check "kb-truncate ellipsizes" css_has '.kb-truncate{overflow:hidden;text-overflow:ellipsis'
  check "kb-visually-hidden exists" css_has '.kb-visually-hidden{'
  check "the frontend lint passes" frontend_lint_passes
}

css_lacks() { # css_lacks <text>
  stylesheet=$(built_css)
  [ -n "$stylesheet" ] && ! tr -d ' \n' <"$stylesheet" | grep -qF -- "$1"
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 5
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
