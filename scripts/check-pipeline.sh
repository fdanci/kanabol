#!/bin/sh
# Acceptance checks for the commit pipeline (group 5 in the project's Tasks.md).
# Usage: scripts/check-pipeline.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Needs the packages scripts/setup.sh installs.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

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

json_is() { # json_is <file> <dotted.path> <expected>
  node -e '
    const [file, path, expected] = process.argv.slice(1);
    const value = path.split(">").reduce((at, key) => at?.[key], JSON.parse(require("fs").readFileSync(file, "utf8")));
    if (String(value) !== expected) { console.error(path + " is " + value + ", expected " + expected); process.exit(1); }
  ' "$@"
}

ignored() {
  git check-ignore -q "$1"
}

prettier_matches_the_frontend() {
  root=$(node -p "require('./package.json').devDependencies.prettier")
  frontend=$(node -p "require('./frontend/package.json').devDependencies.prettier")
  [ "$root" = "$frontend" ] || {
    echo "root has $root, frontend has $frontend"
    return 1
  }
}

message_accepted() {
  printf '%s\n' "$1" | npx --no -- commitlint
}

message_refused() {
  if output=$(printf '%s\n' "$1" | npx --no -- commitlint 2>&1); then
    echo "commitlint accepted: $1"
    return 1
  fi
  # Its own verdict, so a commitlint that fails to run doesn't count as a refusal.
  printf '%s\n' "$output" | grep -q 'found [1-9][0-9]* problems' || {
    printf '%s\n' "$output"
    return 1
  }
}

task_1() {
  echo "Task 1: root package.json and commitlint"
  check "package.json is tracked" tracked package.json
  check "its name marks it as the repo's, not the app's" json_is package.json name kanabol-repo
  check "it's private" json_is package.json private true
  check "prepare installs the hooks" json_is package.json 'scripts>prepare' husky
  check "it has husky 9" json_is package.json 'devDependencies>husky' '^9'
  check "it has lint-staged 15" json_is package.json 'devDependencies>lint-staged' '^15'
  check "it has commitlint 19" json_is package.json 'devDependencies>@commitlint/cli' '^19'
  check "it has the conventional rules 19" json_is package.json 'devDependencies>@commitlint/config-conventional' '^19'
  check "prettier is the frontend's version" prettier_matches_the_frontend
  check "the lock file is tracked" tracked package-lock.json
  check "node_modules is ignored" ignored node_modules/
  check "commitlint.config.js is tracked" tracked commitlint.config.js
  for scope in api web db auth board tasks infra ci docs; do
    check "scope $scope is accepted" message_accepted "feat($scope): add something"
  done
  check "the spec's example is accepted" message_accepted 'feat(board): drag and drop between columns'
  check "a commit without a scope is accepted" message_accepted 'docs: add the FluentAssertions decision record'
  check "a merge commit is accepted" message_accepted "Merge branch 'feature/board' into main"
  check "an unknown scope is refused" message_refused 'feat(banana): add something'
  check "an unknown type is refused" message_refused 'stuff(api): add something'
  check "a message without a type is refused" message_refused 'added some things'
  check "an empty subject is refused" message_refused 'feat(api):'
}

# A throwaway repo holding what's staged here, with its packages installed. The checks commit and format
# in it, so the real repo and its branches stay out of it.
sandbox() {
  repo="$scratch/repo"
  [ -d "$repo/.git" ] && return 0
  tree=$(git write-tree) || return 1
  mkdir -p "$repo" || return 1
  git archive "$tree" | tar -x -C "$repo" || return 1
  (
    cd "$repo" || exit 1
    git init -q -b main &&
      git config user.name check &&
      git config user.email check@example.invalid &&
      git add -A &&
      git commit -q -m 'chore: the starting point' &&
      npm ci --no-audit --no-fund >/dev/null 2>&1 &&
      cd frontend &&
      npm ci --no-audit --no-fund >/dev/null 2>&1
  ) || {
    rm -rf "$repo"
    echo "could not prepare the throwaway repo"
    return 1
  }
}

fresh_sandbox() {
  sandbox || return 1
  git -C "$repo" merge --abort >/dev/null 2>&1
  git -C "$repo" checkout -q -f main &&
    git -C "$repo" reset -q --hard &&
    git -C "$repo" clean -qfd
}

stage_in_sandbox() { # stage_in_sandbox <file> <content, with printf escapes>
  mkdir -p "$repo/$(dirname "$1")" || return 1
  printf '%b' "$2" >"$repo/$1" || return 1
  git -C "$repo" add -- "$1"
}

append_in_sandbox() { # append_in_sandbox <file> <line>
  printf '\n%s\n' "$2" >>"$repo/$1" || return 1
  git -C "$repo" add -- "$1"
}

lint_staged() {
  (cd "$repo" && npx --no -- lint-staged --relative)
}

