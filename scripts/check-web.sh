#!/bin/sh
# Acceptance checks for the frontend skeleton and authentication (groups 9 and 9a in the project's
# Tasks.md).
# The specs hold the rules, so these checks run them, and add what they can't see: the configuration,
# the production build and what it ships.
#
# Usage: scripts/check-web.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Run scripts/setup.sh first, which installs the packages.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
web=frontend
app=$web/src/app

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

lacks() { # lacks <text> <file>
  ! grep -qF -- "$1" "$2" || {
    grep -nF -- "$1" "$2" | head -n 5
    return 1
  }
}

is() { # is <expected> <actual>
  [ "$1" = "$2" ] || {
    echo "expected '$1', got '$2'"
    return 1
  }
}

plain() { # Without the colors the tools print
  sed 's/\x1b\[[0-9;]*m//g' "$1"
}

specs_pass() { # specs_pass <spec file or glob, from frontend/> <minimum passed>
  log="$scratch/specs-$(printf '%s' "$1" | tr '/*' '__').log"
  (cd "$web" && npx ng test --no-watch --include "$1") >"$log" 2>&1 || {
    plain "$log" | tail -n 25
    return 1
  }
  passed=$(plain "$log" | sed -n 's/^ *Tests *\([0-9]*\) passed.*/\1/p' | tail -n 1)
  [ "${passed:-0}" -ge "$2" ] || {
    echo "only ${passed:-0} specs passed, expected at least $2"
    return 1
  }
}

# The production build, made once per run.
built() {
  [ -f "$scratch/built" ] && return 0
  (cd "$web" && npx ng build --output-path "$scratch/dist") >"$scratch/build.log" 2>&1 || {
    plain "$scratch/build.log" | tail -n 25
    return 1
  }
  cat "$scratch"/dist/browser/*.js >"$scratch/bundle.js" && touch "$scratch/built"
}

bundle_lacks() { # bundle_lacks <text>
  built && lacks "$1" "$scratch/bundle.js"
}

bundle_has() { # bundle_has <text>
  built && in_file "$1" "$scratch/bundle.js"
}

css_has() { # css_has <text>: compares without whitespace, which the minifier keeps in places
  built || return 1
  tr -d ' 
' <"$(ls "$scratch"/dist/browser/styles-*.css | head -n 1)" | grep -qF -- "$1" || {
    echo "not in the built CSS: $1"
    return 1
  }
}

# ---- 9.1

only_one_environment() {
  is "$web/src/environments/environment.ts" "$(git ls-files "$web/src/environments/*.ts" | grep -v '\.spec\.ts$')"
}

no_file_replacements() {
  lacks fileReplacements "$web/angular.json"
}

task_1() {
  echo "9.1 One environment for every build"
  check "environment.ts is tracked" tracked "$web/src/environments/environment.ts"
  check "it's the only one" only_one_environment
  check "no build swaps it for another" no_file_replacements
  check "its spec passes" specs_pass src/environments/environment.spec.ts 1
  check "the production build names no host" bundle_lacks localhost
}

# ---- 9.2

console_only_in_logger() {
  ! git grep -n "console\." -- "$web/src/**/*.ts" ":!*.spec.ts" ":!$app/core/logging/logger.ts" ":!$web/src/main.ts"
}

task_2() {
  echo "9.2 The API client, the interceptors and the logging"
  for file in core/api/api-client.ts core/api/correlation-interceptor.ts core/api/error-interceptor.ts     core/logging/logger.ts core/logging/global-error-handler.ts shared/ui/snackbar.ts shared/models/problem-details.ts; do
    check "$file is tracked" tracked "$app/$file"
  done
  check "the API client and interceptor specs pass" specs_pass "src/app/core/api/**/*.spec.ts" 15
  check "the logging specs pass" specs_pass "src/app/core/logging/**/*.spec.ts" 4
  check "the snackbar specs pass" specs_pass src/app/shared/ui/snackbar.spec.ts 4
  check "the app wires them in" specs_pass src/app/app.config.spec.ts 3
  check "the build sends the correlation header" bundle_has X-Correlation-Id
  check "only the Logger writes to the console" console_only_in_logger
}

# ---- 9.3

token_stays_out_of_storage() {
  ! git grep -n -e localStorage -e sessionStorage -e indexedDB -e document.cookie -- "$app/core/auth" ":!*.spec.ts"
}

