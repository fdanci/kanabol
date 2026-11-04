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
api_port=5290
api_pid=

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

# The whole backend as it's staged, in a throwaway folder, to add a probe to.
backend_copy() { # backend_copy <folder>
  mkdir -p "$1"
  cp .editorconfig "$1/"
  git archive "$(git write-tree)" backend | tar -x -C "$1"
}

start_api() { # start_api [project-folder]; sets API_URL
  api_port=$((api_port + 1))
  API_URL="http://127.0.0.1:$api_port"
  api_log="$scratch/api-$api_port.log"
  (cd "${1:-$api_dir}" && exec dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls "$API_URL") >"$api_log" 2>&1 &
  api_pid=$!
  attempts=0
  until curl -s -o /dev/null "$API_URL/health"; do
    attempts=$((attempts + 1))
    if [ "$attempts" -gt 120 ] || ! kill -0 "$api_pid" 2>/dev/null; then
      cat "$api_log"
      return 1
    fi
    sleep 0.5
  done
}

stop_api() {
  kill "$api_pid" 2>/dev/null
  wait "$api_pid" 2>/dev/null
  api_pid=
  return 0
}

returns() { # returns <status> <path>
  actual=$(curl -s -o /dev/null -w '%{http_code}' "$API_URL$2")
  [ "$actual" = "$1" ] || {
    echo "GET $2 returned $actual, expected $1"
    return 1
  }
}

body_has() { # body_has <text> <path>
  curl -s "$API_URL$2" | grep -qF -- "$1"
}

build_backend() {
  dotnet build backend/Kanabol.sln --nologo -v q >"$scratch/build.log" 2>&1 || {
    cat "$scratch/build.log"
    return 1
  }
}

# Run C# from stdin in a throwaway console project that references the given backend projects.
# Fails if it doesn't compile, or if a Check.That in it fails.
probe() { # probe <project...>
  probe_dir=$(mktemp -d)
  probe_refs=
  for project in "$@"; do
    probe_refs="$probe_refs<ProjectReference Include=\"$root/backend/src/$project/$project.csproj\" />"
  done
  cat >"$probe_dir/probe.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>$probe_refs</ItemGroup>
  <ItemGroup>${probe_packages:-}</ItemGroup>
</Project>
XML
  cat >"$probe_dir/Check.cs" <<'CS'
internal static class Check
{
    public static void That(bool condition, string what)
    {
        if (!condition)
        {
            Console.WriteLine($"not true: {what}");
            Environment.ExitCode = 1;
        }
    }

    public static void Throws<TException>(Action action, string what)
        where TException : Exception
    {
        try
        {
            action();
            That(false, what);
        }
        catch (TException)
        {
        }
    }
}
CS
  cat >"$probe_dir/Program.cs"
  (cd "$probe_dir" && dotnet run --nologo -v q)
  probe_status=$?
  rm -rf "$probe_dir"
  return "$probe_status"
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

write_probe_controller() { # write_probe_controller <api-project-folder>
  mkdir -p "$1/Controllers"
  cat >"$1/Controllers/ProbeController.cs" <<'CS'
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

[ApiController]
[Route("api/probe")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { DueDate = "soon" });
}
CS
}

task_2() {
  echo "2.2 Controllers and /health instead of Hello World"
  check "Hello World is gone from Program.cs" not_in_file 'Hello World' "$api_dir/Program.cs"
  check "the backend builds" build_backend
  check "the API starts" start_api
  check "/health returns 200" returns 200 /health
  check "/ no longer answers" returns 404 /
  stop_api
  copy="$scratch/controllers"
  backend_copy "$copy"
  write_probe_controller "$copy/$api_dir"
  check "a controller builds in a copy of the API" dotnet build "$copy/$api_dir/Kanabol.Api.csproj" --nologo -v q
  check "the copy starts" start_api "$copy/$api_dir"
  check "controllers are routed" returns 200 /api/probe
  check "JSON comes back in camelCase" body_has '"dueDate"' /api/probe
  stop_api
}

domain_base_behaves() {
  probe Kanabol.Domain <<'CS'
using System.Reflection;
using Kanabol.Domain.Common;

var first = new Sample();
var second = new Sample();
Check.That(first.Id.Version == 7, "the id is a UUID v7");
Check.That(first.Id != second.Id, "each entity gets its own id");
Check.That(first.DomainEvents.Count == 0, "a new entity has no events");

var raised = new Happened();
first.Emit(raised);
Check.That(first.DomainEvents.Single() == raised, "Raise keeps the event");
first.ClearDomainEvents();
Check.That(first.DomainEvents.Count == 0, "ClearDomainEvents empties the list");

var idSetter = typeof(Entity).GetProperty(nameof(Entity.Id))!.SetMethod!;
Check.That(idSetter.IsFamily, "only subclasses can set the id");

Check.That(typeof(AuditableEntity).IsAbstract, "AuditableEntity is abstract");
Check.That(typeof(AuditableEntity).BaseType == typeof(Entity), "AuditableEntity extends Entity");
Check.That(typeof(Entity).IsAbstract, "Entity is abstract");
void HasType(Type owner, string name, Type type) =>
    Check.That(owner.GetProperty(name)?.PropertyType == type, $"{owner.Name}.{name} is {type}");
HasType(typeof(AuditableEntity), "CreatedAt", typeof(DateTimeOffset));
HasType(typeof(AuditableEntity), "CreatedById", typeof(Guid?));
HasType(typeof(AuditableEntity), "UpdatedAt", typeof(DateTimeOffset?));
HasType(typeof(AuditableEntity), "UpdatedById", typeof(Guid?));
HasType(typeof(IArchivable), "ArchivedAt", typeof(DateTimeOffset?));
HasType(typeof(IArchivable), "ArchivedById", typeof(Guid?));
Check.That(typeof(IArchivable).IsInterface, "IArchivable is an interface");
Check.That(typeof(IDomainEvent).IsInterface, "IDomainEvent is an interface");

internal sealed record Happened : IDomainEvent;

internal sealed class Sample : AuditableEntity
{
    public void Emit(IDomainEvent domainEvent) => Raise(domainEvent);
}
CS
}

task_3() {
  echo "2.3 Domain base types"
  check "Entity, AuditableEntity, IArchivable and IDomainEvent behave" domain_base_behaves
  check "Domain still has no packages" no_packages Kanabol.Domain
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3
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