staged_has() { # staged_has <file> <text>
  git -C "$repo" show ":$1" | grep -qF -- "$2" || {
    git -C "$repo" show ":$1" | head -n 15
    return 1
  }
}

formats() { # formats <file> <messy content> <the tidy line that has to come out>
  fresh_sandbox && stage_in_sandbox "$1" "$2" && lint_staged && staged_has "$1" "$3"
}

leaves_alone() { # leaves_alone <file to add to> <messy line>
  fresh_sandbox && append_in_sandbox "$1" "$2" && lint_staged && staged_has "$1" "$2"
}

eslint_refuses() {
  fresh_sandbox &&
    stage_in_sandbox frontend/src/app/probe.ts 'export const probe: any = 1;\n' || return 1
  if output=$(lint_staged 2>&1); then
    echo "lint-staged accepted the file"
    return 1
  fi
  printf '%s\n' "$output" | grep -q 'no-explicit-any' || {
    printf '%s\n' "$output" | tail -n 15
    return 1
  }
}

dotnet_format_formats_only_what_is_staged() {
  fresh_sandbox || return 1
  stage_in_sandbox backend/src/Kanabol.Domain/Probe.cs 'namespace Kanabol.Domain;\n\npublic   class Probe\n{\n}\n' || return 1
  printf 'namespace Kanabol.Domain;\n\npublic   class Unstaged\n{\n}\n' >"$repo/backend/src/Kanabol.Domain/Unstaged.cs"
  lint_staged || return 1
  staged_has backend/src/Kanabol.Domain/Probe.cs 'public class Probe' || return 1
  grep -qF 'public   class Unstaged' "$repo/backend/src/Kanabol.Domain/Unstaged.cs" || {
    echo "dotnet format changed a file nobody staged"
    return 1
  }
}

markdown_and_yaml_are_formatted() {
  git ls-files '*.md' '*.yaml' '*.yml' | grep -v '^frontend/' | xargs npx --no -- prettier --check
}

json_has_task() { # json_has_task <file> <glob> <command>
  node -e '
    const [file, glob, command] = process.argv.slice(1);
    const tasks = JSON.parse(require("fs").readFileSync(file, "utf8"))[glob] ?? [];
    if (!tasks.includes(command)) { console.error(glob + " has " + JSON.stringify(tasks)); process.exit(1); }
  ' "$@"
}

task_2() {
  echo "Task 2: lint-staged"
  check ".lintstagedrc.json is tracked" tracked .lintstagedrc.json
  check "the frontend has its own, so its tools run from frontend/" tracked frontend/.lintstagedrc.json
  check "C# goes through dotnet format" json_has_task .lintstagedrc.json 'backend/**/*.cs' 'dotnet format backend/Kanabol.sln --include'
  check "Markdown and YAML go through prettier" json_has_task .lintstagedrc.json '*.{md,yaml,yml}' 'prettier --write'
  check "TypeScript and HTML go through prettier" json_has_task frontend/.lintstagedrc.json '*.{ts,html}' 'prettier --write'
  check "TypeScript and HTML go through eslint" json_has_task frontend/.lintstagedrc.json '*.{ts,html}' 'eslint --fix --max-warnings=0'
  check "tracked Markdown and YAML outside frontend/ are already formatted" markdown_and_yaml_are_formatted
  check "TypeScript comes out in the frontend's style" formats frontend/src/app/probe.ts 'export const probe   =   "x"\n' "export const probe = 'x';"
  check "HTML is formatted" formats frontend/src/app/probe.html '<p   class="a"  >x</p>\n' '<p class="a">x</p>'
  check "SCSS is formatted" formats frontend/src/probe.scss '.a{color:red}\n' '.a {'
  check "JSON in the frontend is formatted" formats frontend/probe.json '{"a":1}\n' '{ "a": 1 }'
  check "Markdown in the frontend is formatted" formats frontend/PROBE.md '* a\n' '- a'
  check "Markdown elsewhere is formatted" formats docs/probe.md '* a\n' '- a'
  check "YAML is formatted" formats probe.yaml 'a:    1\n' 'a: 1'
  check "an ESLint error stops the commit" eslint_refuses
  check "dotnet format fixes staged C# and nothing else" dotnet_format_formats_only_what_is_staged
  check "the generated theme stays as it is" leaves_alone frontend/src/styles/theme/_theme-colors.scss '.probe{color:red}'
  check "the UI spec stays identical to its note" leaves_alone docs/UI_UX_SPEC.md '* probe item'
  check "decision records stay identical to their notes" leaves_alone docs/adr/0001-rbac-pe-doua-niveluri.md '* probe item'
  check "the spec keeps its tables as written" leaves_alone docs/SPEC.md '* probe item'
  check "npm's lock file stays out of prettier" grep -qx 'package-lock.json' frontend/.prettierignore
}

if [ "$#" -eq 0 ]; then
  set -- 1 2
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
