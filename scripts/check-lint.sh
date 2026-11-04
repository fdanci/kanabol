#!/bin/sh
# Acceptance checks for the lint setup (group 1 in the project's Tasks.md).
# Usage: scripts/check-lint.sh [task-number...]    (no arguments: every task)
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

tracked() {
  [ -f "$1" ] && [ -n "$(git ls-files -- "$1")" ]
}

not_in_file() { # not_in_file <text> <file>
  ! grep -qF -- "$1" "$2"
}

# What MSBuild really evaluates, wherever the value comes from.
api_property_is() { # api_property_is <property> <value>
  [ "$(dotnet msbuild backend/Kanabol.Api/Kanabol.Api.csproj -getProperty:"$1" | tr -d '\r')" = "$2" ]
}

# Run a shell snippet in a throwaway copy of the backend as it's staged.
in_backend_copy() { # in_backend_copy <snippet>
  work=$(mktemp -d)
  cp .editorconfig "$work/"
  git archive "$(git write-tree)" backend | tar -x -C "$work"
  (cd "$work/backend" && eval "$1")
  status=$?
  rm -rf "$work"
  return "$status"
}

nullable_warning_breaks_the_build() {
  in_backend_copy '
    printf "namespace Kanabol.Api;\n\ninternal static class Probe\n{\n    internal static string Value { get; } = null;\n}\n" >Kanabol.Api/Probe.cs
    dotnet build Kanabol.sln --nologo 2>&1 | grep -q "error CS8625"
  '
}

inline_package_version_is_refused() {
  in_backend_copy '
    sed -i "s#</Project>#<ItemGroup><PackageReference Include=\"Serilog\" Version=\"4.0.0\" /></ItemGroup></Project>#" Kanabol.Api/Kanabol.Api.csproj
    dotnet restore Kanabol.sln --nologo 2>&1 | grep -q "NU1008"
  '
}

task_1() {
  echo "1.1 Directory.Build.props and central package management"
  check "Directory.Build.props is tracked" tracked backend/Directory.Build.props
  check "Directory.Packages.props is tracked" tracked backend/Directory.Packages.props
  for setting in TargetFramework Nullable ImplicitUsings; do
    check "the Api project no longer sets $setting" not_in_file "<$setting>" backend/Kanabol.Api/Kanabol.Api.csproj
  done
  while IFS='=' read -r property value; do
    check "the Api project evaluates $property=$value" api_property_is "$property" "$value"
  done <<'EOF'
TargetFramework=net10.0
Nullable=enable
ImplicitUsings=enable
LangVersion=latest
TreatWarningsAsErrors=true
AnalysisLevel=latest-recommended
EnforceCodeStyleInBuild=true
ManagePackageVersionsCentrally=true
GenerateDocumentationFile=false
EOF
  check "a nullable warning breaks the build" nullable_warning_breaks_the_build
  check "an inline package version is refused" inline_package_version_is_refused
}

backend_format_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
}

# --no-incremental: an up-to-date build wouldn't run the analyzers again.
backend_builds_without_warnings() {
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
}

# Negative controls: the two commands above must be able to fail.
format_rejects_bad_whitespace() {
  in_backend_copy '
    printf "namespace Kanabol.Api;\n\ninternal static class Probe\n{\n  internal static int Value => 1;\n}\n" >Kanabol.Api/Probe.cs
    ! dotnet format Kanabol.sln --verify-no-changes
  '
}

build_rejects_an_analyzer_warning() {
  in_backend_copy '
    printf "namespace Kanabol.Api;\n\ninternal sealed class Probe\n{\n    public int One() => 1;\n}\n" >Kanabol.Api/Probe.cs
    dotnet build Kanabol.sln -warnaserror --no-incremental --nologo 2>&1 | grep -q "error CA1822"
  '
}

task_2() {
  echo "1.2 dotnet format and dotnet build -warnaserror"
  check "dotnet format accepts the solution" backend_format_is_clean
  check "dotnet build -warnaserror accepts the solution" backend_builds_without_warnings
  check "control: dotnet format rejects bad whitespace" format_rejects_bad_whitespace
  check "control: the build rejects an analyzer warning" build_rejects_an_analyzer_warning
}

absent() {
  [ ! -e "$1" ]
}

ng() {
  (cd frontend && NG_CLI_ANALYTICS=false npx ng "$@")
}

