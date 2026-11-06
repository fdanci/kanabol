using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Projects;

/// <summary>Calls the project endpoints as users who really signed in.</summary>
public abstract class ProjectApiTest(ApiFixture api) : ManagementApiTest(api)
{
    protected const string Projects = "/api/projects";

    protected async Task<(User User, string Token)> SignedIn(params string[] permissions)
    {
        var user = await CreateUser(UnusedEmail(), permissions: permissions);
        return (user, await AccessTokenFrom(await Login(user.Email)));
    }

    protected async Task<Project> CreateProject(Func<ProjectBuilder, ProjectBuilder>? configure = null)
    {
        var project = (configure ?? (builder => builder))(new ProjectBuilder()).Build();

        await using var db = Api.Postgres.CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    protected async Task ArchiveInDatabase(Project project)
    {
        await using var db = Api.Postgres.CreateContext();
        await db.Projects.Where(stored => stored.Id == project.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(stored => stored.ArchivedAt, DateTimeOffset.UtcNow));
    }

    protected async Task<List<ProjectMember>> MembersOf(Guid projectId)
    {
        await using var db = Api.Postgres.CreateContext();
        return await db.ProjectMembers.Where(member => member.ProjectId == projectId).ToListAsync();
    }
}
