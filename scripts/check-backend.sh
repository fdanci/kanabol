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
    sql "$admin_connection" "SELECT datname FROM pg_database WHERE datname LIKE '$check_db%'" | tr -d '\r' |
      while IFS= read -r database; do
        sql "$admin_connection" "DROP DATABASE IF EXISTS $database WITH (FORCE)" >/dev/null
      done
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

# What the API needs to start, none of it real. Later settings in the environment win.
api_env() {
  need_database
  Jwt__Secret=${Jwt__Secret:-only-for-these-checks-and-long-enough-to-pass}
  Jwt__Issuer=${Jwt__Issuer:-kanabol-checks}
  Jwt__Audience=${Jwt__Audience:-kanabol-checks}
  Storage__Root=${Storage__Root:-$scratch/attachments}
  Database__MigrateOnStartup=${Database__MigrateOnStartup:-true}
  export Jwt__Secret Jwt__Issuer Jwt__Audience Storage__Root Database__MigrateOnStartup
}

start_api() { # start_api [project-folder]; sets API_URL
  api_env
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
  case " $* " in
  *" Kanabol.Infrastructure "*)
    cat >"$probe_dir/ProbeDatabase.cs" <<'CS'
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure;
using Kanabol.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// A database of the probe's own, with the real AddInfrastructure. Dropped when disposed.
internal sealed class ProbeDatabase(ServiceProvider services) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public static async Task<ProbeDatabase> Create(string name, bool migrate = false)
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("KANABOL_CHECK_CONNECTION"));
        connection.Database = $"{connection.Database}_{name}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection.ConnectionString })
            .Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddInfrastructure(configuration);
        collection.AddScoped<ICurrentUser, ProbeVisitor>();

        var database = new ProbeDatabase(collection.BuildServiceProvider());
        await database.Run(async db =>
        {
            await db.Database.EnsureDeletedAsync();
            if (migrate)
            {
                await db.Database.MigrateAsync();
            }
            else
            {
                await db.Database.EnsureCreatedAsync();
            }
        });
        return database;
    }

    public string CreateScript()
    {
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GenerateCreateScript();
    }

    public async Task Run(Func<AppDbContext, Task> action)
    {
        await using var scope = services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await Run(db => db.Database.EnsureDeletedAsync());
        await services.DisposeAsync();
    }
}

internal sealed class ProbeVisitor : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid Id => throw new InvalidOperationException();

    public bool HasPermission(string permission) => false;
}
CS
    ;;
  esac
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

users_and_roles_schema_is_right() {
  probe Kanabol.Infrastructure <<'CS'
await using var database = await ProbeDatabase.Create("schema_users");
var script = database.CreateScript();
void Has(string text) => Check.That(script.Contains(text), $"the schema has: {text}");

Has("CREATE TABLE users (");
Has("CREATE TABLE roles (");
Has("CREATE TABLE permissions (");
Has("CREATE TABLE role_permissions (");
Has("CREATE TABLE refresh_tokens (");
Has("CONSTRAINT pk_users PRIMARY KEY (id)");
Has("CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id)");
Has("email character varying(320) NOT NULL");
Has("display_name character varying(200) NOT NULL");
Has("password_hash character varying(256) NOT NULL");
Has("is_active boolean NOT NULL");
Has("is_system boolean NOT NULL");
Has("must_change_password boolean NOT NULL");
Has("locked_until timestamp with time zone");
Has("created_at timestamp with time zone NOT NULL");
Has("created_by_id uuid");
Has("updated_at timestamp with time zone");
Has("expires_at timestamp with time zone NOT NULL");
Has("revoked_at timestamp with time zone");
Has("token_hash character varying(64) NOT NULL");
Has("family_id uuid NOT NULL");
Has("CREATE UNIQUE INDEX ux_users_email ON users (email)");
Has("CREATE UNIQUE INDEX ux_roles_name ON roles (name)");
Has("CREATE UNIQUE INDEX ux_permissions_name ON permissions (name)");
Has("CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON refresh_tokens (token_hash)");
Has("CONSTRAINT fk_users_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE RESTRICT");
Has("CREATE INDEX ix_refresh_tokens_family_id ON refresh_tokens (family_id)");
Check.That(!script.Contains("domain_events"), "domain events stay out of the schema");
CS
}