# Generate a real component, look at what the schematics produced, and remove it.
# Angular 22 defaults to OnPush, and the schematic only writes the line for any other strategy.
schematics_generate_kb_onpush_scss() {
  probe=frontend/src/app/schematics-probe
  [ ! -e "$probe" ] || return 1
  ng generate component schematics-probe --skip-tests || return 1
  result=0
  [ -f "$probe/schematics-probe.scss" ] || result=1
  grep -q "selector: 'kb-schematics-probe'" "$probe/schematics-probe.ts" || result=1
  grep -q 'changeDetection' "$probe/schematics-probe.ts" && result=1
  rm -rf "$probe"
  return "$result"
}

schematics_ask_for_onpush_and_scss() {
  (cd frontend && node -e "
    const c = require('./angular.json').projects.kanabol.schematics['@schematics/angular:component'];
    process.exit(c.changeDetection === 'OnPush' && c.style === 'scss' ? 0 : 1);
  ")
}

no_app_root_left() {
  ! grep -rq 'app-root' frontend/src
}

build_renders_kb_root() {
  ng build || return 1
  grep -q '<kb-root>' frontend/dist/kanabol/browser/index.html
}

frontend_tests_pass() {
  ng test --no-watch
}

task_3() {
  echo "1.3 kb prefix, schematics and SCSS"
  check "angular.json uses the kb prefix" grep -q '"prefix": "kb"' frontend/angular.json
  check "angular.json builds styles.scss" grep -q '"styles": \["src/styles.scss"\]' frontend/angular.json
  check "styles.css is now styles.scss" tracked frontend/src/styles.scss
  check "styles.css is gone" absent frontend/src/styles.css
  check "app.css is now app.scss" tracked frontend/src/app/app.scss
  check "app.css is gone" absent frontend/src/app/app.css
  check "app.ts uses kb-root" grep -q "selector: 'kb-root'" frontend/src/app/app.ts
  check "app.ts uses app.scss" grep -q "styleUrl: './app.scss'" frontend/src/app/app.ts
  check "index.html uses kb-root" grep -q '<kb-root></kb-root>' frontend/src/index.html
  check "no app-root is left" no_app_root_left
  check "angular.json asks for OnPush and SCSS" schematics_ask_for_onpush_and_scss
  check "ng generate gives kb, OnPush and SCSS" schematics_generate_kb_onpush_scss
  check "the production build renders kb-root" build_renders_kb_root
  check "the frontend tests pass" frontend_tests_pass
}

typescript_is_effectively_strict() {
  (cd frontend && npx tsc -p tsconfig.app.json --showConfig) | grep -q '"strict": true'
}

angular_compiler_is_clean() {
  (cd frontend && npx ngc -p tsconfig.app.json --noEmit)
}

# Binding a number to a string input only fails under strictTemplates.
strict_templates_reject_a_wrong_input_type() {
  probe=frontend/src/app/strict-templates-probe.ts
  [ ! -e "$probe" ] || return 1
  cat >"$probe" <<'TS'
import { Component, Input } from '@angular/core';

@Component({ selector: 'kb-strict-child', template: '' })
export class StrictChild {
  @Input() label = '';
}

@Component({
  imports: [StrictChild],
  selector: 'kb-strict-probe',
  template: '<kb-strict-child [label]="1" />',
})
export class StrictProbe {}
TS
  output=$(cd frontend && npx ngc -p tsconfig.app.json --noEmit 2>&1)
  rm -f "$probe"
  printf '%s\n' "$output" | grep -q 'TS2322'
}

task_4() {
  echo "1.4 strict TypeScript and templates"
  check "tsconfig.json sets strict explicitly" grep -q '"strict": true' frontend/tsconfig.json
  check "tsconfig.json sets strictTemplates explicitly" grep -q '"strictTemplates": true' frontend/tsconfig.json
  for option in noImplicitOverride noPropertyAccessFromIndexSignature noImplicitReturns noFallthroughCasesInSwitch; do
    check "tsconfig.json keeps $option" grep -q "\"$option\": true" frontend/tsconfig.json
  done
  check "the compiler sees strict: true" typescript_is_effectively_strict
  check "the Angular compiler accepts the app" angular_compiler_is_clean
  check "a wrong input type in a template is rejected" strict_templates_reject_a_wrong_input_type
}

PROBE_DIR=frontend/src/app/lint-probe

write_lint_probe() {
  mkdir -p "$PROBE_DIR"
  cat >"$PROBE_DIR/rule-breaker.ts" <<'TS'
import { ChangeDetectionStrategy, Component, Directive, Input } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'wrong-prefix',
  templateUrl: './rule-breaker.html',
})
export class RuleBreaker {
  @Input() label = '';

