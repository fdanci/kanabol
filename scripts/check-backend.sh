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

# A database of this run's own, dropped at the end. Exports its connection string for the
# probes and for the API.
need_database() {
  [ -n "${KANABOL_CHECK_CONNECTION:-}" ] && return 0
  base_connection=${KANABOL_CHECK_DB:-$(dotnet user-secrets list --id kanabol-api 2>/dev/null |
    tr -d '\r' | sed -n 's/^ConnectionStrings:Default = //p')}
  if [ -z "$base_connection" ]; then
    echo "No database: set KANABOL_CHECK_DB or the API's ConnectionStrings:Default user secret." >&2
    exit 2
  fi
  admin_connection=$(printf '%s' "$base_connection" | sed 's/Database=[^;]*/Database=postgres/')
  sql "$admin_connection" "CREATE DATABASE $check_db" >/dev/null || exit 2
  KANABOL_CHECK_CONNECTION=$(printf '%s' "$base_connection" | sed "s/Database=[^;]*/Database=$check_db/")
  ConnectionStrings__Default=$KANABOL_CHECK_CONNECTION
  export KANABOL_CHECK_CONNECTION ConnectionStrings__Default
}

# Run a statement and print its rows, with columns separated by |. Built once, on first use.
sql() { # sql <connection> <statement>
  sql_dll="$scratch/sql/bin/Debug/net10.0/sql.dll"
  if [ ! -f "$sql_dll" ]; then
    mkdir -p "$scratch/sql"
    cat >"$scratch/sql/sql.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" Version="10.0.*" />
  </ItemGroup>
</Project>
XML
    cat >"$scratch/sql/Program.cs" <<'CS'
await using var connection = new Npgsql.NpgsqlConnection(args[0]);
await connection.OpenAsync();
await using var command = new Npgsql.NpgsqlCommand(args[1], connection);
await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i)?.ToString());
    Console.WriteLine(string.Join('|', values));
}
CS
    dotnet build "$scratch/sql/sql.csproj" --nologo -v q >"$scratch/sql/build.log" 2>&1 || {
      cat "$scratch/sql/build.log" >&2
      return 1
    }
  fi
  dotnet "$sql_dll" "$1" "$2"
}

