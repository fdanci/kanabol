using Kanabol.Domain.Projects;
using Kanabol.Domain.Projects.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Projects;

/// <summary>
/// A project's columns (SPEC section 4.4, "Coloanele fixe", group 22): Backlog, În lucru, În pauză and Gata
/// in every project, in that order, never renamed, moved or removed; the project's own only between
/// Backlog and Gata, a new one right before Gata. The places stay 0, 1, 2... whatever happens.
/// </summary>
public sealed class ProjectColumnTests
{
    // Who changes the columns, which their event names (group 23).
    private static readonly Guid _by = Guid.CreateVersion7();

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn At(int position) => _project.Columns.Single(column => column.Position == position);

    private BoardColumn Fixed(ColumnKind kind) => _project.Columns.Single(column => column.Kind == kind);

    private IEnumerable<string> Names => _project.Columns.OrderBy(column => column.Position).Select(column => column.Name);

    public static TheoryData<ColumnKind> FixedKinds => [ColumnKind.Backlog, ColumnKind.InProgress, ColumnKind.Paused, ColumnKind.Done];

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
    public void AddColumn_Always_PutsACustomOneRightBeforeGata()
    {
        var review = _project.AddColumn("  Review ", _by);
        var qa = _project.AddColumn("QA", _by);

        review.Name.Should().Be("Review");
        review.Kind.Should().Be(ColumnKind.Custom);
        review.IsFixed.Should().BeFalse();
        review.ProjectId.Should().Be(_project.Id);
        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Review", "QA", "Gata");
        qa.Position.Should().Be(4);
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1, 2, 3, 4, 5);
    }

    [Fact]
    public void AddColumn_WithABlankName_Throws()
    {
        var add = () => _project.AddColumn("  ", _by);

        add.Should().Throw<ArgumentException>();
        _project.Columns.Should().HaveCount(4);
    }

    [Fact]
    public void RenameColumn_OfItsOwn_ChangesOnlyItsName()
    {
        var review = _project.AddColumn("Review", _by);

        var change = _project.RenameColumn(review.Id, " Verificare ", _by);

        change.Should().Be(ColumnChange.Done);
        review.Name.Should().Be("Verificare");
        review.Kind.Should().Be(ColumnKind.Custom);
    }

    [Theory]
    [MemberData(nameof(FixedKinds))]
    public void RenameColumn_AFixedOne_IsRefusedAndKeepsItsName(ColumnKind kind)
    {
        var name = Fixed(kind).Name;

        _project.RenameColumn(Fixed(kind).Id, "Altceva", _by).Should().Be(ColumnChange.Fixed);

        Fixed(kind).Name.Should().Be(name);
    }

    [Theory]
    [InlineData(1, new[] { "Backlog", "Review", "În lucru", "În pauză", "QA", "Gata" })]
    [InlineData(2, new[] { "Backlog", "În lucru", "Review", "În pauză", "QA", "Gata" })]
    [InlineData(4, new[] { "Backlog", "În lucru", "În pauză", "QA", "Review", "Gata" })]
    public void MoveColumn_OfItsOwnBetweenBacklogAndGata_PutsItThereAndTheOthersAroundIt(int position, string[] expected)
    {
        var review = _project.AddColumn("Review", _by);
        _project.AddColumn("QA", _by);

        var change = _project.MoveColumn(review.Id, position, holdsTasks: false, _by);

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal(expected);
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1, 2, 3, 4, 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9)]
    public void MoveColumn_OfItsOwnBeforeBacklogOrAfterGata_IsOutOfRangeAndMovesNothing(int position)
    {
        var review = _project.AddColumn("Review", _by);
        _project.AddColumn("QA", _by);

        _project.MoveColumn(review.Id, position, holdsTasks: false, _by).Should().Be(ColumnChange.OutOfRange);

        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Review", "QA", "Gata");
    }

    [Theory]
    [MemberData(nameof(FixedKinds))]
    public void MoveColumn_AFixedOne_IsRefusedAndMovesNothing(ColumnKind kind)
    {
        _project.AddColumn("Review", _by);

        _project.MoveColumn(Fixed(kind).Id, 2, holdsTasks: false, _by).Should().Be(ColumnChange.Fixed);

        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Review", "Gata");
    }

    // SPEC section 9, group 23: every change of the columns raises ColumnsChanged, with who made it.
    [Fact]
    public void EachChangeOfTheColumns_RaisesColumnsChangedWithWhoMadeIt()
    {
        _project.ClearDomainEvents();

        var review = _project.AddColumn("Review", _by);
        _project.RenameColumn(review.Id, "În review", _by);
        _project.MoveColumn(review.Id, 2, holdsTasks: false, _by);
        _project.RemoveColumn(review.Id, holdsTasks: false, _by);

        _project.DomainEvents.Should().Equal(Enumerable.Repeat(new ColumnsChangedEvent(_project.Id, _by), 4));
    }

    [Fact]
    public void AChangeOfTheColumnsThatIsRefusedOrChangesNothing_RaisesNothing()
    {
        var review = _project.AddColumn("Review", _by);
        _project.ClearDomainEvents();

        _project.RenameColumn(Fixed(ColumnKind.Backlog).Id, "Altceva", _by).Should().Be(ColumnChange.Fixed);
        _project.MoveColumn(Fixed(ColumnKind.Paused).Id, 1, holdsTasks: false, _by).Should().Be(ColumnChange.Fixed);
        _project.MoveColumn(review.Id, 0, holdsTasks: false, _by).Should().Be(ColumnChange.OutOfRange);
        _project.MoveColumn(review.Id, 1, holdsTasks: true, _by).Should().Be(ColumnChange.CrossesInProgress);
        _project.RemoveColumn(review.Id, holdsTasks: true, _by).Should().Be(ColumnChange.NotEmpty);
        _project.RenameColumn(review.Id, " Review ", _by).Should().Be(ColumnChange.Done);
        _project.MoveColumn(review.Id, review.Position, holdsTasks: false, _by).Should().Be(ColumnChange.Done);

        _project.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MoveColumn_ToANegativePlace_Throws()
    {
        var review = _project.AddColumn("Review", _by);

        var move = () => _project.MoveColumn(review.Id, -1, holdsTasks: false, _by);

        move.Should().Throw<ArgumentOutOfRangeException>();
    }

    // SPEC section 4.4, group 23: a column holding tasks, archived ones too, doesn't cross În lucru, either way,
    // so a started task never lands before it with its column, nor one not started after it.
    [Fact]
    public void MoveColumn_HoldingTasksFromAfterInProgressToBeforeIt_CrossesInProgressAndMovesNothing()
    {
        var review = _project.AddColumn("Review", _by);

        _project.MoveColumn(review.Id, 1, holdsTasks: true, _by).Should().Be(ColumnChange.CrossesInProgress);

        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Review", "Gata");
    }

    [Fact]
    public void MoveColumn_HoldingTasksFromBeforeInProgressToAfterIt_CrossesInProgressAndMovesNothing()
    {
        var analysis = _project.AddColumn("Analiză", _by);
        _project.MoveColumn(analysis.Id, 1, holdsTasks: false, _by);

        _project.MoveColumn(analysis.Id, 2, holdsTasks: true, _by).Should().Be(ColumnChange.CrossesInProgress);
        _project.MoveColumn(analysis.Id, 3, holdsTasks: true, _by).Should().Be(ColumnChange.CrossesInProgress);

        Names.Should().Equal("Backlog", "Analiză", "În lucru", "În pauză", "Gata");
    }

    [Theory]
    [InlineData(2, new[] { "Backlog", "În lucru", "Review", "În pauză", "QA", "Gata" })]
    [InlineData(4, new[] { "Backlog", "În lucru", "În pauză", "QA", "Review", "Gata" })]
    public void MoveColumn_HoldingTasksOnItsSideOfInProgress_Moves(int position, string[] expected)
    {
        var review = _project.AddColumn("Review", _by);
        _project.AddColumn("QA", _by);

        _project.MoveColumn(review.Id, position, holdsTasks: true, _by).Should().Be(ColumnChange.Done);

        Names.Should().Equal(expected);
    }

    [Fact]
    public void MoveColumn_HoldingTasksBeforeInProgress_MovesAmongTheOthersThere()
    {
        var analysis = _project.AddColumn("Analiză", _by);
        var design = _project.AddColumn("Design", _by);
        _project.MoveColumn(analysis.Id, 1, holdsTasks: false, _by);
        _project.MoveColumn(design.Id, 2, holdsTasks: false, _by);

        _project.MoveColumn(analysis.Id, 2, holdsTasks: true, _by).Should().Be(ColumnChange.Done);

        Names.Should().Equal("Backlog", "Design", "Analiză", "În lucru", "În pauză", "Gata");
    }

    [Fact]
    public void MoveColumn_Empty_CrossesInProgressEitherWay()
    {
        var review = _project.AddColumn("Review", _by);

        _project.MoveColumn(review.Id, 1, holdsTasks: false, _by).Should().Be(ColumnChange.Done);
        Names.Should().Equal("Backlog", "Review", "În lucru", "În pauză", "Gata");
        _project.MoveColumn(review.Id, 3, holdsTasks: false, _by).Should().Be(ColumnChange.Done);
        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Review", "Gata");
    }

    [Fact]
    public void RemoveColumn_OfItsOwnWhenEmpty_TakesItOutAndClosesTheGap()
    {
        var review = _project.AddColumn("Review", _by);
        _project.MoveColumn(review.Id, 1, holdsTasks: false, _by);

        var change = _project.RemoveColumn(review.Id, holdsTasks: false, _by);

        change.Should().Be(ColumnChange.Done);
        Names.Should().Equal("Backlog", "În lucru", "În pauză", "Gata");
        _project.Columns.Select(column => column.Position).Order().Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void RemoveColumn_OfItsOwnThatHoldsTasks_IsRefused()
    {
        var review = _project.AddColumn("Review", _by);

        _project.RemoveColumn(review.Id, holdsTasks: true, _by).Should().Be(ColumnChange.NotEmpty);

        _project.Columns.Should().HaveCount(5);
    }

    [Theory]
    [MemberData(nameof(FixedKinds))]
    public void RemoveColumn_AFixedOne_IsRefusedEmptyOrNot(ColumnKind kind)
    {
        _project.RemoveColumn(Fixed(kind).Id, holdsTasks: false, _by).Should().Be(ColumnChange.Fixed);
        _project.RemoveColumn(Fixed(kind).Id, holdsTasks: true, _by).Should().Be(ColumnChange.Fixed);

        _project.Columns.Should().HaveCount(4);
    }

    [Fact]
    public void ColumnChanges_OnAColumnOfAnotherProject_AreNotFound()
    {
        var elsewhere = new ProjectBuilder().Build().Columns.First().Id;

        _project.RenameColumn(elsewhere, "X", _by).Should().Be(ColumnChange.NotFound);
        _project.MoveColumn(elsewhere, 1, holdsTasks: false, _by).Should().Be(ColumnChange.NotFound);
        _project.RemoveColumn(elsewhere, holdsTasks: false, _by).Should().Be(ColumnChange.NotFound);
    }

    // The fixed names are taken, in any case: a column of the project's own can't pass for one.
    [Theory]
    [InlineData("Backlog", true)]
    [InlineData("backlog", true)]
    [InlineData(" În lucru ", true)]
    [InlineData("ÎN PAUZĂ", true)]
    [InlineData("gata", true)]
    [InlineData("Review", false)]
    [InlineData("Gata!", false)]
    [InlineData("În lucru 2", false)]
    [InlineData("Arhivate", true)]
    [InlineData(" ARHIVATE ", true)]
    [InlineData("arhivate", true)]
    [InlineData("Arhivate 2", false)]
    [InlineData("Arhivat", false)]
    public void IsAReservedName_SaysWhetherTheNameIsOneOfTheFourOrArhivateInAnyCase(string name, bool isReserved) =>
        BoardColumn.IsAReservedName(name).Should().Be(isReserved);
}