  public untyped: any = 1;

  load(): void {
    console.log(this.label);
    Promise.resolve(1);
  }
}

@Directive({ selector: '[wrongPrefix]' })
export class WrongDirective {}

export class EmptyWithoutDecorator {}
TS
  cat >"$PROBE_DIR/rule-breaker.html" <<'HTML'
<div *ngIf="label">{{ label }}</div>
<img src="logo.png" />
HTML
  cat >"$PROBE_DIR/rule-breaker.spec.ts" <<'TS'
export const firstItem = (items: string[]): string => items.at(0)!;
TS
}

# The lint output for the rule breakers, stored once: ESLint is slow and every rule shares it.
lint_probe_output() {
  write_lint_probe
  (cd frontend && npx eslint src/app/lint-probe) 2>&1
  rm -rf "$PROBE_DIR"
}

reports_rule() { # reports_rule <rule>
  printf '%s\n' "$LINT_OUTPUT" | grep -qF -- "$1"
}

spec_allows_non_null_assertion() {
  ! printf '%s\n' "$LINT_OUTPUT" | grep -A3 'rule-breaker.spec.ts' | grep -q 'no-non-null-assertion'
}

# What the schematics generate must already pass the rules, or every new component starts red.
generated_component_passes_lint() {
  fresh=frontend/src/app/fresh-probe
  [ ! -e "$fresh" ] || return 1
  ng generate component fresh-probe || return 1
  (cd frontend && npx eslint --max-warnings=0 src/app/fresh-probe)
  status=$?
  rm -rf "$fresh"
  return "$status"
}


task_5() {
  echo "1.5 ESLint and Prettier"
  check "eslint.config.js is tracked" tracked frontend/eslint.config.js
  check "package.json has a lint script" grep -q '"lint": "ng lint"' frontend/package.json
  check "angular.json has a lint target" grep -q '@angular-eslint/builder:lint' frontend/angular.json
  check ".prettierrc sets trailingComma" grep -q '"trailingComma": "all"' frontend/.prettierrc
  LINT_OUTPUT=$(lint_probe_output)
  for rule in no-console @typescript-eslint/no-explicit-any @typescript-eslint/no-floating-promises \
    @typescript-eslint/explicit-member-accessibility @angular-eslint/component-selector \
    @angular-eslint/directive-selector @angular-eslint/prefer-on-push-component-change-detection \
    @angular-eslint/prefer-signals @angular-eslint/template/prefer-control-flow \
    @angular-eslint/template/alt-text @typescript-eslint/no-extraneous-class; do
    check "reports $rule" reports_rule "$rule"
  done
  check "spec files may use non-null assertions" spec_allows_non_null_assertion
  check "a freshly generated component passes lint" generated_component_passes_lint
}

frontend_lint_is_clean() {
  ng lint
}

frontend_prettier_is_clean() {
  (cd frontend && npx prettier --check .)
}

# Negative controls: the two commands above must be able to fail.
lint_rejects_a_rule_breaker() {
  write_lint_probe
  ng lint
  status=$?
  rm -rf "$PROBE_DIR"
  [ "$status" -ne 0 ]
}

prettier_rejects_bad_formatting() {
  probe=frontend/src/app/prettier-probe.ts
  [ ! -e "$probe" ] || return 1
  printf 'export const value = "double quotes"\n' >"$probe"
  (cd frontend && npx prettier --check .)
  status=$?
  rm -f "$probe"
  [ "$status" -ne 0 ]
}

no_hello_world_left() {
  ! grep -rqi 'hello world' frontend/src
}

task_6() {
  echo "1.6 clean lint and Prettier, no Hello World"
  check "no Hello World is left in src" no_hello_world_left
  check "the frontend tests pass" frontend_tests_pass
  check "ng lint is clean" frontend_lint_is_clean
  check "prettier --check is clean" frontend_prettier_is_clean
  check "control: ng lint rejects a rule breaker" lint_rejects_a_rule_breaker
  check "control: prettier rejects bad formatting" prettier_rejects_bad_formatting
}

[ $# -gt 0 ] || set -- 1 2 3 4 5 6
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
