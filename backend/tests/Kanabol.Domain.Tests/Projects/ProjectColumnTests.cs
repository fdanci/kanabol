using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Projects;

/// <summary>
/// A project's columns (UI spec section 7.4): added at the end, renamed, marked done, moved, and removed
/// only when empty and not the last one. The places stay 0, 1, 2... whatever happens.
/// </summary>
public sealed class ProjectColumnTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn At(int position) => _project.Columns.Single(column => column.Position == position);

    private IEnumerable<string> Names => _project.Columns.OrderBy(column => column.Position).Select(column => column.Name);

    [Fact]
    public void AddColumn_Always_PutsItLastWithItsNameTrimmed()
    {
        var column = _project.AddColumn("  Review ", isDone: true);

        column.Position.Should().Be(3);
        column.Name.Should().Be("Review");
        column.IsDone.Should().BeTrue();
        column.ProjectId.Should().Be(_project.Id);
        Names.Should().Equal("De făcut", "În lucru", "Gata", "Review");
    }

    [Fact]
    public void AddColumn_WithABlankName_Throws()
    {
        var add = () => _project.AddColumn("  ", isDone: false);

        add.Should().Throw<ArgumentException>();
        _project.Columns.Should().HaveCount(3);
    }

    [Fact]
    public void RenameColumn_Always_ChangesOnlyItsName()
    {
        var change = _project.RenameColumn(At(1).Id, " Lucrez ");

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal("De făcut", "Lucrez", "Gata");
        At(1).IsDone.Should().BeFalse();
    }

    [Fact]
    public void MarkColumnDone_Always_SetsTheFlagEitherWay()
    {
        _project.MarkColumnDone(At(1).Id, isDone: true).Should().Be(ColumnChange.Done);
        At(1).IsDone.Should().BeTrue();

        _project.MarkColumnDone(At(2).Id, isDone: false).Should().Be(ColumnChange.Done);
        At(2).IsDone.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, new[] { "Gata", "De făcut", "În lucru" })]
    [InlineData(1, new[] { "De făcut", "Gata", "În lucru" })]
    [InlineData(2, new[] { "De făcut", "În lucru", "Gata" })]
    [InlineData(9, new[] { "De făcut", "În lucru", "Gata" })]
    public void MoveColumn_ToAPlace_PutsItThereAndTheOthersAroundIt(int position, string[] expected)
    {
        var change = _project.MoveColumn(At(2).Id, position);

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal(expected);
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1, 2);
    }

    [Fact]
    public void MoveColumn_ToANegativePlace_Throws()
    {
        var move = () => _project.MoveColumn(At(0).Id, -1);

        move.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RemoveColumn_WhenEmpty_TakesItOutAndClosesTheGap()
    {
        var change = _project.RemoveColumn(At(1).Id, holdsTasks: false);

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal("De făcut", "Gata");
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1);
    }

    [Fact]
    public void RemoveColumn_ThatHoldsTasks_IsRefused()
    {
        _project.RemoveColumn(At(0).Id, holdsTasks: true).Should().Be(ColumnChange.NotEmpty);

        _project.Columns.Should().HaveCount(3);
    }

    [Fact]
    public void RemoveColumn_TheLastOne_IsRefused()
    {
        _project.RemoveColumn(At(2).Id, holdsTasks: false);
        _project.RemoveColumn(At(1).Id, holdsTasks: false);

        _project.RemoveColumn(At(0).Id, holdsTasks: false).Should().Be(ColumnChange.LastColumn);
        Names.Should().Equal("De făcut");
    }

    [Fact]
    public void ColumnChanges_OnAColumnOfAnotherProject_AreNotFound()
    {
        var elsewhere = new ProjectBuilder().Build().Columns.First().Id;

        _project.RenameColumn(elsewhere, "X").Should().Be(ColumnChange.NotFound);
        _project.MarkColumnDone(elsewhere, isDone: true).Should().Be(ColumnChange.NotFound);
        _project.MoveColumn(elsewhere, 0).Should().Be(ColumnChange.NotFound);
        _project.RemoveColumn(elsewhere, holdsTasks: false).Should().Be(ColumnChange.NotFound);
    }

    // SPEC section 5.1: a task in a column that becomes done is completed, and open again when it stops being one.
    [Fact]
    public void FollowColumn_WhenItsColumnBecomesDoneAndStops_CompletesTheTaskThenOpensIt()
    {
        var task = new TaskItemBuilder().InProject(_project).InColumn(At(1)).Build();

        _project.MarkColumnDone(At(1).Id, isDone: true);
        task.FollowColumn(At(1), _now);
        task.CompletedAt.Should().Be(_now);

        task.FollowColumn(At(1), _now.AddHours(1));
        task.CompletedAt.Should().Be(_now, "it was completed already");

        _project.MarkColumnDone(At(1).Id, isDone: false);
        task.FollowColumn(At(1), _now.AddHours(2));
        task.CompletedAt.Should().BeNull();
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void FollowColumn_OfAnotherColumn_Throws()
    {
        var task = new TaskItemBuilder().InProject(_project).InColumn(At(0)).Build();

        var follow = () => task.FollowColumn(At(2), _now);

        follow.Should().Throw<ArgumentException>();
    }
}