cleanup() {
  [ -n "$api_pid" ] && stop_api
  if [ -n "${KANABOL_CHECK_CONNECTION:-}" ]; then
    sql "$admin_connection" "DROP DATABASE IF EXISTS $check_db WITH (FORCE)" >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

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
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>$probe_refs</ItemGroup>
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

results_behave() {
  probe Kanabol.Application <<'CS'
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

Result<int> success = 5;
Check.That(success.IsSuccess && !success.IsFailure, "a value converts to a success");
Check.That(success.Value == 5, "a success has its value");
Check.Throws<InvalidOperationException>(() => _ = success.Error, "a success has no error");

Result<int> failure = Error.NotFound("Task.NotFound", "No such task.");
Check.That(failure.IsFailure && !failure.IsSuccess, "an error converts to a failure");
Check.That(failure.Error.Code == "Task.NotFound", "a failure keeps the code");
Check.That(failure.Error.Message == "No such task.", "a failure keeps the message");
Check.That(failure.Error.Type == ErrorType.NotFound, "a failure keeps the type");
Check.Throws<InvalidOperationException>(() => _ = failure.Value, "a failure has no value");

Check.That(Result.Success().IsSuccess, "Result.Success succeeds");
var plain = Result.Failure(Error.Forbidden("Project.Forbidden", "Not a member."));
Check.That(plain.IsFailure && plain.Error.Type == ErrorType.Forbidden, "Result.Failure fails");

Check.That(Error.Conflict("c", "m").Type == ErrorType.Conflict, "Conflict");
Check.That(Error.Unauthorized("c", "m").Type == ErrorType.Unauthorized, "Unauthorized");
Check.That(Error.Failure("c", "m").Type == ErrorType.Failure, "Failure");

var fields = new Dictionary<string, string[]> { ["Title"] = ["Required."] };
var invalid = Error.Validation(fields);
Check.That(invalid.Type == ErrorType.Validation, "a validation error has the Validation type");
Check.That(invalid.Errors["Title"].Single() == "Required.", "a validation error keeps its fields");

string[] expected = ["Validation", "NotFound", "Conflict", "Forbidden", "Unauthorized", "Failure"];
Check.That(Enum.GetNames<ErrorType>().Order().SequenceEqual(expected.Order()), "ErrorType has the spec's six values");

Check.That(Unit.Value == default, "Unit has one value");

// The shape of a handler in the spec has to compile and behave.
var handler = new PingCommandHandler();
var denied = await handler.Handle(new PingCommand(false), CancellationToken.None);
Check.That(denied.Error.Type == ErrorType.Forbidden, "a handler can return a failed check's error");
var allowed = await handler.Handle(new PingCommand(true), CancellationToken.None);
Check.That(allowed.IsSuccess, "a handler can return Unit.Value");
var answer = await new GetPingQueryHandler().Handle(new GetPingQuery(), CancellationToken.None);
Check.That(answer.Value == "pong", "a query handler returns its value");

internal sealed record PingCommand(bool Allowed) : ICommand<Unit>;

internal sealed class PingCommandHandler : ICommandHandler<PingCommand, Unit>
{
    public async Task<Result<Unit>> Handle(PingCommand command, CancellationToken ct)
    {
        await Task.Yield();
        var access = command.Allowed ? Result.Success() : Result.Failure(Error.Forbidden("c", "m"));
        if (access.IsFailure)
        {
            return access.Error;
        }

        return Unit.Value;
    }
}

internal sealed record GetPingQuery : IQuery<string>;

internal sealed class GetPingQueryHandler : IQueryHandler<GetPingQuery, string>
{
    public Task<Result<string>> Handle(GetPingQuery query, CancellationToken ct) =>
        Task.FromResult<Result<string>>("pong");
}
CS
}

task_4() {
  echo "2.4 Application messaging and results"
  check "Result, Error, ErrorType, Unit and the handler interfaces behave" results_behave
}

application_ports_fit() {
  probe Kanabol.Application <<'CS'
using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;

// Implementing each interface proves its shape at compile time.
IClock clock = new FixedClock();
Check.That(clock.UtcNow == DateTimeOffset.UnixEpoch, "IClock gives the time");

ICurrentUser user = new Visitor();
Check.That(!user.IsAuthenticated && !user.HasPermission("users.manage"), "ICurrentUser answers");

IProjectAccess access = new NoAccess();
var denied = await access.EnsureMemberAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
Check.That(denied.IsFailure, "IProjectAccess returns a Result");

IFileStorage storage = new NullStorage();
var stored = await storage.SaveAsync(Stream.Null, "a.txt", CancellationToken.None);
Check.That(stored == new StoredFile("key", 0, "text/plain"), "IFileStorage returns a StoredFile");

IBoardNotifier notifier = new SilentNotifier();
await notifier.NotifyProjectAsync(Guid.NewGuid(), "TaskMoved", new { }, CancellationToken.None);
await notifier.NotifyUserAsync(Guid.NewGuid(), "NotificationCreated", new { }, CancellationToken.None);

var save = typeof(IAppDbContext).GetMethod("SaveChangesAsync", [typeof(CancellationToken)]);
Check.That(typeof(IAppDbContext).IsInterface, "IAppDbContext is an interface");
Check.That(save?.ReturnType == typeof(Task<int>), "IAppDbContext can save");

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
}

internal sealed class Visitor : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid Id => throw new InvalidOperationException();

    public bool HasPermission(string permission) => false;
}

internal sealed class NoAccess : IProjectAccess
{
    public Task<Result> EnsureMemberAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        Task.FromResult(Result.Failure(Error.Forbidden("c", "m")));

    public Task<Result> EnsureOwnerAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        Task.FromResult(Result.Failure(Error.Forbidden("c", "m")));
}

internal sealed class NullStorage : IFileStorage
{
    public Task<StoredFile> SaveAsync(Stream content, string fileName, CancellationToken ct) =>
        Task.FromResult(new StoredFile("key", 0, "text/plain"));

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult(Stream.Null);

