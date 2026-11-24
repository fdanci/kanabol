using Kanabol.Domain.Search;

namespace Kanabol.Domain.Tests.Search;

/// <summary>
/// The vector of a task's text (SPEC section 19.2): it keeps the text, the vector and the model, copies the
/// vector it gets, and refuses an empty text, vector or model. Replace makes it again in place.
/// </summary>
public sealed class TaskEmbeddingTests
{
    private static readonly Guid _task = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_KeepsTheTextTheVectorAndTheModel()
    {
        var row = new TaskEmbedding(_task, EmbeddingSource.Task, "Raport lunar\n\nPentru client", [0.1f, -0.2f, 0.3f], "text-embedding-3-small", _now);

        row.Should().BeEquivalentTo(new
        {
            TaskId = _task,
            Source = EmbeddingSource.Task,
            Content = "Raport lunar\n\nPentru client",
            Model = "text-embedding-3-small",
            EmbeddedAt = _now,
        });
        row.Embedding.Should().Equal(0.1f, -0.2f, 0.3f);
    }

    [Fact]
    public void Create_CopiesTheVector_SoTheCallerCantChangeItAfterwards()
    {
        float[] numbers = [1f, 2f];

        var row = new TaskEmbedding(_task, EmbeddingSource.Task, "Text", numbers, "model", _now);
        numbers[0] = 9f;

        row.Embedding.Should().Equal(1f, 2f);
    }

    [Theory]
    [InlineData("", "model")]
    [InlineData("  ", "model")]
    [InlineData("Text", "")]
    [InlineData("Text", " ")]
    public void Create_WithoutATextOrAModel_Throws(string content, string model)
    {
        var create = () => new TaskEmbedding(_task, EmbeddingSource.Task, content, [1f], model, _now);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithAnEmptyVector_Throws()
    {
        var create = () => new TaskEmbedding(_task, EmbeddingSource.Task, "Text", [], "model", _now);

        create.Should().Throw<ArgumentException>().WithParameterName("embedding");
    }

    [Fact]
    public void Replace_WithAnotherTextAndModel_KeepsTheRowAndChangesTheRest()
    {
        var row = new TaskEmbedding(_task, EmbeddingSource.Task, "Vechi", [1f, 0f], "model-a", _now);
        var id = row.Id;

        row.Replace("Nou", [0f, 1f, 0f], "model-b", _now.AddHours(1));

        row.Should().BeEquivalentTo(new { Id = id, TaskId = _task, Content = "Nou", Model = "model-b", EmbeddedAt = _now.AddHours(1) });
        row.Embedding.Should().Equal(0f, 1f, 0f);
    }
}