task_3() {
  echo "9.3 The session, its refresh and the guards"
  for file in auth-store.ts auth-api.ts auth-interceptor.ts auth-guard.ts permission-guard.ts permissions.ts; do
    check "core/auth/$file is tracked" tracked "$app/core/auth/$file"
  done
  check "the store's specs pass, with two tabs taking turns" specs_pass src/app/core/auth/auth-store.spec.ts 15
  check "the interceptor's specs pass" specs_pass src/app/core/auth/auth-interceptor.spec.ts 9
  check "the guards' specs pass" specs_pass src/app/core/auth/guards.spec.ts 7
  check "the app restores the session before the first page" specs_pass src/app/app.config.spec.ts 5
  check "the access token goes in no storage" token_stays_out_of_storage
  check "the build takes turns between tabs through navigator.locks" bundle_has kb.auth.refresh
}

# ---- 9.9

task_9() {
  echo "9.9 *kbHasPermission"
  check "the directive is tracked" tracked "$app/shared/directives/has-permission.ts"
  check "its specs pass" specs_pass src/app/shared/directives/has-permission.spec.ts 3
}

# ---- 9.5

no_material_internals() {
  ! grep -nE '.(mat-mdc|mdc)-' "$web/src/styles.scss" "$web"/src/styles/_*.scss
}

task_5() {
  echo "9.5 The shape of Material's controls"
  check "the controls behave as the UI spec has them" specs_pass src/app/app.config.spec.ts 6
  check "the switch is 26px wide" css_has '--mat-slide-toggle-track-width:26px'
  check "and 16px high" css_has '--mat-slide-toggle-track-height:16px'
  check "with a 12px knob" css_has '--mat-slide-toggle-selected-handle-size:12px'
  check "a ticked checkbox is on the accent fill" css_has '--mat-checkbox-selected-icon-color:var(--kb-fill-accent)'
  check "with a white check" css_has '--mat-checkbox-selected-checkmark-color:#fff'
  check "menus are translucent popovers" css_has '--mat-menu-container-color:var(--kb-glass-bg)'
  check "with the popover shadow" css_has '--mat-menu-container-elevation-shadow:var(--kb-shadow-popover)'
  check "and the blur" css_has '.kb-menu{-webkit-backdrop-filter:var(--kb-glass-blur);backdrop-filter:var(--kb-glass-blur)'
  check "tooltips are on ink, which stays dark in dark" css_has '--mat-tooltip-container-color:color-mix(insrgb,var(--kb-ink)90%,transparent)'
  check "sheets have the sheet shadow" css_has '--mat-dialog-container-elevation-shadow:var(--kb-shadow-sheet)'
  check "and the navy veil" css_has '.kb-sheet-backdrop{background:#05206347'
  check "kb-regular brings a button to 28px" css_has '--mat-button-filled-container-height:28px'
  check "kb-sm brings it to 24px" css_has '--mat-button-filled-container-height:24px'
  check "a field is 28px, sunken" css_has '.kb-field{box-sizing:border-box;width:100%;height:28px'
  check "with its label to the left in a 160px column" css_has 'grid-template-columns:160pxminmax(0,1fr)'
  check "a menu item lights up in tint-100, where its text stays readable" css_has '--mat-menu-item-hover-state-layer-color:var(--kb-tint-100)'
  check "an icon button is 32px" css_has '--mat-icon-button-state-layer-size:32px'
  check "with a 40px target" css_has '--mat-icon-button-touch-target-size:40px'
  check "mat-icon draws with the self-hosted Material Symbols" css_has '.kb-symbols{font-family:MaterialSymbolsOutlined'
  check "Material is styled through its tokens only" no_material_internals
}

# ---- 9.6

no_hex_in_components() {
  ! git grep -nE '#[0-9a-fA-F]{3,8}' -- "$app/**/*.ts" "$app/**/*.html" "$app/**/*.scss" ":!*.spec.ts"
}

task_6() {
  echo "9.6 The shared components and the snackbar"
  for file in avatar banner chip confirm-dialog empty-state skeleton upload-zone error-snack snackbar; do
    check "shared/ui/$file.ts is tracked" tracked "$app/shared/ui/$file.ts"
  done
  check "their specs pass" specs_pass "src/app/shared/ui/*.spec.ts" 28
  check "the snackbar is on ink, which stays dark in dark" css_has '--mat-snack-bar-container-color:color-mix(insrgb,var(--kb-ink)92%,transparent)'
  check "with the glass and the popover shadow" css_has '.kb-snack{border-radius:var(--kb-radius-md);box-shadow:var(--kb-shadow-popover)'
  check "no component has a color of its own" no_hex_in_components
}

# ---- 9.4

no_search_in_the_toolbar() {
  lacks 'type="search"' "$app/core/layout/navbar.ts" && lacks matInput "$app/core/layout/navbar.ts"
}

lockup_is_inline() {
  in_file '<kb-lockup' "$app/core/layout/navbar.ts" && in_file '<svg' "$app/shared/ui/lockup.ts" && lacks '<img' "$app/shared/ui/lockup.ts"
}

