using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Tests.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features;

/// <summary>For the handlers that manage users and roles, on a database with every permission, as after the seed.</summary>
public abstract class ManagementHandlerTest(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private readonly AsyncLocal<Guid?> _actingUser = new();
    private readonly AsyncLocal<int> _pauseAtClockCall = new();
    private TaskCompletionSource _paused = new();
    private TaskCompletionSource _resumed = new();
    private Guid _signedIn;
    private int _clockCallsInPausedRequest;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        CurrentUser.Id.Returns(_ => _actingUser.Value ?? _signedIn);
        Clock.UtcNow.Returns(_ => PauseIfThisIsTheCall());

        await using var db = Postgres.CreateContext();
        db.Permissions.AddRange(Permissions.All.Select(name => new Permission(name)));
        await db.SaveChangesAsync();
    }

    protected void SignIn(User user)
    {
        CurrentUser.IsAuthenticated.Returns(true);
        _signedIn = user.Id;
    }

    /// <summary>Runs a command in a scope of its own, as another request would, signed in as the user given or as SignIn's.</summary>
    protected Task<Result<TResult>> HandleInOwnScope<TCommand, TResult>(TCommand command, User? actingUser = null)
        where TCommand : ICommand<TResult> =>
        Task.Run(async () =>
        {
            _actingUser.Value = actingUser?.Id;
            await using var scope = NewScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle(command, CancellationToken.None);
        });

    /// <summary>
    /// Stops a request at its nth read of the clock, runs what else happens meanwhile, then lets it go
    /// on. The clock is read at known places (a handler's "now", the audit stamp of a save), so this puts
    /// the other requests exactly between that request's reads and its save, every time.
    /// </summary>
    protected async Task<T> PausedAtClockCall<T>(int call, Func<Task<T>> request, Func<Task> meanwhile)
    {
        _clockCallsInPausedRequest = 0;
        _paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Task.Run(() =>
        {
            _pauseAtClockCall.Value = call;
            return request();
        });

        if (await Task.WhenAny(_paused.Task, running) == running)
        {
            throw new InvalidOperationException($"The request finished without reading the clock {call} times.");
        }

        await meanwhile();
        _resumed.SetResult();
        return await running;
    }

    private DateTimeOffset PauseIfThisIsTheCall()
    {
        if (_pauseAtClockCall.Value > 0 && Interlocked.Increment(ref _clockCallsInPausedRequest) == _pauseAtClockCall.Value)
        {
            _paused.SetResult();
            _resumed.Task.GetAwaiter().GetResult();
        }

        return Now;
    }

    protected async Task<User> SignInWith(params string[] permissions)
    {
        var user = await CreateUser("manager@example.com", permissions: permissions);
        SignIn(user);
        return user;
    }

    /// <summary>The Admin role is created with its first holder, with every permission, like the seed does.</summary>
    protected async Task<User> CreateAdmin(string email = "admin@example.com")
    {
        await using var db = Postgres.CreateContext();

        var role = await db.Roles.SingleOrDefaultAsync(existing => existing.IsSystem);
        if (role is null)
        {
            role = new Role(Role.AdminName, isSystem: true);
            await db.Permissions.ForEachAsync(permission => role.Grant(permission.Id));
            db.Roles.Add(role);
        }

        var user = new UserBuilder().WithEmail(email).InRole(role).WithPasswordHash(Get<IPasswordHasher>().Hash(Password)).Build();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected async Task<List<string>> PermissionNamesOf(Guid roleId)
    {
        await using var db = Postgres.CreateContext();
        return await db.Roles
            .Where(role => role.Id == roleId)
            .SelectMany(role => role.RolePermissions)
            .Join(db.Permissions, granted => granted.PermissionId, permission => permission.Id, (_, permission) => permission.Name)
            .ToListAsync();
    }

    protected async Task<Role> CreateRole(string name, params string[] permissions)
    {
        await using var db = Postgres.CreateContext();

        var role = new Role(name, isSystem: false);
        var granted = await db.Permissions.Where(permission => permissions.Contains(permission.Name)).ToListAsync();
        granted.ForEach(permission => role.Grant(permission.Id));

        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }
}
