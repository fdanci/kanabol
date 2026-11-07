using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Database;

public sealed class TestDataPersistenceTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    [Fact]
    public async Task Builders_WhenSaved_FormAGraphThePostgresSchemaAccepts()
    {
        var role = new Role("Member", isSystem: false);
        var owner = new UserBuilder().InRole(role).Build();
        var project = new ProjectBuilder().OwnedBy(owner).Build();
        var task = new TaskItemBuilder().InProject(project).AssignedTo(owner).CreatedBy(owner).Build();

        await using (var db = Postgres.CreateContext())
        {
            db.AddRange(role, owner, project, task);
            await db.SaveChangesAsync();
        }

        await using var read = Postgres.CreateContext();
        var stored = await read.Projects.Include(p => p.Columns).Include(p => p.Members).SingleAsync();
        stored.Columns.Should().HaveCount(3);
        stored.Members.Should().ContainSingle(member => member.UserId == owner.Id);
        (await read.TaskItems.SingleAsync()).Assignees.Should().ContainSingle(assignee => assignee.UserId == owner.Id);
    }
}
