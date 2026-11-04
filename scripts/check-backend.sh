#!/bin/sh
# Acceptance checks for the backend structure (group 2 in the project's Tasks.md).
# The test projects only arrive in group 3, so these run the real code from throwaway
# console projects, and start the real API against a real PostgreSQL.
#
# Usage: scripts/check-backend.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails.
#
# Needs a PostgreSQL 17 whose user may create databases. The connection string comes from
# KANABOL_CHECK_DB, or else from the API's user secrets (ConnectionStrings:Default). Each
# run works in its own database and drops it at the end.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

root=$(pwd -W 2>/dev/null || pwd)
failures=0
scratch=$(mktemp -d)
check_db="kanabol_check_$$"
api_dir=backend/src/Kanabol.Api

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

not_in_file() { # not_in_file <text> <file>
  ! grep -qF -- "$1" "$2"
}

references_are() { # references_are <project> <comma-separated names, or empty>
  actual=$(sed -n 's/.*ProjectReference Include=".*[\\/]\(Kanabol\.[A-Za-z]*\)\.csproj".*/\1/p' \
    "backend/src/$1/$1.csproj" | sort | paste -sd, -)
  [ "$actual" = "$2" ] || {
    echo "$1 references: '$actual', expected: '$2'"
    return 1
  }
}

no_packages() { # no_packages <project>
  not_in_file 'PackageReference' "backend/src/$1/$1.csproj"
}

property_is() { # property_is <project> <property> <value>
  [ "$(dotnet msbuild "backend/src/$1/$1.csproj" -getProperty:"$2" | tr -d '\r')" = "$3" ]
}

solution_builds() {
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo
}

formatting_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
}

task_1() {
  echo "2.1 Four projects under backend/src"
  for project in Kanabol.Api Kanabol.Domain Kanabol.Application Kanabol.Infrastructure; do
    check "$project is tracked under src" tracked "backend/src/$project/$project.csproj"
    check "the solution has $project under src" \
      grep -qF "\"$project\", \"src\\$project\\$project.csproj\"" backend/Kanabol.sln
    check "$project inherits the shared build props" property_is "$project" Nullable enable
  done
  check "the Api project left backend/" absent backend/Kanabol.Api/Kanabol.Api.csproj
  check "the old Api folder is gone" absent backend/Kanabol.Api
  check "Domain references no project" references_are Kanabol.Domain ""
  check "Domain has no packages" no_packages Kanabol.Domain
  check "Application references only Domain" references_are Kanabol.Application Kanabol.Domain
  check "Infrastructure references only Application" \
    references_are Kanabol.Infrastructure Kanabol.Application
  check "Api references Application and Infrastructure" \
    references_are Kanabol.Api Kanabol.Application,Kanabol.Infrastructure
  check "the solution has no x64 configuration" not_in_file 'x64' backend/Kanabol.sln
  check "the solution has no x86 configuration" not_in_file 'x86' backend/Kanabol.sln
  check "the solution keeps Debug|Any CPU" grep -qF 'Debug|Any CPU = Debug|Any CPU' backend/Kanabol.sln
  check "the solution keeps Release|Any CPU" grep -qF 'Release|Any CPU = Release|Any CPU' backend/Kanabol.sln
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
  check "the backend notes run the moved project" grep -qF 'dotnet run --project src/Kanabol.Api' backend/CLAUDE.md
  check "the lint checks follow the move" not_in_file 'backend/Kanabol.Api' scripts/check-lint.sh
  check "the foundation checks follow the move" not_in_file 'backend/Kanabol.Api' scripts/check-foundation.sh
}

if [ "$#" -eq 0 ]; then
  set -- 1
fi

for task in "$@"; do
  "task_$task"
done

rm -rf "$scratch"

echo
if [ "$failures" -gt 0 ]; then
  echo "$failures check(s) failed."
  exit 1
fi
echo "All checks passed."
