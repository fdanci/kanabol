using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddProjectMember;
using Kanabol.Application.Features.Projects.Commands.ChangeProjectMemberRole;
using Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;
using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>
/// Two owners, each taking the other one away at the same moment: each request sees the other owner
/// stay, so a count alone would leave the project with none. A change to the members is a change to
/// the project, whose version the second save finds changed.
/// </summary>
public sealed class ConcurrentMemberChangesTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    [Fact]
    public async Task Remove_OfEachOtherByTheLastTwoOwnersAtOnce_KeepsOneOwner()
    {
        var first = await CreateUser();
        var second = await CreateUser("second@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(first).OwnedBy(second));

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<RemoveProjectMemberCommand, Unit>(new RemoveProjectMemberCommand(project.Id, second.Id), first),
            async () => (await HandleInOwnScope<RemoveProjectMemberCommand, Unit>(new RemoveProjectMemberCommand(project.Id, first.Id), second)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        (await MembersOf(project.Id)).Should().ContainSingle(member => member.UserId == second.Id && member.Role == ProjectRole.Owner);
    }

    [Fact]
    public async Task Demote_OfEachOtherByTheLastTwoOwnersAtOnce_KeepsOneOwner()
    {
        var first = await CreateUser();
        var second = await CreateUser("second@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(first).OwnedBy(second));

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<ChangeProjectMemberRoleCommand, ProjectMemberDto>(new ChangeProjectMemberRoleCommand(project.Id, second.Id, ProjectRole.Member), first),
            async () => (await HandleInOwnScope<ChangeProjectMemberRoleCommand, ProjectMemberDto>(new ChangeProjectMemberRoleCommand(project.Id, first.Id, ProjectRole.Member), second)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        (await MembersOf(project.Id)).Count(member => member.Role == ProjectRole.Owner).Should().Be(1);
    }

    [Fact]
    public async Task Add_OfTheSameUserTwiceAtOnce_AddsThemOnce()
    {
        var owner = await CreateUser();
        var user = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<AddProjectMemberCommand, ProjectMemberDto>(new AddProjectMemberCommand(project.Id, user.Id, ProjectRole.Owner), owner),
            async () => (await HandleInOwnScope<AddProjectMemberCommand, ProjectMemberDto>(new AddProjectMemberCommand(project.Id, user.Id, ProjectRole.Member), owner)).IsSuccess.Should().BeTrue());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        (await MembersOf(project.Id)).Should().ContainSingle(member => member.UserId == user.Id && member.Role == ProjectRole.Member);
    }
}
