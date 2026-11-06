using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.CreateTask;
using Kanabol.Application.Tests.Features.Projects;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>For the handlers of tasks: a project with its owner, created through the plain context, and tasks made by the handler.</summary>
public abstract class TaskHandlerTest(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    /// <summary>What the token of the signed-in user says they hold, which some handlers read (SPEC section 4.4).</summary>
    protected void HoldsInToken(params string[] permissions) =>
        CurrentUser.HasPermission(Arg.Any<string>()).Returns(call => permissions.Contains(call.Arg<string>()));

    protected static CreateTaskCommand NewTask(
        Project project,
        string title = "Login SSO",
        Guid? columnId = null,
        Guid? assigneeId = null,
        TaskPriority priority = TaskPriority.Medium,
        string? description = null,
        DateTimeOffset? dueDate = null) =>
        new(project.Id, title, description, priority, columnId, assigneeId, dueDate);

    protected Task<Common.Result<TaskItemDto>> Create(CreateTaskCommand command) =>
        Handle<CreateTaskCommand, TaskItemDto>(command);

    /// <summary>A task made by the handler, as the user given, who has to be a member of the project.</summary>
    protected async Task<TaskItemDto> CreateTaskAs(User user, Project project, string title = "Login SSO", Guid? assigneeId = null)
    {
        SignIn(user);
        var result = await Create(NewTask(project, title, assigneeId: assigneeId));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    protected async Task<TaskItem?> Stored(Guid taskId)
    {
        await using var db = Postgres.CreateContext();
        return await db.TaskItems.IgnoreQueryFilters().Include(task => task.History).SingleOrDefaultAsync(task => task.Id == taskId);
    }

    protected async Task<List<TaskItem>> TasksOf(Project project)
    {
        await using var db = Postgres.CreateContext();
        return await db.TaskItems.IgnoreQueryFilters().Where(task => task.ProjectId == project.Id).OrderBy(task => task.Number).ToListAsync();
    }

    protected async Task<int> NextTaskNumberOf(Project project)
    {
        await using var db = Postgres.CreateContext();
        return await db.Projects.IgnoreQueryFilters().Where(stored => stored.Id == project.Id).Select(stored => stored.NextTaskNumber).SingleAsync();
    }

    /// <summary>Saves tasks built outside a handler, with the project's counter moved past them.</summary>
    protected async Task AddTasks(Project project, params TaskItem[] tasks)
    {
        await using var db = Postgres.CreateContext();
        db.TaskItems.AddRange(tasks);
        await db.Projects.IgnoreQueryFilters().Where(stored => stored.Id == project.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(stored => stored.NextTaskNumber, project.NextTaskNumber));
        await db.SaveChangesAsync();
    }

    protected static BoardColumn ColumnAt(Project project, int position) => project.Columns.Single(column => column.Position == position);

    protected static BoardColumn DoneColumn(Project project) => project.Columns.Single(column => column.IsDone);
}
