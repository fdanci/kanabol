using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Builders;

public sealed class TestDataBuilderTests
{
    [Fact]
    public void UserBuilder_WhenBuiltTwice_GivesEachUserItsOwnEmail()
    {
        var first = new UserBuilder().Build();
        var second = new UserBuilder().Build();

        first.Email.Should().NotBe(second.Email);
        first.IsActive.Should().BeTrue();
    }

    [Fact]
    public void UserBuilder_WhenGivenARole_UsesIt()
    {
        var role = new Role("Manager", isSystem: false);

        var user = new UserBuilder().InRole(role).WithEmail("ana@example.com").MustChangePassword().Build();

        user.RoleId.Should().Be(role.Id);
        user.Email.Should().Be("ana@example.com");
        user.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public void ProjectBuilder_ByDefault_HasTheDefaultColumnsInOrder()
    {
        var project = new ProjectBuilder().Build();

        project.Columns.Select(column => column.Name).Should().Equal("De făcut", "În lucru", "Gata");
        project.Columns.Select(column => column.Position).Should().Equal(0, 1, 2);
        project.Columns.Select(column => column.IsDone).Should().Equal(false, false, true);
        project.Columns.Should().OnlyContain(column => column.ProjectId == project.Id);
    }

    [Fact]
    public void ProjectBuilder_WhenGivenPeople_AddsThemWithTheirRoles()
    {
        var owner = new UserBuilder().Build();
        var member = new UserBuilder().Build();

        var project = new ProjectBuilder().OwnedBy(owner).WithMember(member).Build();

        project.Members.Should().ContainSingle(m => m.UserId == owner.Id && m.Role == ProjectRole.Owner);
        project.Members.Should().ContainSingle(m => m.UserId == member.Id && m.Role == ProjectRole.Member);
    }

    [Fact]
    public void ProjectBuilder_WhenBuiltTwice_GivesEachProjectItsOwnValidKey()
    {
        var first = new ProjectBuilder().Build();
        var second = new ProjectBuilder().Build();

        first.Key.Should().NotBe(second.Key);
        Project.IsValidKey(first.Key).Should().BeTrue();
        Project.IsValidKey(second.Key).Should().BeTrue();
    }

    [Fact]
    public void TaskItemBuilder_WhenInAProject_StartsInItsFirstColumn()
    {
        var project = new ProjectBuilder().Build();
        var assignee = new UserBuilder().Build();
        var dueDate = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

        var task = new TaskItemBuilder().InProject(project).AssignedTo(assignee).DueAt(dueDate).WithPriority(TaskPriority.High).Build();

        task.ProjectId.Should().Be(project.Id);
        task.ColumnId.Should().Be(project.Columns.First().Id);
        task.AssigneeId.Should().Be(assignee.Id);
        task.DueDate.Should().Be(dueDate);
        task.Priority.Should().Be(TaskPriority.High);
    }

    [Fact]
    public void TaskItemBuilder_WhenGivenAColumn_UsesIt()
    {
        var project = new ProjectBuilder().Build();
        var done = project.Columns.Last();

        var task = new TaskItemBuilder().InProject(project).InColumn(done).Build();

        task.ColumnId.Should().Be(done.Id);
    }
}
