#!/bin/sh
# Acceptance checks for the frontend skeleton and authentication (group 9 in the project's Tasks.md).
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

if [ "$#" -eq 0 ]; then
  set -- 1 7 2 3
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
