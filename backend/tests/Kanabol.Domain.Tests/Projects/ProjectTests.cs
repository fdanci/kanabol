using Kanabol.Domain.Projects;

namespace Kanabol.Domain.Tests.Projects;

/// <summary>SPEC sections 4.4 and 5.1: a key of 2 to 5 capitals, the default columns, and always an owner.</summary>
public sealed class ProjectTests
{
    private static readonly Guid _owner = Guid.CreateVersion7();
    private static readonly Guid _other = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Always_MakesTheCreatorItsOnlyOwner()
    {
        var project = Project.Create("Kanabol", "KAN", "Board-ul echipei", _owner);

        project.Members.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ProjectId = project.Id, UserId = _owner, Role = ProjectRole.Owner });
    }

    [Fact]
    public void Create_Always_AddsTheDefaultColumnsInOrderWithGataAsDone()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);

        project.Columns.Select(column => column.Name).Should().Equal("De făcut", "În lucru", "Gata");
        project.Columns.Select(column => column.Position).Should().Equal(0, 1, 2);
        project.Columns.Select(column => column.IsDone).Should().Equal(false, false, true);
        project.Columns.Should().OnlyContain(column => column.ProjectId == project.Id);
    }

    [Fact]
    public void Create_Always_StartsTheTaskNumbersAtOne() =>
        Project.Create("Kanabol", "KAN", null, _owner).NextTaskNumber.Should().Be(1);

    [Fact]
    public void Create_WhenTheTextHasSpacesAround_KeepsItTrimmed()
    {
        var project = Project.Create("  Kanabol ", " kan ", "  Board-ul echipei \n", _owner);

        project.Name.Should().Be("Kanabol");
        project.Key.Should().Be("KAN");
        project.Description.Should().Be("Board-ul echipei");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Create_WhenTheDescriptionIsBlank_HasNone(string? description) =>
        Project.Create("Kanabol", "KAN", description, _owner).Description.Should().BeNull();

    [Theory]
    [InlineData("KA")]
    [InlineData("KANBO")]
    [InlineData("kan")]
    [InlineData(" Kan ")]
    public void IsValidKey_WhenTwoToFiveLetters_IsTrue(string key) => Project.IsValidKey(key).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("K")]
    [InlineData("KANBOL")]
    [InlineData("K1")]
    [InlineData("K-N")]
    [InlineData("ȘȚ")]
    [InlineData("K N")]
    public void IsValidKey_WhenNotTwoToFiveLatinLetters_IsFalse(string key) => Project.IsValidKey(key).Should().BeFalse();

    [Fact]
    public void Constructor_WhenTheKeyIsInvalid_Throws()
    {
        var create = () => new Project("Kanabol", "K1", null);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateDetails_Always_ChangesNameAndDescriptionButNotTheKey()
    {
        var project = Project.Create("Kanabol", "KAN", "Vechi", _owner);

        project.UpdateDetails(" Kanabol v2 ", " ");

        project.Name.Should().Be("Kanabol v2");
        project.Description.Should().BeNull();
        project.Key.Should().Be("KAN");
    }

    [Fact]
    public void AddMember_WhenNew_AddsThemWithTheirRole()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);

        project.AddMember(_other, ProjectRole.Member).Should().Be(MembershipChange.Done);

        project.Members.Should().ContainSingle(member => member.UserId == _other && member.Role == ProjectRole.Member);
    }

    [Fact]
    public void AddMember_WhenAlreadyAMember_RefusesAndKeepsTheirRole()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);

        project.AddMember(_owner, ProjectRole.Member).Should().Be(MembershipChange.AlreadyAMember);

        project.Members.Should().ContainSingle(member => member.UserId == _owner && member.Role == ProjectRole.Owner);
    }

    [Fact]
    public void ChangeMemberRole_WhenAnotherOwnerStays_DemotesTheOwner()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.AddMember(_other, ProjectRole.Owner);

        project.ChangeMemberRole(_owner, ProjectRole.Member).Should().Be(MembershipChange.Done);

        project.Members.Single(member => member.UserId == _owner).Role.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public void ChangeMemberRole_WhenItIsTheLastOwner_RefusesAndKeepsTheOwner()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.AddMember(_other, ProjectRole.Member);

        project.ChangeMemberRole(_owner, ProjectRole.Member).Should().Be(MembershipChange.LastOwner);

        project.Members.Single(member => member.UserId == _owner).Role.Should().Be(ProjectRole.Owner);
    }

    [Fact]
    public void ChangeMemberRole_WhenPromotingAMember_MakesThemAnOwner()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.AddMember(_other, ProjectRole.Member);

        project.ChangeMemberRole(_other, ProjectRole.Owner).Should().Be(MembershipChange.Done);

        project.Members.Count(member => member.Role == ProjectRole.Owner).Should().Be(2);
    }

    [Fact]
    public void ChangeMemberRole_WhenTheRoleIsTheSame_ChangesNothing()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);

        project.ChangeMemberRole(_owner, ProjectRole.Owner).Should().Be(MembershipChange.Done);

        project.Members.Single().Role.Should().Be(ProjectRole.Owner);
    }

    [Fact]
    public void ChangeMemberRole_WhenNotAMember_Refuses() =>
        Project.Create("Kanabol", "KAN", null, _owner).ChangeMemberRole(_other, ProjectRole.Owner).Should().Be(MembershipChange.NotAMember);

    [Fact]
    public void RemoveMember_WhenAMember_RemovesThem()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.AddMember(_other, ProjectRole.Member);

        project.RemoveMember(_other).Should().Be(MembershipChange.Done);

        project.Members.Should().ContainSingle(member => member.UserId == _owner);
    }

    [Fact]
    public void RemoveMember_WhenAnotherOwnerStays_RemovesTheOwner()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.AddMember(_other, ProjectRole.Owner);

        project.RemoveMember(_owner).Should().Be(MembershipChange.Done);

        project.Members.Should().ContainSingle(member => member.UserId == _other && member.Role == ProjectRole.Owner);
    }

    [Fact]
    public void RemoveMember_WhenItIsTheLastOwner_RefusesAndKeepsThem()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.AddMember(_other, ProjectRole.Member);

        project.RemoveMember(_owner).Should().Be(MembershipChange.LastOwner);

        project.Members.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveMember_WhenNotAMember_Refuses() =>
        Project.Create("Kanabol", "KAN", null, _owner).RemoveMember(_other).Should().Be(MembershipChange.NotAMember);

    [Fact]
    public void Archive_Always_RecordsWhenAndWho()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);

        project.Archive(_other, _now);

        project.ArchivedAt.Should().Be(_now);
        project.ArchivedById.Should().Be(_other);
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_KeepsTheFirstTime()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.Archive(_owner, _now);

        project.Archive(_other, _now.AddHours(1));

        project.ArchivedAt.Should().Be(_now);
        project.ArchivedById.Should().Be(_owner);
    }

    [Fact]
    public void Restore_WhenArchived_ClearsTheArchive()
    {
        var project = Project.Create("Kanabol", "KAN", null, _owner);
        project.Archive(_owner, _now);

        project.Restore();

        project.ArchivedAt.Should().BeNull();
        project.ArchivedById.Should().BeNull();
    }
}