    public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class SilentNotifier : IBoardNotifier
{
    public Task NotifyProjectAsync(Guid projectId, string eventName, object payload, CancellationToken ct) =>
        Task.CompletedTask;

    public Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct) =>
        Task.CompletedTask;
}
CS
}

task_5() {
  echo "2.5 Application interfaces"
  check "IAppDbContext, ICurrentUser, IClock, IFileStorage, IProjectAccess and IBoardNotifier fit" application_ports_fit
}

app_db_context_is_wired() {
  probe Kanabol.Infrastructure <<'CS'
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

var connection = Environment.GetEnvironmentVariable("KANABOL_CHECK_CONNECTION");
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection })
    .Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddInfrastructure(configuration);
services.AddScoped<ICurrentUser, Nobody>();
await using var provider = services.BuildServiceProvider(validateScopes: true);
await using var scope = provider.CreateAsyncScope();

var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
Check.That(db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL", "the context uses Npgsql");
Check.That(await db.Database.CanConnectAsync(), "the context reaches PostgreSQL");
Check.That(ReferenceEquals(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), db), "IAppDbContext is the context");
Check.That(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.Offset == TimeSpan.Zero, "IClock is in UTC");

var interceptors = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!;
Check.That(interceptors.OfType<AuditingInterceptor>().Any(), "the context has the auditing interceptor");
Check.That(interceptors.OfType<DomainEventsInterceptor>().Any(), "the context has the domain events interceptor");
var extensions = db.GetService<IDbContextOptions>().Extensions.Select(e => e.GetType().Name);
Check.That(extensions.Contains("NamingConventionsOptionsExtension"), "the context uses the snake_case convention");

internal sealed class Nobody : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid Id => throw new InvalidOperationException();

    public bool HasPermission(string permission) => false;
}
CS
}