task_4() {
  echo "9.4 The shell"
  for file in core/layout/shell.ts core/layout/navbar.ts core/layout/sidenav.ts core/layout/sidebar-store.ts     core/layout/navigation.ts core/layout/pages.ts shared/ui/lockup.ts shared/ui/page-header.ts; do
    check "$file is tracked" tracked "$app/$file"
  done
  check "the layout specs pass, with the 403 page at the address asked for" specs_pass src/app/core/layout/layout.spec.ts 15
  check "the sidebar remembers its state and folds by the window's width" specs_pass src/app/core/layout/sidebar-store.spec.ts 5
  check "dates and numbers are Romanian" specs_pass src/app/app.config.spec.ts 7
  check "the toolbar has no global search" no_search_in_the_toolbar
  check "the lockup is inline SVG" lockup_is_inline
  check "the sidebar is as wide as its content" css_has '--mat-sidenav-container-width:auto'
  check "the build carries the Romanian locale" bundle_has '"ro-RO"'
}

# ---- 9.8

pages_load_lazily() {
  in_file "import('./features/auth/login-page')" "$web/src/app/app.routes.ts" &&
    in_file "import('./features/auth/change-password-page')" "$web/src/app/app.routes.ts"
}

task_8() {
  echo "9.8 The login page and the forced password change"
  for file in login-page.ts login-page.html change-password-page.ts change-password-page.html auth-card.scss; do
    check "features/auth/$file is tracked" tracked "$app/features/auth/$file"
  done
  check "their specs pass" specs_pass src/app/features/auth/auth-pages.spec.ts 13
  check "the guards' specs pass, the login page only for those signed out" specs_pass src/app/core/auth/guards.spec.ts 9
  check "both pages load when first needed" pages_load_lazily
  check "a refused login says only that the email or password is wrong" messages_have 'Email sau parolă incorecte.'
  check "the password rules are listed" messages_have 'Diferită de parola curentă'
}

# ---- 9.10

task_10() {
  echo "9.10 The specs of the session"
  check "the store's, with the races between requests and between tabs" specs_pass src/app/core/auth/auth-store.spec.ts 20
  check "the interceptor's, with a refresh refused, failed or already done" specs_pass src/app/core/auth/auth-interceptor.spec.ts 13
}

# ---- 9.7 (done second: every text in a template needs it)

extracted() {
  [ -f "$scratch/locale/messages.xlf" ] && return 0
  (cd "$web" && npx ng extract-i18n --output-path "$scratch/locale") >"$scratch/extract.log" 2>&1 || {
    plain "$scratch/extract.log" | tail -n 25
    return 1
  }
}

messages_have() { # messages_have <text>
  extracted && in_file "$1" "$scratch/locale/messages.xlf"
}

localize_is_installed() {
  in_file '"@angular/localize"' "$web/package.json" && in_file '"@angular/localize/init"' "$web/angular.json"
}

source_locale_is_romanian() {
  in_file '"sourceLocale": "ro"' "$web/angular.json"
}

task_7() {
  echo "9.7 i18n with Romanian as the source"
  check "@angular/localize is installed and loaded" localize_is_installed
  check "the source locale is ro" source_locale_is_romanian
  check "its spec passes" specs_pass src/app/localize.spec.ts 2
  check "ng extract-i18n works" extracted
  check "and writes Romanian as the source language" messages_have 'source-language="ro"'
}

# ---- 9a.1

