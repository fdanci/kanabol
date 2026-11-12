#!/bin/sh
# Acceptance checks for the commit pipeline (group 5 in the project's Tasks.md).
# Usage: scripts/check-pipeline.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. Needs the packages scripts/setup.sh installs, and no Docker.
# Since docs/SPEC.md v1.33 the hooks run the same fast checks on main as on any other branch, and the full
# suite runs in the GitHub workflow (docs/ci.md): tasks 3 to 6 check that main gets only the fast checks.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# A docker that always answers, or one that never does: the hooks need none, on main either.
stubs="$scratch/stubs"
no_docker="$scratch/no-docker"
mkdir -p "$stubs" "$no_docker"
printf '#!/bin/sh\nexit 0\n' >"$stubs/docker"
printf '#!/bin/sh\necho "Cannot connect to the Docker daemon" >&2\nexit 1\n' >"$no_docker/docker"
chmod +x "$stubs/docker" "$no_docker/docker"
real_path=$PATH
PATH="$stubs:$PATH"

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

not_in_file() { # not_in_file <text> <file>
  ! grep -qF -- "$1" "$2"
}

code_lacks() { # code_lacks <text> <script>: not in its code, its comments aside
  ! grep -v '^[[:space:]]*#' "$2" | grep -qF -- "$1"
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
      git tag starting-point &&
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
  # main goes back too, since a check may have merged into it.
  git -C "$repo" checkout -q -f -B main starting-point &&
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

executable() {
  git ls-files -s -- "$1" | grep -q '^100755'
}

on_a_feature_branch() {
  git -C "$repo" checkout -q -B feature/probe
}

# Stands in for the full suite, so these checks can see whether it ran. Task 6 runs the real one.
stub_full_suite() {
  printf '#!/bin/sh\necho "the full suite ran"\nexit 7\n' >"$repo/scripts/test-all.sh"
}

verify() { # verify <mode>
  verify_log="$scratch/verify.log"
  (cd "$repo" && scripts/verify.sh "$@") >"$verify_log" 2>&1
}

verify_log_has() { # verify_log_has <extended regex>
  sed -e 's/\x1b\[[0-9;]*m//g' "$verify_log" | grep -qE -- "$1" || {
    echo "the output doesn't have: $1"
    tail -n 12 "$verify_log"
    return 1
  }
}

verify_log_lacks() { # verify_log_lacks <extended regex>
  if sed -e 's/\x1b\[[0-9;]*m//g' "$verify_log" | grep -qE -- "$1"; then
    echo "the output has: $1"
    return 1
  fi
}

verify_passes() {
  verify commit || {
    echo "verify.sh failed"
    tail -n 12 "$verify_log"
    return 1
  }
}

verify_refuses() {
  if verify commit; then
    echo "verify.sh passed"
    return 1
  fi
}

verify_keeps_main_to_the_fast_checks() {
  fresh_sandbox && stub_full_suite && stage_in_sandbox docs/probe.md '- a\n' || return 1
  verify_passes && verify_log_has 'main: fast checks' && verify_log_lacks 'the full suite ran'
}

verify_builds_backend_changes_on_main() {
  fresh_sandbox && stub_full_suite || return 1
  stage_in_sandbox backend/src/Kanabol.Domain/Probe.cs 'namespace Kanabol.Domain;\n\npublic   class Probe\n{\n}\n' || return 1
  verify_passes && staged_has backend/src/Kanabol.Domain/Probe.cs 'public class Probe' &&
    verify_log_has '^Passed!.*Kanabol.Domain.Tests' && verify_log_lacks 'the full suite ran'
}

verify_stops_on_a_failing_domain_test_on_main() {
  fresh_sandbox || return 1
  stage_in_sandbox backend/tests/Kanabol.Domain.Tests/FailingTests.cs 'namespace Kanabol.Domain.Tests;\n\npublic sealed class FailingTests\n{\n    [Fact]\n    public void Always_WhenRun_Fails() => 1.Should().Be(2);\n}\n' || return 1
  verify_refuses && verify_log_has 'Always_WhenRun_Fails'
}

verify_keeps_docs_fast_on_a_branch() {
  fresh_sandbox && on_a_feature_branch && stub_full_suite && stage_in_sandbox docs/probe.md '* a\n' || return 1
  mkdir -p "$scratch/no-dotnet" || return 1
  printf '#!/bin/sh\necho "dotnet ran"\nexit 1\n' >"$scratch/no-dotnet/dotnet"
  chmod +x "$scratch/no-dotnet/dotnet"
  verify_log="$scratch/verify.log"
  (cd "$repo" && PATH="$scratch/no-dotnet:$PATH" scripts/verify.sh commit) >"$verify_log" 2>&1 || {
    echo "verify.sh failed"
    tail -n 12 "$verify_log"
    return 1
  }
  verify_log_lacks 'the full suite ran' &&
    verify_log_lacks 'dotnet ran' &&
    verify_log_lacks 'Tests +[0-9]+ passed' &&
    verify_log_has 'fast checks' &&
    staged_has docs/probe.md '- a'
}

verify_formats_and_builds_backend_changes() {
  fresh_sandbox && on_a_feature_branch && stub_full_suite || return 1
  stage_in_sandbox backend/src/Kanabol.Domain/Probe.cs 'namespace Kanabol.Domain;\n\npublic   class Probe\n{\n}\n' || return 1
  verify_passes && staged_has backend/src/Kanabol.Domain/Probe.cs 'public class Probe' && verify_log_lacks 'the full suite ran'
}

verify_stops_on_a_build_error() {
  fresh_sandbox && on_a_feature_branch || return 1
  stage_in_sandbox backend/src/Kanabol.Domain/Probe.cs 'namespace Kanabol.Domain;\n\npublic class Probe\n{\n    public int Value => "nope";\n}\n' || return 1
  verify_refuses && verify_log_has 'CS0029'
}

verify_stops_on_a_failing_domain_test() {
  fresh_sandbox && on_a_feature_branch || return 1
  stage_in_sandbox backend/tests/Kanabol.Domain.Tests/FailingTests.cs 'namespace Kanabol.Domain.Tests;\n\npublic sealed class FailingTests\n{\n    [Fact]\n    public void Always_WhenRun_Fails() => 1.Should().Be(2);\n}\n' || return 1
  verify_refuses && verify_log_has 'Always_WhenRun_Fails'
}

verify_runs_frontend_specs() {
  fresh_sandbox && on_a_feature_branch && stub_full_suite || return 1
  stage_in_sandbox frontend/src/app/probe.ts "export const probe = 'x';\n" || return 1
  verify_passes && verify_log_has 'Tests +[0-9]+ passed' && verify_log_lacks 'the full suite ran'
}

verify_stops_on_a_failing_spec() {
  fresh_sandbox && on_a_feature_branch || return 1
  stage_in_sandbox frontend/src/app/failing.spec.ts "describe('always', () => {\n  it('fails', () => {\n    expect(1).toBe(2);\n  });\n});\n" || return 1
  verify_refuses && verify_log_has 'Tests +1 failed'
}

verify_stops_on_lint_before_the_suite() {
  fresh_sandbox && stub_full_suite && stage_in_sandbox frontend/src/app/probe.ts 'export const probe: any = 1;\n' || return 1
  verify_refuses && verify_log_has 'no-explicit-any' && verify_log_lacks 'the full suite ran'
}

verify_without_docker() {
  verify_log="$scratch/verify.log"
  (cd "$repo" && PATH="$no_docker:$real_path" scripts/verify.sh commit) >"$verify_log" 2>&1
}

verify_needs_no_docker_on_main() {
  fresh_sandbox && stub_full_suite && stage_in_sandbox docs/probe.md '- a\n' || return 1
  verify_without_docker || {
    tail -n 12 "$verify_log"
    return 1
  }
  verify_log_lacks "Docker isn't running" && verify_log_lacks 'the full suite ran'
}

verify_needs_no_docker_on_a_branch() {
  fresh_sandbox && on_a_feature_branch && stage_in_sandbox docs/probe.md '- a\n' || return 1
  verify_without_docker || {
    tail -n 12 "$verify_log"
    return 1
  }
}

task_4() {
  echo "Task 4: scripts/verify.sh"
  check "it's tracked" tracked scripts/verify.sh
  check "it's executable" executable scripts/verify.sh
  check "it's valid sh" sh -n scripts/verify.sh
  check "it never runs the full suite" code_lacks 'test-all.sh' scripts/verify.sh
  check "main has no case of its own in it" code_lacks '"main"' scripts/verify.sh
  check "master is gone" not_in_file master scripts/verify.sh
  check "lint-staged gets relative paths, the only ones dotnet format's --include accepts" grep -qF 'lint-staged --relative' scripts/verify.sh
  check "main runs the fast checks, not the full suite" verify_keeps_main_to_the_fast_checks
  check "on main backend changes are formatted and built, and the domain tests run" verify_builds_backend_changes_on_main
  check "on main a failing domain test stops it" verify_stops_on_a_failing_domain_test_on_main
  check "a branch with only docs runs neither the suite nor the builds" verify_keeps_docs_fast_on_a_branch
  check "a branch with backend changes formats and builds them" verify_formats_and_builds_backend_changes
  check "a build error stops it" verify_stops_on_a_build_error
  check "a failing domain test stops it" verify_stops_on_a_failing_domain_test
  check "a branch with frontend changes runs the specs" verify_runs_frontend_specs
  check "a failing spec stops it" verify_stops_on_a_failing_spec
  check "on main a lint error stops it" verify_stops_on_lint_before_the_suite
  check "the Docker check is tracked" tracked scripts/require-docker.sh
  check "the Docker check is executable" executable scripts/require-docker.sh
  check "the Docker check is valid sh" sh -n scripts/require-docker.sh
  check "on main, Docker isn't needed either" verify_needs_no_docker_on_main
  check "on a branch, Docker isn't needed" verify_needs_no_docker_on_a_branch
}

hook_is() { # hook_is <hook> <text>
  grep -qF -- "$2" ".husky/$1"
}

commit_in_sandbox() { # commit_in_sandbox <message>
  commit_log="$scratch/commit.log"
  git -C "$repo" commit -m "$1" >"$commit_log" 2>&1
}

head_of() {
  git -C "$repo" rev-parse "$1"
}

hooks_are_installed_by_npm() {
  sandbox || return 1
  [ "$(git -C "$repo" config core.hooksPath)" = '.husky/_' ]
}

bad_message_is_refused() {
  fresh_sandbox && on_a_feature_branch && stage_in_sandbox docs/probe.md '- a\n' || return 1
  before=$(head_of HEAD)
  if commit_in_sandbox 'added some things'; then
    echo "the commit went through"
    return 1
  fi
  grep -q 'subject may not be empty\|type may not be empty' "$commit_log" || {
    tail -n 12 "$commit_log"
    return 1
  }
  [ "$(head_of HEAD)" = "$before" ]
}

commit_goes_through_verify() {
  fresh_sandbox && on_a_feature_branch && stage_in_sandbox docs/probe.md '* a\n' || return 1
  commit_in_sandbox 'docs: add a probe' || {
    tail -n 12 "$commit_log"
    return 1
  }
  git -C "$repo" show HEAD:docs/probe.md | grep -qx -- '- a'
}

# The suite stands in as one that fails, so a hook that ran it would refuse.
commit_on_main_skips_the_suite() {
  fresh_sandbox && stub_full_suite && stage_in_sandbox docs/probe.md '* a\n' || return 1
  commit_in_sandbox 'docs: add a probe' || {
    tail -n 12 "$commit_log"
    return 1
  }
  grep -q 'main: fast checks' "$commit_log" && ! grep -q 'the full suite ran' "$commit_log" &&
    git -C "$repo" show HEAD:docs/probe.md | grep -qx -- '- a'
}

feature_with_a_commit() {
  fresh_sandbox && on_a_feature_branch && stage_in_sandbox docs/probe.md '- a\n' &&
    commit_in_sandbox 'docs: add a probe' && git -C "$repo" checkout -q main
}

merge_into_main() {
  merge_log="$scratch/merge.log"
  git -C "$repo" merge --no-ff --no-edit feature/probe >"$merge_log" 2>&1
}

merge_skips_the_suite() {
  feature_with_a_commit && stub_full_suite || return 1
  merge_into_main || {
    tail -n 12 "$merge_log"
    return 1
  }
  grep -q 'merge: lint-staged' "$merge_log" && grep -q 'main: fast checks' "$merge_log" &&
    ! grep -q 'the full suite ran' "$merge_log" &&
    [ "$(git -C "$repo" rev-list --parents -n 1 main | wc -w)" -eq 3 ]
}

# A domain test that fails, committed past the hook on a branch: the merge's fast checks stop it.
merge_stops_on_a_failing_domain_test() {
  fresh_sandbox && on_a_feature_branch || return 1
  stage_in_sandbox backend/tests/Kanabol.Domain.Tests/FailingTests.cs 'namespace Kanabol.Domain.Tests;\n\npublic sealed class FailingTests\n{\n    [Fact]\n    public void Always_WhenRun_Fails() => 1.Should().Be(2);\n}\n' &&
    git -C "$repo" commit -q --no-verify -m 'test(api): add a test that fails' &&
    git -C "$repo" checkout -q main || return 1
  before=$(head_of main)
  if merge_into_main; then
    echo "the merge went through"
    return 1
  fi
  grep -q 'Always_WhenRun_Fails' "$merge_log" && [ "$(head_of main)" = "$before" ]
}

remote_for_sandbox() {
  rm -rf "$scratch/remote.git"
  git init -q --bare "$scratch/remote.git" &&
    git -C "$repo" remote remove origin >/dev/null 2>&1
  git -C "$repo" remote add origin "$scratch/remote.git"
}

push_to_main_skips_the_suite() {
  fresh_sandbox && stub_full_suite && remote_for_sandbox || return 1
  push_log="$scratch/push.log"
  git -C "$repo" push origin main >"$push_log" 2>&1 || {
    tail -n 12 "$push_log"
    return 1
  }
  ! grep -q 'the full suite ran' "$push_log" &&
    [ "$(git -C "$scratch/remote.git" rev-parse refs/heads/main)" = "$(head_of main)" ]
}

push_to_a_branch_skips_the_suite() {
  fresh_sandbox && on_a_feature_branch && stub_full_suite && remote_for_sandbox || return 1
  push_log="$scratch/push.log"
  git -C "$repo" push origin feature/probe >"$push_log" 2>&1 || {
    tail -n 12 "$push_log"
    return 1
  }
  ! grep -q 'the full suite ran' "$push_log"
}

merge_needs_no_docker() {
  feature_with_a_commit && stub_full_suite || return 1
  merge_log="$scratch/merge.log"
  PATH="$no_docker:$real_path" git -C "$repo" merge --no-ff --no-edit feature/probe >"$merge_log" 2>&1 || {
    tail -n 12 "$merge_log"
    return 1
  }
  ! grep -q "Docker isn't running" "$merge_log" && ! grep -q 'the full suite ran' "$merge_log"
}

push_needs_no_docker() {
  fresh_sandbox && stub_full_suite && remote_for_sandbox || return 1
  push_log="$scratch/push.log"
  PATH="$no_docker:$real_path" git -C "$repo" push origin main >"$push_log" 2>&1 || {
    tail -n 12 "$push_log"
    return 1
  }
  ! grep -q "Docker isn't running" "$push_log" &&
    [ "$(git -C "$scratch/remote.git" rev-parse refs/heads/main)" = "$(head_of main)" ]
}

task_3() {
  echo "Task 3: the hooks"
  for hook in pre-commit pre-merge-commit commit-msg; do
    check "$hook is tracked" tracked ".husky/$hook"
    check "$hook is executable" executable ".husky/$hook"
    check "$hook is valid sh" sh -n ".husky/$hook"
    check "$hook has no line husky has deprecated" not_in_file husky.sh ".husky/$hook"
  done
  check ".husky/.gitkeep is gone" test ! -e .husky/.gitkeep
  check "pre-commit runs verify.sh" hook_is pre-commit 'exec scripts/verify.sh commit'
  check "pre-merge-commit runs verify.sh" hook_is pre-merge-commit 'exec scripts/verify.sh merge'
  check "commit-msg runs commitlint" hook_is commit-msg 'npx --no -- commitlint --edit "$1"'
  check "there's no pre-push hook: the workflow runs the suite on a push to main" test ! -e .husky/pre-push
  check "npm installs the hooks" hooks_are_installed_by_npm
  check "a bad message is refused and nothing is committed" bad_message_is_refused
  check "a commit goes through verify.sh, and what it formats gets committed" commit_goes_through_verify
  check "a commit on main runs the fast checks, not the suite" commit_on_main_skips_the_suite
  check "a merge into main runs the fast checks, not the suite, as a merge commit" merge_skips_the_suite
  check "a merge into main stops on a failing domain test" merge_stops_on_a_failing_domain_test
  check "a push to main runs no suite" push_to_main_skips_the_suite
  check "a push to a branch runs none either" push_to_a_branch_skips_the_suite
  check "a merge into main needs no Docker" merge_needs_no_docker
  check "a push to main needs no Docker" push_needs_no_docker
}

# A repo as a fresh clone has it, with no packages installed.
bare_copy() { # bare_copy <folder>
  tree=$(git write-tree) || return 1
  mkdir -p "$1" || return 1
  git archive "$tree" | tar -x -C "$1" || return 1
  git -C "$1" init -q -b main &&
    git -C "$1" config user.name check &&
    git -C "$1" config user.email check@example.invalid &&
    git -C "$1" add -A &&
    git -C "$1" commit -q -m 'chore: the starting point'
}

setup_copy_ready() {
  setup_copy="$scratch/setup"
  [ -d "$setup_copy/.git" ] && return 0
  bare_copy "$setup_copy" || return 1
  (cd "$setup_copy" && scripts/setup.sh) >"$scratch/setup.log" 2>&1 || {
    tail -n 12 "$scratch/setup.log"
    rm -rf "$setup_copy"
    return 1
  }
}

setup_config_is() { # setup_config_is <key> <value>
  setup_copy_ready || return 1
  [ "$(git -C "$setup_copy" config "$1")" = "$2" ] || {
    echo "$1 is $(git -C "$setup_copy" config "$1"), expected $2"
    return 1
  }
}

setup_installed() { # setup_installed <path>
  setup_copy_ready && [ -e "$setup_copy/$1" ]
}

setup_restored_tools() {
  setup_copy_ready && (cd "$setup_copy" && dotnet tool run dotnet-ef --version)
}

setup_leaves_the_tree_clean() {
  setup_copy_ready || return 1
  [ -z "$(git -C "$setup_copy" status --porcelain)" ] || {
    git -C "$setup_copy" status --short | head -n 10
    return 1
  }
}

setup_can_run_again() {
  setup_copy_ready && (cd "$setup_copy" && scripts/setup.sh) >"$scratch/setup-again.log" 2>&1 || {
    tail -n 12 "$scratch/setup-again.log"
    return 1
  }
}

setup_turns_on_the_hooks() {
  setup_copy_ready || return 1
  if git -C "$setup_copy" commit --allow-empty -m 'added some things' >"$scratch/setup-commit.log" 2>&1; then
    echo "the commit went through"
    return 1
  fi
  grep -q 'type may not be empty' "$scratch/setup-commit.log"
}

setup_makes_merges_into_main_create_a_commit() {
  setup_copy_ready || return 1
  git -C "$setup_copy" checkout -q -b feature/probe &&
    printf '# Probe\n' >"$setup_copy/probe.md" &&
    git -C "$setup_copy" add probe.md &&
    git -C "$setup_copy" commit -q -m 'docs: add a probe' &&
    git -C "$setup_copy" checkout -q main || return 1
  # No --no-ff here: this branch could fast-forward, and the setting has to prevent it, so the merge
  # commit goes through the hook.
  git -C "$setup_copy" merge --no-edit feature/probe >"$scratch/setup-merge.log" 2>&1 || {
    tail -n 12 "$scratch/setup-merge.log"
    return 1
  }
  grep -q 'merge: lint-staged' "$scratch/setup-merge.log" &&
    [ "$(git -C "$setup_copy" rev-list --parents -n 1 main | wc -w)" -eq 3 ]
}

task_5() {
  echo "Task 5: scripts/setup.sh"
  check "it's tracked" tracked scripts/setup.sh
  check "it's executable" executable scripts/setup.sh
  check "it's valid sh" sh -n scripts/setup.sh
  check "it installs the root packages" setup_installed node_modules/.bin/lint-staged
  check "it installs the frontend's" setup_installed frontend/node_modules/.bin/ng
  check "it restores the dotnet tools" setup_restored_tools
  check "merges into main never fast-forward" setup_config_is branch.main.mergeOptions --no-ff
  check "commits aren't blocked on signatures" setup_config_is commit.gpgsign false
  check "git looks for hooks where husky 9 keeps them" setup_config_is core.hooksPath .husky/_
  check "it leaves the tree clean" setup_leaves_the_tree_clean
  check "the hooks work after it" setup_turns_on_the_hooks
  check "a plain merge into main creates a commit, which goes through the hook" setup_makes_merges_into_main_create_a_commit
  check "it can run again" setup_can_run_again
  check "the root notes say to run it" grep -qF 'scripts/setup.sh' CLAUDE.md
}

# The real hooks and the real suite, in a throwaway repo with a local remote.
real_copy_ready() {
  real="$scratch/real"
  real_remote="$scratch/real-remote.git"
  [ -d "$real/.git" ] && return 0
  bare_copy "$real" || return 1
  git init -q --bare "$real_remote" && git -C "$real" remote add origin "$real_remote" || return 1
  (cd "$real" && scripts/setup.sh) >"$scratch/real-setup.log" 2>&1 || {
    tail -n 12 "$scratch/real-setup.log"
    rm -rf "$real"
    return 1
  }
  git -C "$real" checkout -q -b feature/probe
}

real_stage() { # real_stage <file> <content, with printf escapes>
  mkdir -p "$real/$(dirname "$1")" && printf '%b' "$2" >"$real/$1" && git -C "$real" add -- "$1"
}

real_commit() { # real_commit <seconds allowed> <message>
  started=$(date +%s)
  git -C "$real" commit -m "$2" >"$scratch/real-commit.log" 2>&1 || {
    tail -n 12 "$scratch/real-commit.log"
    return 1
  }
  took=$(($(date +%s) - started))
  [ "$took" -lt "$1" ] || {
    echo "it took $took s"
    return 1
  }
}

real_docs_commit() {
  real_copy_ready && real_stage docs/probe.md '# Probe\n' && real_commit 40 'docs: add a probe'
}

real_backend_commit() {
  real_copy_ready &&
    real_stage backend/src/Kanabol.Domain/Probe.cs 'namespace Kanabol.Domain;\n\npublic   class Probe\n{\n}\n' &&
    real_commit 40 'feat(api): add a probe'
}

real_frontend_commit() {
  real_copy_ready &&
    real_stage frontend/src/app/probe.ts "export const probe   =   'x';\n" &&
    real_commit 40 'feat(web): add a probe'
}

real_commit_on_both_sides() {
  real_copy_ready &&
    real_stage backend/src/Kanabol.Domain/ProbeTwo.cs 'namespace Kanabol.Domain;\n\npublic class ProbeTwo\n{\n}\n' &&
    real_stage frontend/src/app/probe-two.ts "export const probeTwo = 'x';\n" &&
    real_commit 40 'feat(api): add a probe on both sides'
}

real_committed_has() { # real_committed_has <file> <text>
  git -C "$real" show "HEAD:$1" | grep -qF -- "$2"
}

real_bad_message_is_refused() {
  real_copy_ready && real_stage docs/bad.md '# Bad\n' || return 1
  before=$(git -C "$real" rev-parse HEAD)
  if git -C "$real" commit -m 'added some things' >"$scratch/real-commit.log" 2>&1; then
    echo "the commit went through"
    return 1
  fi
  grep -q 'type may not be empty' "$scratch/real-commit.log" || {
    tail -n 12 "$scratch/real-commit.log"
    return 1
  }
  git -C "$real" reset -q HEAD docs/bad.md && rm -f "$real/docs/bad.md"
  [ "$(git -C "$real" rev-parse HEAD)" = "$before" ]
}

# The hooks' fast checks ran, and nothing of the full suite: one test project (the domain's) and the specs,
# no "all green" of scripts/test-all.sh.
real_fast_checks_ran() { # real_fast_checks_ran <log>
  sed -e 's/\x1b\[[0-9;]*m//g' "$1" >"$1.plain"
  grep -q 'main: fast checks' "$1.plain" &&
    [ "$(grep -c '^Passed!' "$1.plain")" -eq 1 ] &&
    grep -q '^Passed!.*Kanabol.Domain.Tests' "$1.plain" &&
    grep -qE 'Tests +[0-9]+ passed' "$1.plain" &&
    ! grep -q 'all green' "$1.plain" || {
    tail -n 12 "$1.plain"
    return 1
  }
}

# docs/SPEC.md §14.3 and §14.6 (v1.33): the merge into main runs the same fast checks as any commit, and
# the full suite is left to the GitHub workflow. The branch brings backend and frontend changes.
real_merge_runs_the_fast_checks() {
  real_copy_ready && git -C "$real" checkout -q main || return 1
  started=$(date +%s)
  git -C "$real" merge --no-ff --no-edit feature/probe >"$scratch/real-merge.log" 2>&1 || {
    tail -n 12 "$scratch/real-merge.log"
    return 1
  }
  took=$(($(date +%s) - started))
  real_fast_checks_ran "$scratch/real-merge.log" || return 1
  [ "$(git -C "$real" rev-list --parents -n 1 main | wc -w)" -eq 3 ] || {
    echo "main doesn't end in a merge commit"
    return 1
  }
}

failing_application_test='namespace Kanabol.Application.Tests;\n\npublic sealed class FailingTests\n{\n    [Fact]\n    public void Always_WhenRun_Fails() => 1.Should().Be(2);\n}\n'
failing_domain_test='namespace Kanabol.Domain.Tests;\n\npublic sealed class FailingDomainTests\n{\n    [Fact]\n    public void Always_WhenRun_FailsInTheDomain() => 1.Should().Be(2);\n}\n'

# A failing application test gets past every hook, on main too: only the workflow's suite finds it.
real_hooks_leave_application_tests_to_the_workflow() {
  real_copy_ready || return 1
  git -C "$real" checkout -q -b feature/failing main &&
    real_stage backend/tests/Kanabol.Application.Tests/FailingTests.cs "$failing_application_test" &&
    real_commit 300 'test(api): add a test that fails' &&
    git -C "$real" checkout -q main || return 1
  git -C "$real" merge --no-ff --no-edit feature/failing >"$scratch/real-merge-failing.log" 2>&1 || {
    tail -n 12 "$scratch/real-merge-failing.log"
    return 1
  }
  ! grep -q 'Always_WhenRun_Fails' "$scratch/real-merge-failing.log"
}

# A failing domain test committed with --no-verify: the merge's fast checks stop it, and main stays.
real_merge_refuses_a_failing_domain_test() {
  real_copy_ready || return 1
  git -C "$real" checkout -q -b feature/failing-domain main &&
    real_stage backend/tests/Kanabol.Domain.Tests/FailingDomainTests.cs "$failing_domain_test" &&
    git -C "$real" commit -q --no-verify -m 'test(api): add a domain test that fails' &&
    git -C "$real" checkout -q main || return 1
  before=$(git -C "$real" rev-parse main)
  if git -C "$real" merge --no-ff --no-edit feature/failing-domain >"$scratch/real-merge.log" 2>&1; then
    echo "the merge went through"
    return 1
  fi
  grep -q 'Always_WhenRun_FailsInTheDomain' "$scratch/real-merge.log" || {
    tail -n 12 "$scratch/real-merge.log"
    return 1
  }
  git -C "$real" merge --abort || return 1
  [ "$(git -C "$real" rev-parse main)" = "$before" ]
}

real_push_runs_no_suite() {
  real_copy_ready || return 1
  git -C "$real" push origin main >"$scratch/real-push.log" 2>&1 || {
    tail -n 12 "$scratch/real-push.log"
    return 1
  }
  ! grep -qE '^Passed!|all green|Tests +[0-9]+ passed' "$scratch/real-push.log" &&
    [ "$(git -C "$real_remote" rev-parse refs/heads/main)" = "$(git -C "$real" rev-parse main)" ]
}

show_time() {
  printf '        %s s\n' "${took:-?}"
}

task_6() {
  echo "Task 6: the real hooks, in a throwaway repo: main gets the fast checks, the workflow the suite"
  PATH=$real_path
  check "a commit with only docs takes under 40 s" real_docs_commit
  show_time
  check "a commit in the backend takes under 40 s" real_backend_commit
  show_time
  check "what it staged got formatted" real_committed_has backend/src/Kanabol.Domain/Probe.cs 'public class Probe'
  check "a commit in the frontend takes under 40 s" real_frontend_commit
  show_time
  check "what it staged got formatted" real_committed_has frontend/src/app/probe.ts "export const probe = 'x';"
  check "a commit on both sides takes under 40 s" real_commit_on_both_sides
  show_time
  check "a bad message is refused by commitlint" real_bad_message_is_refused
  check "a --no-ff merge into main runs the fast checks and not the full suite" real_merge_runs_the_fast_checks
  show_time
  check "a failing application test gets past the hooks, main's too: the workflow's suite is the one to find it" \
    real_hooks_leave_application_tests_to_the_workflow
  check "a merge into main refuses a failing domain test committed with --no-verify" real_merge_refuses_a_failing_domain_test
  check "a push to main runs no suite and reaches the remote" real_push_runs_no_suite
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 5 6
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