users_and_roles_behave() {
  probe Kanabol.Infrastructure <<'CS'
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

await using var database = await ProbeDatabase.Create("users");

var permission = new Permission("users.manage");
var role = new Role("Manager", isSystem: false);
role.Grant(permission.Id);
var user = new User("ana@example.com", "Ana", "hash", role.Id);
var token = new RefreshToken(user.Id, Guid.CreateVersion7(), new string('a', 64), DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow);

await database.Run(async db =>
{
    db.Permissions.Add(permission);
    db.Roles.Add(role);
    db.Users.Add(user);
    db.RefreshTokens.Add(token);
    await db.SaveChangesAsync();
});

Check.That(user.CreatedAt != default, "users get audit stamps");
Check.That(role.CreatedAt != default, "roles get audit stamps");

await database.Run(async db =>
{
    var stored = await db.Users.Include(u => u.Role).SingleAsync(u => u.Id == user.Id);
    Check.That(stored.Id.Version == 7, "ids stored are UUID v7");
    Check.That(stored.Email == "ana@example.com", "the email is stored");
    Check.That(stored.DisplayName == "Ana", "the display name is stored");
    Check.That(stored.PasswordHash == "hash", "the password hash is stored");
    Check.That(stored.IsActive, "new users are active");
    Check.That(!stored.MustChangePassword, "the password change is opt-in");
    Check.That(stored.LockedUntil is null, "new users aren't locked");
    Check.That(stored.Role.Name == "Manager", "a user has exactly one role");

    var storedRole = await db.Roles.Include(r => r.RolePermissions).SingleAsync(r => r.Id == role.Id);
    Check.That(storedRole.RolePermissions.Single().PermissionId == permission.Id, "a role keeps its permissions");
    Check.That(!storedRole.IsSystem, "IsSystem is stored");

    var storedToken = await db.RefreshTokens.SingleAsync(t => t.Id == token.Id);
    Check.That(storedToken.UserId == user.Id && storedToken.RevokedAt is null, "refresh tokens are stored unrevoked");
});

async Task Refused(string what, Action<Kanabol.Infrastructure.Persistence.AppDbContext> change) =>
    await database.Run(async db =>
    {
        change(db);
        try
        {
            await db.SaveChangesAsync();
            Check.That(false, what);
        }
        catch (DbUpdateException)
        {
        }
    });

await Refused("a second user with the same email is refused", db => db.Users.Add(new User("ana@example.com", "Other", "hash", role.Id)));
await Refused("a second role with the same name is refused", db => db.Roles.Add(new Role("Manager", false)));
await Refused("a second permission with the same name is refused", db => db.Permissions.Add(new Permission("users.manage")));
await Refused("a second token with the same hash is refused", db => db.RefreshTokens.Add(new RefreshToken(user.Id, Guid.CreateVersion7(), new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
await Refused("a user needs a role that exists", db => db.Users.Add(new User("no-role@example.com", "None", "hash", Guid.CreateVersion7())));
await Refused("a role with users can't be deleted", db => db.Roles.Remove(db.Roles.Single(r => r.Id == role.Id)));
CS
}

domain_has_no_annotations() {
  ! grep -rq 'DataAnnotations' backend/src/Kanabol.Domain --include='*.cs'
}

task_7() {
  echo "2.7 Users, roles, permissions and refresh tokens"
  need_database
  check "the schema uses the spec's names, types and indexes" users_and_roles_schema_is_right
  check "the entities behave on a real PostgreSQL" users_and_roles_behave
  check "Domain still has no packages" no_packages Kanabol.Domain
  check "Domain has no data annotations" domain_has_no_annotations
  check "Application still references only Domain" references_are Kanabol.Application Kanabol.Domain
}

board_schema_is_right() {
  probe Kanabol.Infrastructure <<'CS'
await using var database = await ProbeDatabase.Create("schema_board");
var script = database.CreateScript();
void Has(string text) => Check.That(script.Contains(text), $"the schema has: {text}");

Has("CREATE TABLE projects (");
Has("CREATE TABLE project_members (");
Has("CREATE TABLE board_columns (");
Has("CREATE TABLE task_items (");
Has("CREATE TABLE comments (");
Has("CREATE TABLE attachments (");
Has("CREATE TABLE task_history_entries (");
Has("CREATE TABLE notifications (");
Has("CONSTRAINT pk_task_items PRIMARY KEY (id)");
Has("CONSTRAINT pk_project_members PRIMARY KEY (project_id, user_id)");
Has("title character varying(300) NOT NULL");
Has("description text");
Has("body text NOT NULL");
Has("priority text NOT NULL");
Has("role text NOT NULL");
Has("type text NOT NULL");
Has("due_date timestamp with time zone");
Has("completed_at timestamp with time zone");
Has("archived_at timestamp with time zone");
Has("archived_by_id uuid");
Has("read_at timestamp with time zone");
Has("changed_at timestamp with time zone NOT NULL");
Has("is_done boolean NOT NULL");
Has("size bigint NOT NULL");
Has("storage_key character varying(500) NOT NULL");
Has("CONSTRAINT fk_task_items_projects_project_id FOREIGN KEY (project_id) REFERENCES projects (id)");
Has("CONSTRAINT fk_task_items_board_columns_column_id FOREIGN KEY (column_id) REFERENCES board_columns (id)");
Has("CONSTRAINT fk_task_items_users_assignee_id FOREIGN KEY (assignee_id) REFERENCES users (id)");
Has("CONSTRAINT fk_comments_task_items_task_id FOREIGN KEY (task_id) REFERENCES task_items (id)");
Has("CONSTRAINT fk_notifications_users_user_id FOREIGN KEY (user_id) REFERENCES users (id)");
Has("CREATE INDEX ix_task_items_project_id_column_id ON task_items (project_id, column_id)");
Check.That(!script.Contains("domain_events"), "domain events stay out of the schema");
Check.That(!script.Contains("xmin"), "xmin is PostgreSQL's own column, not one we create");
CS
}

board_entities_behave() {
  probe Kanabol.Infrastructure <<'CS'
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

await using var database = await ProbeDatabase.Create("board");

var role = new Role("Member", isSystem: false);
var user = new User("ana@example.com", "Ana", "hash", role.Id);
var project = new Project("Website", "The company site");
project.AddMember(user.Id, ProjectRole.Owner);
var column = new BoardColumn(project.Id, "To do", 0, isDone: false);
var task = new TaskItem(project.Id, column.Id, "Write the copy", "All pages", TaskPriority.High, 0);
var comment = new Comment(task.Id, user.Id, "Looks fine");
var attachment = new Attachment(task.Id, "brief.pdf", "application/pdf", 1234, "2026/11/key.pdf", user.Id);
var entry = new TaskHistoryEntry(task.Id, "Title", "Old", "New", user.Id, DateTimeOffset.UtcNow);
var notification = new Notification(user.Id, NotificationType.TaskAssigned, task.Id, "Assigned to you", DateTimeOffset.UtcNow);

await database.Run(async db =>
{
    db.Roles.Add(role);
    db.Users.Add(user);
    db.Projects.Add(project);
    db.BoardColumns.Add(column);
    db.TaskItems.Add(task);
    db.Comments.Add(comment);
    db.Attachments.Add(attachment);
    db.TaskHistoryEntries.Add(entry);
    db.Notifications.Add(notification);
    await db.SaveChangesAsync();
});

await database.Run(async db =>
{
    var stored = await db.TaskItems
        .Include(t => t.Comments)
        .Include(t => t.Attachments)
        .Include(t => t.History)
        .SingleAsync(t => t.Id == task.Id);
    Check.That(stored.Title == "Write the copy" && stored.Description == "All pages", "a task keeps its text");
    Check.That(stored.Priority == TaskPriority.High, "a task keeps its priority");
    Check.That(stored.ProjectId == project.Id && stored.ColumnId == column.Id, "a task knows its project and column");
    Check.That(stored.Position == 0, "a task keeps its position");
    Check.That(stored.AssigneeId is null && stored.DueDate is null, "assignee and deadline are optional");
    Check.That(stored.CompletedAt is null && stored.ArchivedAt is null, "a new task is neither done nor archived");
    Check.That(stored.Comments.Single().Body == "Looks fine", "a task owns its comments");
    Check.That(stored.Attachments.Single().StorageKey == "2026/11/key.pdf", "a task owns its attachments");
    Check.That(stored.History.Single().Field == "Title", "a task owns its history");
    Check.That(stored.CreatedAt != default, "tasks get audit stamps");

    var storedProject = await db.Projects.Include(p => p.Members).Include(p => p.Columns).SingleAsync(p => p.Id == project.Id);
    Check.That(storedProject.Members.Single().Role == ProjectRole.Owner, "a project owns its members");
    Check.That(storedProject.Columns.Single().Name == "To do", "a project owns its columns");
    Check.That(!storedProject.Columns.Single().IsDone, "a column knows whether it means done");

    var storedNotification = await db.Notifications.SingleAsync();
    Check.That(storedNotification.Type == NotificationType.TaskAssigned && storedNotification.ReadAt is null, "a notification is stored unread");

    var priority = await db.Database.SqlQueryRaw<string>("SELECT priority AS \"Value\" FROM task_items").SingleAsync();
    Check.That(priority == "High", "enums are stored as readable text");
    var memberRole = await db.Database.SqlQueryRaw<string>("SELECT role AS \"Value\" FROM project_members").SingleAsync();
    Check.That(memberRole == "Owner", "the project role is stored as text");

    var warnings = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.WarningsConfiguration;
    var behavior = warnings.GetBehavior(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning);
    Check.That(behavior == WarningBehavior.Ignore, "reading through aggregates makes the query filter warning noise");
});

// A new child found through a tracked parent has to be inserted, not updated.
await database.Run(async db =>
{
    var storedProject = await db.Projects.Include(p => p.Members).SingleAsync(p => p.Id == project.Id);
    var second = new User("dan@example.com", "Dan", "hash", role.Id);
    db.Users.Add(second);
    storedProject.AddMember(second.Id, ProjectRole.Member);
    await db.SaveChangesAsync();
    Check.That(await db.ProjectMembers.CountAsync() == 2, "a member added through its project is inserted");
});

async Task ConcurrentEditsConflict<TEntity>(string what, Guid id, string property)
    where TEntity : class
{
    await using var firstScope = database.Services.CreateAsyncScope();
    await using var secondScope = database.Services.CreateAsyncScope();
    var first = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var second = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var firstCopy = await first.Set<TEntity>().FindAsync(id);
    var secondCopy = await second.Set<TEntity>().FindAsync(id);
    first.Entry(firstCopy!).Property(property).CurrentValue = "first";
    await first.SaveChangesAsync();
    second.Entry(secondCopy!).Property(property).CurrentValue = "second";
    try
    {
        await second.SaveChangesAsync();
        Check.That(false, what);
    }
    catch (DbUpdateConcurrencyException)
    {
    }
}

await ConcurrentEditsConflict<TaskItem>("concurrent edits of a task conflict", task.Id, "Title");
await ConcurrentEditsConflict<Project>("concurrent edits of a project conflict", project.Id, "Name");
await ConcurrentEditsConflict<BoardColumn>("concurrent edits of a column conflict", column.Id, "Name");

await database.Run(async db =>
{
    Check.That(await db.TaskItems.CountAsync() == 1, "tasks start out visible");
    await db.Database.ExecuteSqlRawAsync("UPDATE task_items SET archived_at = now()");
    Check.That(await db.TaskItems.CountAsync() == 0, "archived tasks disappear from queries");
    Check.That(await db.TaskItems.IgnoreQueryFilters().CountAsync() == 1, "IgnoreQueryFilters still finds them");
    Check.That(await db.Comments.CountAsync() == 1, "the comments of an archived task stay");

    Check.That(await db.Projects.CountAsync() == 1, "projects start out visible");
    await db.Database.ExecuteSqlRawAsync("UPDATE projects SET archived_at = now()");
    Check.That(await db.Projects.CountAsync() == 0, "archived projects disappear from queries");
    Check.That(await db.Projects.IgnoreQueryFilters().CountAsync() == 1, "IgnoreQueryFilters still finds them");
});
CS
}

task_8() {
  echo "2.8 Projects, board, tasks and notifications"
  need_database
  check "the schema uses the spec's names, types and indexes" board_schema_is_right
  check "the entities behave on a real PostgreSQL" board_entities_behave
  check "Domain still has no packages" no_packages Kanabol.Domain
  check "Domain has no data annotations" domain_has_no_annotations
}

ef() { # ef <arguments...>: runs dotnet ef from backend/, against the built projects
  (cd backend && dotnet ef "$@" -p src/Kanabol.Infrastructure -s src/Kanabol.Api --no-build)
}

ef_tool_is_pinned() {
  grep -qF '"dotnet-ef"' dotnet-tools.json &&
    grep -qF '"version": "10.0.14"' dotnet-tools.json &&
    dotnet tool restore
}

migration_exists() {
  ls backend/src/Kanabol.Infrastructure/Persistence/Migrations/*_InitialSchema.cs
}

write_migration_sql() {
  ef migrations script -o "$scratch/schema.sql"
}

no_line_in_sql() { # no_line_in_sql <extended regex>
  [ -s "$scratch/schema.sql" ] && ! grep -E "$1" "$scratch/schema.sql"
}

only_ef_identifiers_are_quoted() {
  actual=$(grep -oE '"[^"]+"' "$scratch/schema.sql" | sort -u | paste -sd, -)
  [ "$actual" = '"__EFMigrationsHistory"' ] || {
    echo "quoted identifiers: $actual"
    return 1
  }
}

every_timestamp_is_timestamptz() {
  [ -s "$scratch/schema.sql" ] && ! grep -E '^ +[a-z_]+(_at|_until|_date) ' "$scratch/schema.sql" | grep -v 'timestamp with time zone'
}

migration_applies() {
  probe Kanabol.Infrastructure <<'CS'
using Kanabol.Domain.Tasks;
using Kanabol.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

await using var database = await ProbeDatabase.Create("migrated", migrate: true);

await database.Run(async db =>
{
    Check.That(!(await db.Database.GetPendingMigrationsAsync()).Any(), "no migration is left to apply");
    Check.That((await db.Database.GetAppliedMigrationsAsync()).Single().EndsWith("_InitialSchema", StringComparison.Ordinal), "InitialSchema is the one applied");
    await db.Database.MigrateAsync();

    var tables = await db.Database.SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' ORDER BY 1").ToListAsync();
    string[] expected = ["__EFMigrationsHistory", "attachments", "board_columns", "comments", "notifications", "permissions", "project_members", "projects", "refresh_tokens", "role_permissions", "roles", "task_history_entries", "task_items", "users"];
    Check.That(tables.SequenceEqual(expected), $"the tables are the expected ones: {string.Join(", ", tables)}");

    var booleans = await db.Database.SqlQueryRaw<string>("SELECT DISTINCT column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND data_type = 'boolean' ORDER BY 1").ToListAsync();
    Check.That(booleans.SequenceEqual(["is_active", "is_done", "is_system", "must_change_password"]), $"booleans: {string.Join(", ", booleans)}");

    var keys = await db.Database.SqlQueryRaw<string>("SELECT DISTINCT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'id'").ToListAsync();
    Check.That(keys.SequenceEqual(["uuid"]), "every id is a uuid");

    var enums = await db.Database.SqlQueryRaw<string>("SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND column_name IN ('priority', 'role', 'type')").ToListAsync();
    Check.That(enums.Count == 3 && enums.All(type => type == "text"), "enums are text columns");
});
CS
}

task_9() {
  echo "2.9 The InitialSchema migration"
  need_database
  check "dotnet-ef is pinned in the tool manifest" ef_tool_is_pinned
  check "the backend builds" build_backend
  check "the migration exists" migration_exists
  check "the model has no changes without a migration" ef migrations has-pending-model-changes
  check "the migration script generates" write_migration_sql
  check "no table or column needs quoting, so all are snake_case" only_ef_identifiers_are_quoted
  check "no column starts with a capital" no_line_in_sql '^ +[A-Z][a-z]'
  check "every timestamp is timestamptz" every_timestamp_is_timestamptz
  check "nothing is a timestamp without time zone" no_line_in_sql 'without time zone'
  check "no unique index is named ix_" no_line_in_sql '^CREATE UNIQUE INDEX ix_'
  check "no plain index is named ux_" no_line_in_sql '^CREATE INDEX ux_'
  check "every unique index is named ux_" no_line_in_sql '^CREATE UNIQUE INDEX [^u]'
  check "every plain index is named ix_" no_line_in_sql '^CREATE INDEX [^i]'
  check "the row version is xmin, not a column of ours" no_line_in_sql '^ +(version|xmin) '
  check "the migration applies to a real PostgreSQL" migration_applies
}

fresh_database() { # fresh_database <suffix>: creates a database and prints its connection string
  need_database
  sql "$admin_connection" "CREATE DATABASE ${check_db}_$1" >/dev/null || return 1
  printf '%s' "$base_connection" | sed "s/Database=[^;]*/Database=${check_db}_$1/"
}

table_count() { # table_count <connection>
  sql "$1" "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'"
}

# Starts the API with one setting removed, and passes if it refuses to run and says why.
refuses_to_start_without() { # refuses_to_start_without <variable> <text in the error>
  api_env
  log="$scratch/refused-$1.log"
  if (cd "$api_dir" && env -u "$1" timeout 60 dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls http://127.0.0.1:5289) >"$log" 2>&1; then
    echo "the API ran without $1"
    return 1
  fi
  grep -qF "$2" "$log" || {
    tail -n 5 "$log"
    return 1
  }
}

refuses_a_short_secret() {
  api_env
  log="$scratch/refused-short.log"
  if (cd "$api_dir" && Jwt__Secret=too-short timeout 60 dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls http://127.0.0.1:5289) >"$log" 2>&1; then
    echo "the API ran with a short secret"
    return 1
  fi
  grep -qF 'Secret' "$log"
}

migrate_off_leaves_database_empty() {
  connection=$(fresh_database migrate_off) || return 1
  ConnectionStrings__Default=$connection Database__MigrateOnStartup=false start_api
  stop_api
  [ "$(table_count "$connection")" = 0 ]
}

migrate_on_creates_the_schema() {
  connection=$(fresh_database migrate_on) || return 1
  ConnectionStrings__Default=$connection Database__MigrateOnStartup=true start_api || return 1
  stop_api
  [ "$(sql "$connection" "SELECT to_regclass('public.task_items') IS NOT NULL")" = True ] || return 1
  # Starting again on a migrated database must work too.
  ConnectionStrings__Default=$connection Database__MigrateOnStartup=true start_api || return 1
  stop_api
}

current_user_reads_claims() {
  probe Kanabol.Api <<'CS'
using System.Security.Claims;
using Kanabol.Api.Authentication;

var id = Guid.CreateVersion7();
var signedIn = new DefaultHttpContext
{
    User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("sub", id.ToString()), new Claim("permission", "users.manage")],
        authenticationType: "probe")),
};
var user = new HttpCurrentUser(new HttpContextAccessor { HttpContext = signedIn });
Check.That(user.IsAuthenticated, "a signed-in request is authenticated");
Check.That(user.Id == id, "the id comes from the sub claim");
Check.That(user.HasPermission("users.manage"), "permissions come from the permission claims");
Check.That(!user.HasPermission("roles.manage"), "other permissions are refused");

var anonymous = new HttpCurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
Check.That(!anonymous.IsAuthenticated, "a request without a user isn't authenticated");
Check.That(!anonymous.HasPermission("users.manage"), "a request without a user has no permissions");
Check.Throws<InvalidOperationException>(() => _ = anonymous.Id, "a request without a user has no id");

var outsideRequest = new HttpCurrentUser(new HttpContextAccessor());
Check.That(!outsideRequest.IsAuthenticated, "outside a request nobody is authenticated");
CS
}

task_10() {
  echo "2.10 Program.cs: options, migration on startup, Swagger"
  need_database
  check "the backend builds" build_backend
  check "it refuses to start without Jwt:Secret" refuses_to_start_without Jwt__Secret 'Secret'
  check "it refuses to start with a short Jwt:Secret" refuses_a_short_secret
  check "it refuses to start without Storage:Root" refuses_to_start_without Storage__Root 'Root'
  check "it refuses to start without a connection string" refuses_to_start_without ConnectionStrings__Default 'ConnectionString'
  check "Database:MigrateOnStartup=false leaves the database alone" migrate_off_leaves_database_empty
  check "Database:MigrateOnStartup=true migrates, and restarts fine" migrate_on_creates_the_schema
  ASPNETCORE_ENVIRONMENT=Production
  export ASPNETCORE_ENVIRONMENT
  check "the Production API starts" start_api
  check "/health answers in Production" returns 200 /health
  check "Swagger is off in Production" returns 404 /swagger/index.html
  check "the OpenAPI document is off in Production" returns 404 /swagger/v1/swagger.json
  stop_api
  ASPNETCORE_ENVIRONMENT=Development
  check "the Development API starts" start_api
  check "Swagger is on in Development" returns 200 /swagger/index.html
  check "the OpenAPI document is on in Development" returns 200 /swagger/v1/swagger.json
  stop_api
  unset ASPNETCORE_ENVIRONMENT
  check "the current user comes from the request's claims" current_user_reads_claims
}

is_ignored() {
  git check-ignore -q "$1"
}

no_cors_anywhere() {
  ! grep -rniE '\bcors\b' backend/src --include='*.cs' --include='*.json' --exclude-dir=bin --exclude-dir=obj
}

json_is_valid() { # json_is_valid <file>
  node -e "JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'))" "$1"
}

has_user_secret() { # has_user_secret <key>
  dotnet user-secrets list --id kanabol-api 2>/dev/null | grep -q "^$1 = "
}

# The JWT secret and the storage root aren't in the environment here, so they have to come from
# the user secrets and from appsettings.Development.json.
development_starts_from_its_own_settings() {
  if ! has_user_secret Jwt:Secret; then
    echo "skipped: no Jwt:Secret user secret on this machine"
    return 0
  fi
  connection=$(fresh_database development) || return 1
  (
    ASPNETCORE_ENVIRONMENT=Development
    ConnectionStrings__Default=$connection
    Jwt__Secret=
    Jwt__Issuer=
    Jwt__Audience=
    Storage__Root=
    Database__MigrateOnStartup=
    unset Jwt__Secret Jwt__Issuer Jwt__Audience Storage__Root Database__MigrateOnStartup
    export ASPNETCORE_ENVIRONMENT ConnectionStrings__Default
    log="$scratch/development.log"
    (cd "$api_dir" && exec dotnet bin/Debug/net10.0/Kanabol.Api.dll --urls http://127.0.0.1:5288) >"$log" 2>&1 &
    pid=$!
    attempts=0
    until curl -s -o /dev/null http://127.0.0.1:5288/health; do
      attempts=$((attempts + 1))
      if [ "$attempts" -gt 120 ] || ! kill -0 "$pid" 2>/dev/null; then
        tail -n 8 "$log"
        exit 1
      fi
      sleep 0.5
    done
    kill "$pid"
    wait "$pid" 2>/dev/null
    exit 0
  ) || return 1
  [ "$(sql "$connection" "SELECT to_regclass('public.users') IS NOT NULL")" = True ]
}

# A throwaway copy holds the local file, which must not be left in the working tree.
local_settings_override_development_only() {
  copy="$scratch/local-settings"
  backend_copy "$copy"
  dotnet build "$copy/$api_dir/Kanabol.Api.csproj" --nologo -v q || return 1
  printf '{ "Database": { "MigrateOnStartup": false } }\n' >"$copy/$api_dir/appsettings.Development.local.json"

  in_development=$(fresh_database local_development) || return 1
  ConnectionStrings__Default=$in_development ASPNETCORE_ENVIRONMENT=Development start_api "$copy/$api_dir"
  stop_api
  [ "$(table_count "$in_development")" = 0 ] || {
    echo "the local file was ignored in Development"
    return 1
  }

  in_production=$(fresh_database local_production) || return 1
  ConnectionStrings__Default=$in_production ASPNETCORE_ENVIRONMENT=Production start_api "$copy/$api_dir"
  stop_api
  [ "$(table_count "$in_production")" != 0 ] || {
    echo "the local file was used outside Development"
    return 1
  }
}

task_11() {
  echo "2.11 Settings and secrets"
  need_database
  settings="$api_dir/appsettings.json"
  development="$api_dir/appsettings.Development.json"
  check "appsettings.json is valid JSON" json_is_valid "$settings"
  check "appsettings.Development.json is valid JSON" json_is_valid "$development"
  check "appsettings.json turns migrations on" grep -qE '"MigrateOnStartup": true' "$settings"
  check "appsettings.json has the JWT issuer" grep -qE '"Issuer": "kanabol"' "$settings"
  check "appsettings.json has the JWT audience" grep -qE '"Audience": "kanabol"' "$settings"
  check "appsettings.json keeps attachments in the Docker volume" grep -qF '"Root": "/data/attachments"' "$settings"
  check "Development keeps attachments in an ignored folder" grep -qF '.data/attachments' "$development"
  check "that folder is ignored" is_ignored .data/attachments/file.txt
  for file in "$settings" "$development"; do
    check "$file has no JWT secret" not_in_file '"Secret"' "$file"
    check "$file has no connection string" not_in_file 'ConnectionStrings' "$file"
    check "$file has no password" not_in_file 'assword' "$file"
  done
  check "there's no CORS anywhere" no_cors_anywhere
  check "the API has a user secrets id" property_is Kanabol.Api UserSecretsId kanabol-api
  check "the local settings file is ignored" is_ignored "$api_dir/appsettings.Development.local.json"
  check "the backend builds" build_backend
  check "Development starts from user secrets and its settings file" development_starts_from_its_own_settings
  check "the local settings file overrides, in Development only" local_settings_override_development_only
  check "the backend notes explain user secrets" grep -qF 'dotnet user-secrets' backend/CLAUDE.md
  check "the backend notes explain the local settings file" grep -qF 'appsettings.Development.local.json' backend/CLAUDE.md
}

correlation_header() { # correlation_header [curl-arguments...]: the id the API answers with
  curl -s -D - -o /dev/null "$@" "$API_URL/health" | tr -d '\r' | sed -n 's/^[Xx]-[Cc]orrelation-[Ii]d: //p'
}

echoes_correlation_id() {
  [ "$(correlation_header -H 'X-Correlation-Id: from-the-browser-1')" = from-the-browser-1 ]
}

is_guid() {
  printf '%s' "$1" | grep -qE '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' || {
    echo "not a GUID: '$1'"
    return 1
  }
}

generates_correlation_id() {
  is_guid "$(correlation_header)"
}

replaces_unsafe_correlation_id() {
  is_guid "$(correlation_header -H 'X-Correlation-Id: bad id {with} spaces')" &&
    is_guid "$(correlation_header -H "X-Correlation-Id: $(printf 'a%.0s' $(seq 1 65))")"
}

log_has() { # log_has <text>
  grep -qF -- "$1" "$api_log" || {
    echo "not in the log: $1"
    tail -n 5 "$api_log"
    return 1
  }
}

log_lacks() { # log_lacks <text>
  ! grep -qF -- "$1" "$api_log"
}

# Stands in for Seq until Docker runs the real one: records what the sink posts.
start_fake_seq() { # start_fake_seq <port> <file>
  node -e "
    const http = require('http');
    const fs = require('fs');
    http.createServer((request, response) => {
      let body = '';
      request.on('data', (chunk) => (body += chunk));
      request.on('end', () => {
        fs.appendFileSync(process.argv[2], request.url + ' ' + body + '\n');
        response.statusCode = 201;
        response.end('{}');
      });
    }).listen(Number(process.argv[1]), '127.0.0.1');
  " "$1" "$2" &
  fake_seq_pid=$!
}

seq_received() { # seq_received <file> <text>
  attempts=0
  until grep -qF -- "$2" "$1" 2>/dev/null; do
    attempts=$((attempts + 1))
    [ "$attempts" -gt 40 ] && return 1
    sleep 0.5
  done
}

user_id_reaches_the_logs() {
  probe Kanabol.Api <<'CS'
using Kanabol.Api.Logging;
using Kanabol.Application.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Hosting;

var sink = new Collecting();
using var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
var id = Guid.CreateVersion7();

var middleware = new UserIdLogContextMiddleware(_ =>
{
    logger.Information("inside the request");
    return Task.CompletedTask;
});
var diagnostics = new DiagnosticContext(logger);
await middleware.InvokeAsync(new DefaultHttpContext(), new SignedIn(id), diagnostics);
Check.That(sink.Events.Single().Properties["UserId"].ToString().Contains(id.ToString(), StringComparison.Ordinal), "logs inside a request carry the user's id");

sink.Events.Clear();
await middleware.InvokeAsync(new DefaultHttpContext(), new Visitor(), diagnostics);
Check.That(!sink.Events.Single().Properties.ContainsKey("UserId"), "logs without a user carry no id");

internal sealed class Collecting : ILogEventSink
{
    public List<LogEvent> Events { get; } = [];

    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}

internal sealed class SignedIn(Guid id) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid Id => id;

    public bool HasPermission(string permission) => false;
}

internal sealed class Visitor : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid Id => throw new InvalidOperationException();

    public bool HasPermission(string permission) => false;
}
CS
}

connection_password() {
  printf '%s' "$base_connection" | sed -n 's/.*Password=\([^;]*\).*/\1/p'
}

task_12() {
  echo "2.12 Serilog and correlation ids"
  need_database
  settings="$api_dir/appsettings.json"
  development="$api_dir/appsettings.Development.json"
  check "the default Logging section is gone from appsettings.json" not_in_file '"Logging"' "$settings"
  check "the default Logging section is gone from Development" not_in_file '"Logging"' "$development"
  check "Development logs Kanabol at Debug" grep -qF '"Kanabol": "Debug"' "$development"
  check "the backend builds" build_backend

  seq_file="$scratch/seq.log"
  start_fake_seq 5287 "$seq_file"
  Serilog__WriteTo__1__Args__serverUrl=http://127.0.0.1:5287
  ASPNETCORE_ENVIRONMENT=Production
  export ASPNETCORE_ENVIRONMENT Serilog__WriteTo__1__Args__serverUrl
  check "the Production API starts" start_api
  check "a correlation id from the request comes back" echoes_correlation_id
  check "without one the API generates a GUID" generates_correlation_id
  check "an unsafe correlation id is replaced" replaces_unsafe_correlation_id
  sleep 1
  check "Production logs compact JSON" log_has '{"@t":'
  check "the request log has the correlation id" log_has '"CorrelationId":"from-the-browser-1"'
  check "the request log has the path" log_has '"RequestPath":"/health"'
  check "the request log has the status" log_has '"StatusCode":200'
  check "the request log has the duration" log_has '"Elapsed":'
  check "logs carry the environment" log_has '"EnvironmentName":"Production"'
  check "logs carry the machine" log_has '"MachineName":'
  check "Production hides ASP.NET's per-request logs" log_lacks 'Request starting'
  check "Production hides the SQL" log_lacks 'Executed DbCommand'
  check "the logs reach Seq" seq_received "$seq_file" 'from-the-browser-1'
  check "Production logs don't have the JWT secret" log_lacks "$Jwt__Secret"
  check "Production logs don't have the database password" log_lacks "$(connection_password)"
  stop_api
  kill "$fake_seq_pid" 2>/dev/null
  wait "$fake_seq_pid" 2>/dev/null

  ASPNETCORE_ENVIRONMENT=Development
  unset Serilog__WriteTo__1__Args__serverUrl
  check "the Development API starts" start_api
  check "a request in Development answers" returns 200 /health
  sleep 1
  check "Development logs are readable, not JSON" log_lacks '{"@t":'
  check "Development shows ASP.NET's per-request logs" log_has 'Request starting'
  check "Development shows the SQL" log_has 'Executed DbCommand'
  check "Development logs don't have the JWT secret" log_lacks "$Jwt__Secret"
  check "Development logs don't have the database password" log_lacks "$(connection_password)"
  stop_api
  unset ASPNETCORE_ENVIRONMENT

  check "the user's id reaches the logs" user_id_reaches_the_logs
}

if [ "$#" -eq 0 ]; then
  set -- 1 2 3 4 5 6 7 8 9 10 11 12
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
