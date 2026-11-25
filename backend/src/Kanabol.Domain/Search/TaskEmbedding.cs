using Kanabol.Domain.Common;

namespace Kanabol.Domain.Search;

/// <summary>
/// The vector of a text from a task, for the search by meaning (SPEC section 19.2): one row per task and
/// source, gone with the task. It keeps the text it was made from and the model that made it, so a changed
/// text or another model in the configuration tells which rows to make again. The vector is a plain list of
/// numbers here; only the EF configuration knows pgvector's type, so the domain stays free of it.
/// </summary>
public sealed class TaskEmbedding : Entity
{
    public TaskEmbedding(Guid taskId, EmbeddingSource source, string content, IReadOnlyList<float> embedding, string model, DateTimeOffset embeddedAt)
    {
        TaskId = taskId;
        Source = source;
        Replace(content, embedding, model, embeddedAt);
    }

    private TaskEmbedding()
    {
    }

    public Guid TaskId { get; private set; }

    public EmbeddingSource Source { get; private set; }

    /// <summary>
    /// The task's text when the vector was made, whole even when the model got only its start (SPEC section 19.5):
    /// it's what a later run compares with the text now.
    /// </summary>
    public string Content { get; private set; } = null!;

    /// <summary>Its length is the model's: the column has no fixed one, since each row keeps its model.</summary>
    public IReadOnlyList<float> Embedding { get; private set; } = [];

    /// <summary>The model's name, not the deployment's: a renamed deployment doesn't make the vectors again.</summary>
    public string Model { get; private set; } = null!;

    public DateTimeOffset EmbeddedAt { get; private set; }

    /// <summary>The vector made again, for a changed text or another model.</summary>
    public void Replace(string content, IReadOnlyList<float> embedding, string model, DateTimeOffset embeddedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (embedding.Count == 0)
        {
            throw new ArgumentException("A vector has at least one number.", nameof(embedding));
        }

        Content = content;

        // A copy: the caller's list can't change the vector afterwards.
        Embedding = [.. embedding];
        Model = model;
        EmbeddedAt = embeddedAt;
    }
}
