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

[ $# -gt 0 ] || set -- 1
for task in "$@"; do
  "task_$task"
done

if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed"
  exit 1
fi
echo "all checks passed"