# The whole tokens file against UI spec 2.4, light and dark, compared value by value.
tokens_are_the_specs() {
  node -e "
    const fs = require('fs');
    const spec = fs.readFileSync('docs/UI_UX_SPEC.md', 'utf8').replace(/\r/g, '');
    const start = spec.indexOf('\`src/styles/_tokens.scss\`:');
    const open = spec.indexOf('\`\`\`scss', start);
    const fromSpec = spec.slice(open + 7, spec.indexOf('\`\`\`', open + 7));
    const fromFile = fs.readFileSync('$web/src/styles/_tokens.scss', 'utf8').replace(/\r/g, '');
    const normalize = (value) => value.replace(/\s+/g, '').replace(/([(,:])\./g, '\$10.').replace(/(\.\d*?[1-9])0+(?!\d)/g, '\$1').toLowerCase();
    const tokens = (part) => Object.fromEntries([...part.matchAll(/(--kb-[a-z0-9-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], normalize(m[2])]));
    const parts = (source) => [source.slice(0, source.indexOf('@mixin dark')), source.slice(source.indexOf('@mixin dark'))].map(tokens);
    const [specLight, specDark] = parts(fromSpec);
    const [fileLight, fileDark] = parts(fromFile);
    let bad = 0;
    for (const [name, a, b] of [['light', specLight, fileLight], ['dark', specDark, fileDark]]) {
      if (Object.keys(a).length < 10) { console.log('too few tokens read from the spec: ' + name); bad++; }
      for (const key of new Set([...Object.keys(a), ...Object.keys(b)])) {
        if (a[key] !== b[key]) { console.log(name + ' ' + key + ': spec ' + a[key] + ', file ' + b[key]); bad++; }
      }
    }
    process.exit(bad ? 1 : 0);
  "
}

# The three tokens of UI spec v1.6, with the spec's values (2.3, 2.4) written here. In dark, null
# means the token isn't redefined there: it keeps its light value.
new_tokens_have_the_specs_values() {
  node -e "
    const source = require('fs').readFileSync('$web/src/styles/_tokens.scss', 'utf8');
    const split = source.indexOf('@mixin dark');
    const value = (part, name) => (part.match(new RegExp('--kb-' + name + ':\\\\s*([^;]+);')) || [])[1] ?? null;
    let bad = 0;
    for (const [name, light, dark] of [['on-ink-accent', '#91c4fb', null], ['logo-bar', '#052063', '#ffffff'], ['logo-disc', '#91c4fb', null]]) {
      const inLight = value(source.slice(0, split), name);
      const inDark = value(source.slice(split), name);
      if (inLight !== light) { console.log('light ' + name + ': ' + inLight + ', expected ' + light); bad++; }
      if (inDark !== dark) { console.log('dark ' + name + ': ' + inDark + ', expected ' + (dark ?? 'the light value')); bad++; }
    }
    process.exit(bad ? 1 : 0);
  "
}

# The snackbar's action on ink, AA for normal text in both themes. The snackbar is ink at 92%, so
# it's also measured over the lightest surface showing through it.
on_ink_accent_reads_on_ink() {
  node -e "
    const source = require('fs').readFileSync('$web/src/styles/_tokens.scss', 'utf8');
    const split = source.indexOf('@mixin dark');
    const tokens = (part) => Object.fromEntries([...part.matchAll(/--kb-([a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{6})\s*;/g)].map((m) => [m[1], m[2]]));
    const light = tokens(source.slice(0, split));
    const dark = { ...light, ...tokens(source.slice(split)) };
    const channels = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
    const luminance = (hex) => {
      const [r, g, b] = channels(hex).map((c) => c / 255).map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    const ratio = (a, b) => {
      const [high, low] = [luminance(a), luminance(b)].sort((x, y) => y - x);
      return (high + 0.05) / (low + 0.05);
    };
    const mix = (a, b, share) => '#' + channels(a).map((c, i) => Math.round(c * share + channels(b)[i] * (1 - share)).toString(16).padStart(2, '0')).join('');
    let bad = 0;
    for (const [mode, t] of [['light', light], ['dark', dark]]) {
      if (!t['on-ink-accent'] || !t['ink']) { console.log(mode + ': a token is missing'); bad++; continue; }
      for (const [name, background] of [['ink', t['ink']], ['ink at 92% over ' + mode + ' surface-0', mix(t['ink'], t['surface-0'], 0.92)]]) {
        const value = ratio(t['on-ink-accent'], background);
        if (value < 4.5) { console.log(mode + ' on-ink-accent on ' + name + ': ' + value.toFixed(2)); bad++; }
      }
    }
    process.exit(bad ? 1 : 0);
  "
}

lockup_takes_the_logo_tokens() {
  lockup=$app/shared/ui/lockup.ts
  in_file 'fill: var(--kb-logo-bar)' "$lockup" && in_file 'fill: var(--kb-logo-disc)' "$lockup" &&
    lacks 'var(--kb-sky-300)' "$lockup" && lacks 'var(--kb-navy-900)' "$lockup"
}

task_9a() {
  echo "9a.1 The tokens of UI spec v1.6"
  check "_tokens.scss is UI spec 2.4's, in light and dark" tokens_are_the_specs
  check "on-ink-accent, logo-bar and logo-disc have the spec's values" new_tokens_have_the_specs_values
  check "the snackbar's action passes AA on ink, in both themes" on_ink_accent_reads_on_ink
  check "the snackbar's action is on-ink-accent" css_has '--mat-snack-bar-button-color:var(--kb-on-ink-accent)'
  check "the lockup takes the mark's colors, not the theme's" lockup_takes_the_logo_tokens
  check "the build ships the lockup's bar in logo-bar" bundle_has 'kb-logo-bar)'
  check "and its discs in logo-disc" bundle_has 'kb-logo-disc)'
}

if [ "$#" -eq 0 ]; then
  set -- 1 7 2 3 9 5 6 4 8 10 9a
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
