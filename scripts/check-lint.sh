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

[ $# -gt 0 ] || set -- 1 2 3
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
