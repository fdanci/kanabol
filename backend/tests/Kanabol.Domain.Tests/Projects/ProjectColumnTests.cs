using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Projects;

/// <summary>
/// A project's columns (UI spec section 7.4): the four fixed ones every project starts with (SPEC section
/// 4.4, group 22), and its own, added, renamed, moved, and removed only when empty. The places stay
/// 0, 1, 2... whatever happens.
/// </summary>
public sealed class ProjectColumnTests
{
    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn At(int position) => _project.Columns.Single(column => column.Position == position);

    private IEnumerable<string> Names => _project.Columns.OrderBy(column => column.Position).Select(column => column.Name);

    [Fact]
    public void NewProject_Always_HasEachFixedKindOnceInOrder()
    {
        var project = new Project("Kanabol", "KAN", null);

        project.Columns.OrderBy(column => column.Position).Select(column => (column.Name, column.Kind)).Should().Equal(
            ("Backlog", ColumnKind.Backlog),
            ("În lucru", ColumnKind.InProgress),
            ("În pauză", ColumnKind.Paused),
            ("Gata", ColumnKind.Done));
        project.Columns.Should().OnlyContain(column => column.ProjectId == project.Id && column.IsFixed);
    }

    [Fact]
    public void AddColumn_Always_MakesACustomOneWithItsNameTrimmed()
    {
        var column = _project.AddColumn("  Review ");

        column.Name.Should().Be("Review");
        column.Kind.Should().Be(ColumnKind.Custom);
        column.IsFixed.Should().BeFalse();
        column.ProjectId.Should().Be(_project.Id);
        _project.Columns.Select(other => other.Position).Order().Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void AddColumn_WithABlankName_Throws()
    {
        var add = () => _project.AddColumn("  ");

        add.Should().Throw<ArgumentException>();
        _project.Columns.Should().HaveCount(4);
    }

    [Fact]
    public void RenameColumn_Always_ChangesOnlyItsName()
    {
        var review = _project.AddColumn("Review");

        var change = _project.RenameColumn(review.Id, " Verificare ");

        change.Should().Be(ColumnChange.Done);
        review.Name.Should().Be("Verificare");
        review.Kind.Should().Be(ColumnKind.Custom);
    }

    [Fact]
    public void MoveColumn_ToAPlace_PutsItThereAndTheOthersAroundIt()
    {
        var review = _project.AddColumn("Review");

        var change = _project.MoveColumn(review.Id, 2);

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal("Backlog", "În lucru", "Review", "În pauză", "Gata");
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1, 2, 3, 4);
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
        var review = _project.AddColumn("Review");
        _project.MoveColumn(review.Id, 1);

        var change = _project.RemoveColumn(review.Id, holdsTasks: false);

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Gata");
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void RemoveColumn_ThatHoldsTasks_IsRefused()
    {
        var review = _project.AddColumn("Review");

        _project.RemoveColumn(review.Id, holdsTasks: true).Should().Be(ColumnChange.NotEmpty);

        _project.Columns.Should().HaveCount(5);
    }

    [Fact]
    public void ColumnChanges_OnAColumnOfAnotherProject_AreNotFound()
    {
        var elsewhere = new ProjectBuilder().Build().Columns.First().Id;

        _project.RenameColumn(elsewhere, "X").Should().Be(ColumnChange.NotFound);
        _project.MoveColumn(elsewhere, 0).Should().Be(ColumnChange.NotFound);
        _project.RemoveColumn(elsewhere, holdsTasks: false).Should().Be(ColumnChange.NotFound);
    }
}
