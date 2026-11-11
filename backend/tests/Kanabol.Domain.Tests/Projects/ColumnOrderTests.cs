using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Projects;

/// <summary>
/// SPEC section 4.4, "Mutarea între coloane": a task in În lucru, or in a column after it, never goes back to
/// Backlog or to a column of the project's own before În lucru. The rule is on where the task is now, not on
/// where it has been.
/// </summary>
public sealed class ColumnOrderTests
{
    // Who changes the columns, which their event names (group 23).
    private static readonly Guid _by = Guid.CreateVersion7();

    private readonly Project _project = new ProjectBuilder().Build();
    private readonly BoardColumn _before;
    private readonly BoardColumn _after;

    public ColumnOrderTests()
    {
        // Backlog, Analiză, În lucru, În pauză, Review, Gata.
        _before = _project.AddColumn("Analiză", _by);
        _project.MoveColumn(_before.Id, 1, holdsTasks: false, _by);
        _after = _project.AddColumn("Review", _by);
    }

    private BoardColumn Named(string name) => _project.Columns.Single(column => column.Name == name);

    [Theory]
    [InlineData("Backlog", "Analiză")]
    [InlineData("Analiză", "Backlog")]
    [InlineData("Backlog", "În lucru")]
    [InlineData("Analiză", "Gata")]
    [InlineData("În lucru", "În lucru")]
    [InlineData("În lucru", "În pauză")]
    [InlineData("În pauză", "În lucru")]
    [InlineData("Review", "În lucru")]
    [InlineData("Gata", "În lucru")]
    [InlineData("Gata", "Review")]
    public void GoesBackBeforeInProgress_ForwardOrBeforeItOrFromItOnward_IsFalse(string from, string to) =>
        BoardColumn.GoesBackBeforeInProgress(Named(from), Named(to), _project.Columns).Should().BeFalse();

    [Theory]
    [InlineData("În lucru", "Backlog")]
    [InlineData("În lucru", "Analiză")]
    [InlineData("În pauză", "Backlog")]
    [InlineData("Review", "Analiză")]
    [InlineData("Gata", "Backlog")]
    [InlineData("Gata", "Analiză")]
    public void GoesBackBeforeInProgress_FromInProgressOrAfterToAColumnBeforeIt_IsTrue(string from, string to) =>
        BoardColumn.GoesBackBeforeInProgress(Named(from), Named(to), _project.Columns).Should().BeTrue();
}
