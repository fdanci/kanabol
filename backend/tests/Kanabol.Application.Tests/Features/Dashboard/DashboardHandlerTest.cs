using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Dashboard;

/// <summary>
/// Dan's tasks across several projects, for the main panel and "Task-urile mele" (SPEC section 4.4, "Panoul
/// principal și task-urile mele"): his own are the open and finished tasks he is assigned to, not archived,
/// in projects not archived where he is still a member.
/// </summary>
public abstract class DashboardHandlerTest(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    /// <summary>The Monday of Now's week, at midnight in UTC: Now is Thursday 5 November 2026.</summary>
    protected static readonly DateTimeOffset WeekStart = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    protected sealed record Team(User Ana, User Dan, Project Kan, Project Web, Project Old, Project Mob);

    /// <summary>
    /// KAN (Ana owns it, Dan is a member): KAN-1 Dan's, open, overdue, High; KAN-2 Dan's, open, due in 5 days,
    /// Low; KAN-3 Dan's, archived; KAN-4 Dan's, finished this week, no deadline; KAN-5 Dan's, finished last
    /// week, past its deadline; KAN-6 Ana's only, open, overdue. WEB (Dan is a member): WEB-1 Dan's and Ana's,
    /// open, no deadline, Critical; WEB-2 Dan's, finished this week, then archived. OLD, archived (Dan is a
    /// member): OLD-1 Dan's, open, overdue. MOB (Dan left it, still assigned): MOB-1 Dan's, open.
    /// </summary>
    protected async Task<Team> DansTasks()
    {
        var ana = await CreateUserNamed("Ana Pop", "ana.pop@example.com");
        var dan = await CreateUserNamed("Dan Ionescu", "dan@example.com");
        var kan = await CreateProject(builder => builder.WithKey("KAN").WithName("Kanabol").OwnedBy(ana).WithMember(dan));
        var web = await CreateProject(builder => builder.WithKey("WEB").WithName("Site").OwnedBy(ana).WithMember(dan));
        var old = await CreateProject(builder => builder.WithKey("OLD").WithName("Vechi").OwnedBy(ana).WithMember(dan));
        var mob = await CreateProject(builder => builder.WithKey("MOB").WithName("Mobil").OwnedBy(ana));

        TaskItemBuilder In(Project project) => new TaskItemBuilder().InProject(project).CreatedBy(ana);

        TaskItem[] kanTasks =
        [
            In(kan).WithTitle("Login SSO").WithPriority(TaskPriority.High).AssignedTo(dan).DueAt(Now.AddDays(-1)).Build(),
            In(kan).WithTitle("Pagina de start").WithPriority(TaskPriority.Low).AssignedTo(dan).DueAt(Now.AddDays(5)).Build(),
            In(kan).WithTitle("Arhivat").AssignedTo(dan).DueAt(Now.AddDays(-2)).Build(),
            In(kan).InColumn(DoneColumn(kan)).WithTitle("Logo nou").AssignedTo(dan).Build(),
            In(kan).InColumn(DoneColumn(kan)).WithTitle("Raport vechi").AssignedTo(dan).DueAt(Now.AddDays(-3)).Build(),
            In(kan).WithTitle("Al Anei").AssignedTo(ana).DueAt(Now.AddDays(-1)).Build(),
        ];
        kanTasks[2].Archive(ana.Id, Now);
        await AddTasks(kan, kanTasks);

        TaskItem[] webTasks =
        [
            In(web).WithTitle("Formular de contact").WithPriority(TaskPriority.Critical).AssignedTo(dan, ana).Build(),
            In(web).InColumn(DoneColumn(web)).WithTitle("Banner arhivat").AssignedTo(dan).Build(),
        ];
        webTasks[1].Archive(ana.Id, Now);
        await AddTasks(web, webTasks);

        await AddTasks(old, In(old).WithTitle("Din arhivă").AssignedTo(dan).DueAt(Now.AddDays(-1)).Build());
        await Archive(old, ana);

        await AddTasks(mob, In(mob).WithTitle("Plecat din proiect").AssignedTo(dan).Build());

        await CompletedAt(kan, 4, Now.AddDays(-1));
        await CompletedAt(kan, 5, WeekStart.AddDays(-5));
        await CompletedAt(web, 2, Now.AddDays(-2));

        SignIn(dan);
        return new Team(ana, dan, kan, web, old, mob);
    }

    protected async Task CompletedAt(Project project, int number, DateTimeOffset completedAt)
    {
        await using var db = Postgres.CreateContext();
        await db.TaskItems.IgnoreQueryFilters()
            .Where(task => task.ProjectId == project.Id && task.Number == number)
            .ExecuteUpdateAsync(set => set.SetProperty(task => task.CompletedAt, completedAt));
    }
}
