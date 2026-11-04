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

architecture_tests_pass() {
  dotnet test backend/tests/Kanabol.Api.Tests/Kanabol.Api.Tests.csproj --nologo --filter 'FullyQualifiedName~ArchitectureTests' >"$scratch/architecture.log" 2>&1 || {
    tail -n 15 "$scratch/architecture.log"
    return 1
  }
  grep -qE 'Passed: +[1-9]' "$scratch/architecture.log"
}

# Breaks a rule in a copy of the backend. The architecture tests have to catch it.
architecture_tests_catch() { # architecture_tests_catch <name> <file in src> <C# source> [package for Domain]
  copy="$scratch/architecture-$1"
  backend_copy "$copy"
  mkdir -p "$(dirname "$copy/backend/src/$2")"
  printf '%s\n' "$3" >"$copy/backend/src/$2"
  if [ -n "${4:-}" ]; then
    sed -i "s#</Project>#<ItemGroup><PackageReference Include=\"$4\" /></ItemGroup></Project>#" \
      "$copy/backend/src/Kanabol.Domain/Kanabol.Domain.csproj"
  fi
  if dotnet test "$copy/backend/tests/Kanabol.Api.Tests/Kanabol.Api.Tests.csproj" --nologo -p:TreatWarningsAsErrors=false \
    --filter 'FullyQualifiedName~ArchitectureTests' >"$scratch/caught-$1.log" 2>&1; then
    echo "the tests passed with the rule broken"
    return 1
  fi
  grep -qE 'Failed: +[1-9]' "$scratch/caught-$1.log" || {
    tail -n 12 "$scratch/caught-$1.log"
    return 1
  }
}

task_6() {
  echo "3.6 Architecture tests"
  check "the architecture tests pass" architecture_tests_pass
  check "EF in Domain is caught" architecture_tests_catch domain-ef Kanabol.Domain/Probe.cs \
    'namespace Kanabol.Domain; public sealed class Probe(Microsoft.EntityFrameworkCore.DbContext context) { public object Context { get; } = context; }' \
    Microsoft.EntityFrameworkCore
  check "Infrastructure in Application is caught" architecture_tests_catch application-infrastructure Kanabol.Application/Probe.cs \
    'namespace Kanabol.Infrastructure.Probing { public sealed class Thing; } namespace Kanabol.Application { public sealed class Probe { public Kanabol.Infrastructure.Probing.Thing? Used { get; set; } } }'
}

docker_is_running() {
  docker_cli=$(command -v docker || echo '/c/Program Files/Docker/Docker/resources/bin/docker.exe')
  "$docker_cli" info --format '{{.ServerVersion}}'
}

tests_pass() { # tests_pass <project> <filter> <minimum passed>
  log="$scratch/tests-$1.log"
  dotnet test "backend/tests/$1/$1.csproj" --nologo --filter "$2" >"$log" 2>&1 || {
    tail -n 25 "$log"
    return 1
  }
  passed=$(sed -n 's/.*Passed: *\([0-9]*\),.*/\1/p' "$log" | tail -n 1)
  [ "${passed:-0}" -ge "$3" ] || {
    echo "only ${passed:-0} tests passed, expected at least $3"
    return 1
  }
}

# With the reset turned off, the tests that depend on it have to notice.
tests_notice_a_broken_reset() {
  copy="$scratch/broken-reset"
  backend_copy "$copy"
  sed -i 's/await _respawner!\.ResetAsync(connection);/await Task.CompletedTask;/' \
    "$copy/backend/tests/Shared/Database/PostgresFixture.cs"
  if grep -q '_respawner!.ResetAsync' "$copy/backend/tests/Shared/Database/PostgresFixture.cs"; then
    echo "the reset wasn't turned off"
    return 1
  fi
  if dotnet test "$copy/backend/tests/Kanabol.Application.Tests/Kanabol.Application.Tests.csproj" --nologo \
    -p:TreatWarningsAsErrors=false --filter 'FullyQualifiedName~PostgresFixtureTests' >"$scratch/broken-reset.log" 2>&1; then
    echo "the tests passed with the reset turned off"
    return 1
  fi
  grep -qE 'Failed: +[1-9]' "$scratch/broken-reset.log" || {
    tail -n 12 "$scratch/broken-reset.log"
    return 1
  }
}

task_2() {
  echo "3.2 PostgreSQL with Testcontainers and Respawn"
  check "Docker is running" docker_is_running
  check "the fixture is tracked" tracked backend/tests/Shared/Database/PostgresFixture.cs
  check "it uses the image compose uses" in_file 'postgres:17-alpine' backend/tests/Shared/Database/PostgresFixture.cs
  check "the fixture's tests pass on a real PostgreSQL" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~PostgresFixtureTests' 5
  check "they notice when the reset does nothing" tests_notice_a_broken_reset
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
}

tests_catch_a_wrong_signing_key() {
  copy="$scratch/wrong-key"
  backend_copy "$copy"
  sed -i 's/Encoding.UTF8.GetBytes(jwt.Secret)/Encoding.UTF8.GetBytes(new string(\x27y\x27, 48))/' \
    "$copy/backend/tests/Kanabol.Api.Tests/Support/ApiFactory.cs"
  if dotnet test "$copy/backend/tests/Kanabol.Api.Tests/Kanabol.Api.Tests.csproj" --nologo -p:TreatWarningsAsErrors=false \
    --filter 'FullyQualifiedName~ApiFactoryTests' >"$scratch/wrong-key.log" 2>&1; then
    echo "the tests passed with tokens signed by another key"
    return 1
  fi
  grep -qE 'Failed: +[1-9]' "$scratch/wrong-key.log" || {
    tail -n 12 "$scratch/wrong-key.log"
    return 1
  }
}

task_3() {
  echo "3.3 WebApplicationFactory on the container, with test tokens"
  check "the factory is tracked" tracked backend/tests/Kanabol.Api.Tests/Support/ApiFactory.cs
  check "the factory's tests pass" tests_pass Kanabol.Api.Tests 'FullyQualifiedName~ApiFactoryTests' 6
  check "they catch tokens the API's settings wouldn't accept" tests_catch_a_wrong_signing_key
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
}

task_4() {
  echo "3.4 Test data builders"
  for builder in UserBuilder ProjectBuilder TaskItemBuilder; do
    check "$builder is tracked" tracked "backend/tests/Shared/Builders/$builder.cs"
  done
  check "the builders work in the domain tests" tests_pass Kanabol.Domain.Tests 'FullyQualifiedName~TestDataBuilderTests' 6
  check "what they build fits the real schema" tests_pass Kanabol.Application.Tests 'FullyQualifiedName~TestDataPersistenceTests' 1
  check "the API tests can use them too" builders_compile_in_the_api_tests
  check "the solution builds with warnings as errors" solution_builds
  check "dotnet format is clean" formatting_is_clean
}

builders_compile_in_the_api_tests() {
  dotnet msbuild backend/tests/Kanabol.Api.Tests/Kanabol.Api.Tests.csproj -getItem:Compile | grep -q 'UserBuilder.cs'
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 6
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
