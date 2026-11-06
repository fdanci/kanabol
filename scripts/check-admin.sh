#!/bin/sh
# Acceptance checks for the administration of users and roles (group 10 in the project's Tasks.md).
# The specs hold the rules, so these checks run them, and add what they can't see: the routes, the
# texts as extracted for translation, and the production build.
#
# Usage: scripts/check-admin.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Run scripts/setup.sh first, which installs the packages.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
web=frontend
app=$web/src/app

cleanup() {
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

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

bundle_has() { # bundle_has <text>
  built && in_file "$1" "$scratch/bundle.js"
}

# The texts as ng extract-i18n writes them for translation, made once per run.
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

# A page of the shell that loads with its first visit, behind its permission: withPermission in
# app.routes.ts puts the 403 page after it on the same path.
lazy_behind() { # lazy_behind <path> <PERMISSIONS key> <routes file, from app/>
  node -e "
    const source = require('fs').readFileSync('$app/app.routes.ts', 'utf8');
    const route = new RegExp('withPermission\\\\(PERMISSIONS\\\\.$2, \\\\{\\\\s*path: \\'$1\\',[^}]*loadChildren: \\\\(\\\\) =>\\\\s*import\\\\(\\'./$3\\'\\\\)');
    if (!route.test(source)) { console.log('no lazy route for $1 behind $2 in app.routes.ts'); process.exit(1); }
    if (source.includes(\"placeholder('$1'\")) { console.log('the placeholder for $1 is still there'); process.exit(1); }
  "
}

# ---- 10.1

task_1() {
  echo "10.1 Roles"
  for file in models.ts permission-groups.ts roles-api.ts roles-store.ts role-editor.ts role-editor.html \
    roles-page.ts roles-page.html roles.routes.ts; do
    check "features/roles/$file is tracked" tracked "$app/features/roles/$file"
  done
  check "shared/models/page.ts is tracked" tracked "$app/shared/models/page.ts"
  check "the specs of the roles pass" specs_pass "src/app/features/roles/*.spec.ts" 24
  check "a form keeps to itself the codes it shows under a field" specs_pass src/app/core/api/error-interceptor.spec.ts 16
  check "/roles loads with its first visit, behind roles.manage" lazy_behind roles rolesManage features/roles/roles.routes
  check "the system role says why it can't change" messages_have 'Rolul de sistem nu se poate modifica'
  check "the permissions have Romanian names" messages_have 'Arhivează și restaurează task-uri'
  check "user counts follow the Romanian plural" messages_have ' de utilizatori}'
  check "the build reads the permissions the API knows" bundle_has '/permissions'
}

if [ "$#" -eq 0 ]; then
  set -- 1
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