interceptors_behave() {
  probe Kanabol.Infrastructure <<'CS'
using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Common;
using Kanabol.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("KANABOL_CHECK_CONNECTION")!;
var clock = new MovableClock();
var user = new SwitchableUser();
var recorder = new Recorder(connection);

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IClock>(clock);
services.AddSingleton<ICurrentUser>(user);
services.AddSingleton(recorder);
services.AddScoped<IDomainEventHandler<ItemNamed>, ThrowingHandler>();
services.AddScoped<IDomainEventHandler<ItemNamed>, RecordingHandler>();
services.AddScoped<AuditingInterceptor>();
services.AddScoped<DomainEventsInterceptor>();
await using var provider = services.BuildServiceProvider();

ProbeContext NewContext(IServiceProvider scoped) => new(new DbContextOptionsBuilder<ProbeContext>()
    .UseNpgsql(connection)
    .UseSnakeCaseNamingConvention()
    .AddInterceptors(scoped.GetRequiredService<AuditingInterceptor>(), scoped.GetRequiredService<DomainEventsInterceptor>())
    .Options);

await using (var scope = provider.CreateAsyncScope())
{
    await using var setup = NewContext(scope.ServiceProvider);
    await setup.Database.ExecuteSqlRawAsync("CREATE TABLE probe_items (id uuid PRIMARY KEY, name text NOT NULL, created_at timestamptz NOT NULL, created_by_id uuid, updated_at timestamptz, updated_by_id uuid)");
}

var creator = Guid.CreateVersion7();
var editor = Guid.CreateVersion7();
var createdAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
var updatedAt = createdAt.AddHours(1);

var item = new Item("first");
await using (var scope = provider.CreateAsyncScope())
{
    await using var db = NewContext(scope.ServiceProvider);
    clock.UtcNow = createdAt;
    user.Signed = creator;
    db.Items.Add(item);
    await db.SaveChangesAsync();

    Check.That(item.CreatedAt == createdAt, "an insert stamps CreatedAt from IClock");
    Check.That(item.CreatedById == creator, "an insert stamps CreatedById from ICurrentUser");
    Check.That(item.UpdatedAt is null && item.UpdatedById is null, "an insert leaves the update stamps empty");
    Check.That(recorder.Seen.SequenceEqual(["first"]), "the handler saw the event once");
    Check.That(recorder.RowsVisibleFromAnotherConnection.SequenceEqual([1L]), "the event is published after the commit");
    Check.That(item.DomainEvents.Count == 0, "published events leave the entity");

    await db.SaveChangesAsync();
    Check.That(recorder.Seen.Count == 1, "saving again doesn't publish again");

    clock.UtcNow = updatedAt;
    user.Signed = editor;
    item.Rename("second");
    item.CreatedAt = updatedAt;
    item.CreatedById = editor;
    db.SaveChanges();
    Check.That(recorder.Seen.SequenceEqual(["first", "second"]), "synchronous saves publish too");
    Check.That(item.UpdatedAt == updatedAt, "an update stamps UpdatedAt");
    Check.That(item.UpdatedById == editor, "an update stamps UpdatedById");
}

await using (var scope = provider.CreateAsyncScope())
{
    await using var db = NewContext(scope.ServiceProvider);
    var stored = await db.Items.SingleAsync(i => i.Id == item.Id);
    Check.That(stored.CreatedAt == createdAt, "an update can't rewrite CreatedAt");
    Check.That(stored.CreatedById == creator, "an update can't rewrite CreatedById");
    Check.That(stored.UpdatedAt == updatedAt, "the update stamp reaches the database");
}

await using (var scope = provider.CreateAsyncScope())
{
    await using var db = NewContext(scope.ServiceProvider);
    user.Signed = null;
    var anonymous = new Item("anonymous");
    db.Items.Add(anonymous);
    await db.SaveChangesAsync();
    Check.That(anonymous.CreatedById is null, "without a user CreatedById stays empty");
}

var seenBefore = recorder.Seen.Count;
await using (var scope = provider.CreateAsyncScope())
{
    await using var db = NewContext(scope.ServiceProvider);
    var duplicate = new Item("duplicate", item.Id);
    db.Items.Add(duplicate);
    Check.Throws<DbUpdateException>(() => db.SaveChanges(), "a duplicate id fails");
    Check.That(recorder.Seen.Count == seenBefore, "a failed save publishes nothing");
    Check.That(duplicate.DomainEvents.Count == 1, "a failed save keeps the events for a retry");
}

internal sealed record ItemNamed(string Name) : IDomainEvent;

internal sealed class Item : AuditableEntity
{
    public Item(string name, Guid? id = null)
    {
        Id = id ?? Id;
        Rename(name);
    }

    private Item()
    {
    }

    public string Name { get; private set; } = "";

    public void Rename(string name)
    {
        Name = name;
        Raise(new ItemNamed(name));
    }
}

internal sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Item>().ToTable("probe_items").Ignore(i => i.DomainEvents);
}

internal sealed class MovableClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}

internal sealed class SwitchableUser : ICurrentUser
{
    public Guid? Signed { get; set; }

    public bool IsAuthenticated => Signed is not null;

    public Guid Id => Signed ?? throw new InvalidOperationException();

    public bool HasPermission(string permission) => false;
}

internal sealed class Recorder(string connection)
{
    public List<string> Seen { get; } = [];

    public List<long> RowsVisibleFromAnotherConnection { get; } = [];

    public async Task Record(string name)
    {
        Seen.Add(name);
        await using var other = new NpgsqlConnection(connection);
        await other.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM probe_items", other);
        RowsVisibleFromAnotherConnection.Add((long)(await count.ExecuteScalarAsync())!);
    }
}

// A failing handler can't undo what's committed, so it mustn't stop the next one or fail the save.
internal sealed class ThrowingHandler : IDomainEventHandler<ItemNamed>
{
    public Task Handle(ItemNamed domainEvent, CancellationToken ct) =>
        throw new InvalidOperationException("Handler failure on purpose.");
}

internal sealed class RecordingHandler(Recorder recorder) : IDomainEventHandler<ItemNamed>
{
    public Task Handle(ItemNamed domainEvent, CancellationToken ct) => recorder.Record(domainEvent.Name);
}
CS
}

task_6() {
  echo "2.6 AppDbContext and interceptors"
  need_database
  check "AppDbContext is wired to Npgsql, snake_case and the interceptors" app_db_context_is_wired
  check "the interceptors audit and publish, on a real PostgreSQL" interceptors_behave
  check "Domain still has no packages" no_packages Kanabol.Domain
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
