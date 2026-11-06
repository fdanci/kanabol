using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>
/// For the handlers of projects and their members, on a database with every permission, as after the seed.
/// The data is set up through a plain context, without the audit stamps, so a project made here was created Now.
/// </summary>
public abstract class ProjectHandlerTest(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    protected Task<Result<TResult>> Handle<TCommand, TResult>(TCommand command)
        where TCommand : ICommand<TResult> =>
        Get<ICommandHandler<TCommand, TResult>>().Handle(command, CancellationToken.None);

    protected Task<Result<TResult>> Ask<TQuery, TResult>(TQuery query)
        where TQuery : IQuery<TResult> =>
        Get<IQueryHandler<TQuery, TResult>>().Handle(query, CancellationToken.None);

    protected async Task<Project> CreateProject(Func<ProjectBuilder, ProjectBuilder>? configure = null)
    {
        var project = (configure ?? (builder => builder))(new ProjectBuilder()).Build();
        project.CreatedAt = Now;

        await using var db = Postgres.CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    protected async Task Archive(Project project, User by)
    {
        await using var db = Postgres.CreateContext();
        var stored = await db.Projects.SingleAsync(existing => existing.Id == project.Id);
        stored.Archive(by.Id, Now);
        await db.SaveChangesAsync();
    }

    /// <summary>The project as stored, archived or not, with its members and columns.</summary>
    protected async Task<Project?> Reload(Project project)
    {
        await using var db = Postgres.CreateContext();
        return await db.Projects
            .IgnoreQueryFilters()
            .Include(stored => stored.Members)
            .Include(stored => stored.Columns)
            .SingleOrDefaultAsync(stored => stored.Id == project.Id);
    }

    protected async Task<List<ProjectMember>> MembersOf(Guid projectId)
    {
        await using var db = Postgres.CreateContext();
        return await db.ProjectMembers.Where(member => member.ProjectId == projectId).ToListAsync();
    }
}
