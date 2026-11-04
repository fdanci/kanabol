#!/bin/sh
# Acceptance checks for the test structure (group 3 in the project's Tasks.md).
# Usage: scripts/check-tests.sh [task-number...]    (no arguments: every task)
# Exits non-zero if any check fails. The tests that need PostgreSQL need Docker running.

set -u
cd "$(git rev-parse --show-toplevel)" || exit 2

failures=0
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
test_projects="Kanabol.Domain.Tests Kanabol.Application.Tests Kanabol.Api.Tests"

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

not_in_file() { # not_in_file <text> <file>
  ! grep -qF -- "$1" "$2"
}

test_property_is() { # test_property_is <project> <property> <value>
  actual=$(dotnet msbuild "backend/tests/$1/$1.csproj" -getProperty:"$2" | tr -d '\r')
  [ "$actual" = "$3" ] || {
    echo "$1 $2 is '$actual'"
    return 1
  }
}

references_are() { # references_are <test project> <comma-separated names>
  actual=$(sed -n 's/.*ProjectReference Include=".*[\\/]\(Kanabol\.[A-Za-z.]*\)\.csproj".*/\1/p' \
    "backend/tests/$1/$1.csproj" | sort | paste -sd, -)
  [ "$actual" = "$2" ] || {
    echo "$1 references: '$actual'"
    return 1
  }
}

solution_builds() {
  dotnet build backend/Kanabol.sln -warnaserror --no-incremental --nologo -v q
}

formatting_is_clean() {
  dotnet format backend/Kanabol.sln --verify-no-changes
}

# The whole backend as it's staged, in a throwaway folder, to add a probe to.
backend_copy() { # backend_copy <folder>
  mkdir -p "$1"
  cp .editorconfig "$1/"
  git archive "$(git write-tree)" backend | tar -x -C "$1"
}

fluent_assertions_is_the_free_version() {
  in_file '<PackageVersion Include="FluentAssertions" Version="7.2.2" />' backend/Directory.Packages.props || return 1
  nuspec="$(dotnet nuget locals global-packages --list | sed 's/^[^:]*: //' | tr -d '\r')/fluentassertions/7.2.2/fluentassertions.nuspec"
  in_file 'Apache-2.0' "$nuspec"
}

# Each project has to run a passing test and report a failing one, using all three libraries.
project_runs_tests() { # project_runs_tests <project>
  copy="$scratch/runner-$1"
  backend_copy "$copy"
  cat >"$copy/backend/tests/$1/ProbeTests.cs" <<CS
namespace $1;

public sealed class ProbeTests
{
    [Fact]
    public void Answer_WhenAsked_ReturnsWhatItWasGiven()
    {
        var source = Substitute.For<IFormatProvider>();
        source.GetFormat(typeof(string)).Returns("given");

        source.GetFormat(typeof(string)).Should().Be("given");
    }

    [Fact]
    public void Answer_WhenWrong_FailsTheRun()
    {
        1.Should().Be(2);
    }
}
CS
  dotnet test "$copy/backend/tests/$1/$1.csproj" --nologo --filter 'FullyQualifiedName~ProbeTests' >"$scratch/run-$1.log" 2>&1
  grep -qE 'Failed: +1, +Passed: +1,' "$scratch/run-$1.log" || {
    tail -n 12 "$scratch/run-$1.log"
    return 1
  }
}

task_1() {
  echo "3.1 Test projects"
  check "tests/Directory.Build.props is tracked" tracked backend/tests/Directory.Build.props
  for project in $test_projects; do
    check "$project is tracked" tracked "backend/tests/$project/$project.csproj"
    check "the solution has $project under tests" \
      in_file "\"$project\", \"tests\\$project\\$project.csproj\"" backend/Kanabol.sln
    check "$project is a test project" test_property_is "$project" IsTestProject true
    check "$project isn't packable" test_property_is "$project" IsPackable false
    check "$project inherits the shared build props" test_property_is "$project" TreatWarningsAsErrors true
  done
  check "Domain.Tests references Domain" references_are Kanabol.Domain.Tests Kanabol.Domain
  check "Application.Tests references Application and Infrastructure" \
    references_are Kanabol.Application.Tests Kanabol.Application,Kanabol.Infrastructure
  check "Api.Tests references Api" references_are Kanabol.Api.Tests Kanabol.Api
  check "the solution still has no x64 configuration" not_in_file 'x64' backend/Kanabol.sln
  check "the solution still has no x86 configuration" not_in_file 'x86' backend/Kanabol.sln
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
  check "FluentAssertions is the Apache-licensed 7.2.2" fluent_assertions_is_the_free_version
  for project in $test_projects; do
    check "$project runs xUnit, NSubstitute and FluentAssertions" project_runs_tests "$project"
  done
}

if [ "$#" -eq 0 ]; then
  set -- 1
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
