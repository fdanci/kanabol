using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Settings;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Reports;

/// <summary>
/// Work finished in October 2026 across three projects, for the reports (SPEC section 5.1.1): each task
/// is built where it was created, then moved through the columns at its own moments, as the history has
/// it, so completed_at and the ColumnId entries agree.
/// </summary>
public abstract class ReportsHandlerTest(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    /// <summary>Four weeks from Monday 5 October 2026, up to Monday 2 November, in UTC.</summary>
    protected static readonly DateTimeOffset From = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    protected static readonly DateTimeOffset To = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    protected sealed record Team(User Ana, User Dan, User Eva, Project Kan, Project Web, Project Old);

    protected static DateTimeOffset October(int day, int hour = 10) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>The default schedule in UTC, for the moves out of În lucru, which stop the worked time's counter.</summary>
    protected static readonly WorkingHours Hours = new(WorkSchedule.Default(), TimeZoneInfo.Utc);

    /// <summary>A task created at the moment given, then moved into each column given at its moment, by Ana.</summary>
    protected static TaskItem Through(TaskItemBuilder builder, DateTimeOffset createdAt, User by, params (BoardColumn Column, DateTimeOffset At)[] moves)
    {
        var task = builder.Build();
        task.CreatedAt = createdAt;
        foreach (var (column, at) in moves)
        {
            task.MoveTo(column, 0, by.Id, at, hours: Hours);
        }

        return task;
    }

    /// <summary>
    /// KAN (Ana owns it, Dan is a member), columns Backlog, În lucru, În pauză, Gata:
    /// KAN-1 Dan's and Ana's, created 1 Oct 09:00, În lucru 6 Oct, Gata 8 Oct, due 10 Oct (on time);
    /// KAN-2 Dan's, created straight in În lucru 2 Oct 09:00, Gata 14 Oct 09:00, due 13 Oct (late);
    /// KAN-3 Dan's and Ana's, created 15 Oct, În lucru 18 Oct 12:00, Gata 20 Oct 12:00, archived 21 Oct, no deadline;
    /// KAN-4 Ana's, open, archived 9 Oct, due 10 Oct; KAN-5 Dan's, open, due 20 Oct (overdue now);
    /// KAN-6 Dan's, Gata 30 Sep (before the interval); KAN-7 Dan's, created 1 Oct, În lucru 3 Oct,
    /// Gata at From exactly, due 4 Oct (late); KAN-8 Dan's, Gata at To exactly (after the interval).
    /// WEB (Ana owns it, Dan and Eva are members): WEB-1 Eva's, created 20 Oct, În lucru 22 Oct, Gata 27 Oct,
    /// due 30 Oct (on time); WEB-2 nobody's, created 27 Oct, Gata 28 Oct; WEB-3 Eva's, open, due 1 Nov (overdue now).
    /// OLD (Ana owns it, Dan is a member), archived: OLD-1 Dan's, created 10 Oct, În lucru 12 Oct, Gata 15 Oct;
    /// OLD-2 Dan's, open, due 1 Oct.
    /// Signed in as someone with reports.view in their token.
    /// </summary>
    protected async Task<Team> FinishedWork()
    {
        var ana = await CreatePerson("Ana Pop", "ana.pop@example.com");
        var dan = await CreatePerson("Dan Ionescu", "dan@example.com");
        var eva = await CreatePerson("Eva Marin", "eva@example.com");
        var kan = await CreateProject(builder => builder.WithKey("KAN").WithName("Kanabol").OwnedBy(ana).WithMember(dan));
        var web = await CreateProject(builder => builder.WithKey("WEB").WithName("Site").OwnedBy(ana).WithMember(dan).WithMember(eva));
        var old = await CreateProject(builder => builder.WithKey("OLD").WithName("Vechi").OwnedBy(ana).WithMember(dan));

        TaskItemBuilder In(Project project) => new TaskItemBuilder().InProject(project).CreatedBy(ana);
        var kanTodo = ColumnAt(kan, 0);
        var kanDoing = ColumnAt(kan, 1);
        var kanDone = DoneColumn(kan);

        TaskItem[] kanTasks =
        [
            Through(In(kan).WithTitle("Login SSO").AssignedTo(dan, ana).DueAt(October(10)), October(1, 9), ana, (kanDoing, October(6)), (kanDone, October(8))),
            Through(In(kan).InColumn(kanDoing).WithTitle("Pagina de start").AssignedTo(dan).DueAt(October(13, 12)), October(2, 9), ana, (kanDone, October(14, 9))),
            Through(In(kan).WithTitle("Logo nou").AssignedTo(dan, ana), October(15), ana, (kanDoing, October(18, 12)), (kanDone, October(20, 12))),
            Through(In(kan).WithTitle("Arhivat deschis").AssignedTo(ana).DueAt(October(10)), October(3), ana),
            Through(In(kan).WithTitle("Întârziat").AssignedTo(dan).DueAt(October(20)), October(1), ana),
            Through(In(kan).WithTitle("Din septembrie").AssignedTo(dan), new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero), ana, (kanDone, new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero))),
            Through(In(kan).WithTitle("La început").AssignedTo(dan).DueAt(October(4)), October(1), ana, (kanDoing, October(3)), (kanDone, From)),
            Through(In(kan).WithTitle("La capăt").AssignedTo(dan), October(26), ana, (kanDone, To)),
        ];
        kanTasks[2].Archive(ana.Id, October(21), Hours);
        kanTasks[3].Archive(ana.Id, October(9), Hours);
        await AddTasks(kan, kanTasks);

        TaskItem[] webTasks =
        [
            Through(In(web).WithTitle("Formular de contact").AssignedTo(eva).DueAt(October(30)), October(20), ana, (ColumnAt(web, 1), October(22)), (DoneColumn(web), October(27))),
            Through(In(web).WithTitle("Fără nimeni"), October(27), ana, (DoneColumn(web), October(28))),
            Through(In(web).WithTitle("Al Evei, întârziat").AssignedTo(eva).DueAt(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero)), October(25), ana),
        ];
        await AddTasks(web, webTasks);

        TaskItem[] oldTasks =
        [
            Through(In(old).WithTitle("Din arhivă").AssignedTo(dan), October(10), ana, (ColumnAt(old, 1), October(12)), (DoneColumn(old), October(15))),
            Through(In(old).WithTitle("Uitat în arhivă").AssignedTo(dan).DueAt(October(1)), October(1), ana),
        ];
        await AddTasks(old, oldTasks);
        await Archive(old, ana);

        await SignInWithReports();
        return new Team(ana, dan, eva, kan, web, old);
    }

    /// <summary>
    /// Someone of a role without permissions, named, and without a password: the handlers take the caller from
    /// CurrentUser and nobody signs in, so no test pays for PBKDF2, which made these tests most of their time.
    /// </summary>
    protected async Task<User> CreatePerson(string displayName, string email)
    {
        await using var db = Postgres.CreateContext();
        var role = new Role($"Role of {email}", isSystem: false);
        var user = new User(email, displayName, "no password: nobody signs in here", role.Id);
        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return await db.Users.AsNoTracking().SingleAsync(found => found.Id == user.Id);
    }

    /// <summary>Someone in no project, with reports.view in the token, as the reports read it (SPEC section 5.1.1).</summary>
    protected async Task<User> SignInWithReports()
    {
        var viewer = await CreatePerson("Radu Rapoarte", "radu@example.com");
        SignIn(viewer);
        HoldsInToken(Permissions.ReportsView);
        return viewer;
    }
}
