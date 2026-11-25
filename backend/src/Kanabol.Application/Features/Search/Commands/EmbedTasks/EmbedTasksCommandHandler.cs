using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Features.Search.Commands.EmbedTasks;

/// <summary>
/// Makes the vectors that are missing or out of date (SPEC section 19.5): a task with no vector, one whose stored
/// text is no longer the one TaskText composes now, or one made by another model than Embeddings:Model. Archived
/// tasks too, since the search can take them in. The database finds them, in batches ordered by the task's id;
/// each batch is one call to the model and one save, so a failed call keeps the batches before it, and the next run
/// starts again from the first task still to do. A text over the model's limit is cut by the model's Fit and the
/// log names its task. The logs hold numbers and ids, never a text or a vector.
/// </summary>
public sealed class EmbedTasksCommandHandler(
    IAppDbContext db,
    IEmbeddingModel model,
    IOptions<EmbeddingsOptions> options,
    IClock clock,
    ILogger<EmbedTasksCommandHandler> logger) : ICommandHandler<EmbedTasksCommand, EmbeddedTasks>
{
    public async Task<Result<EmbeddedTasks>> Handle(EmbedTasksCommand command, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return EmbeddingErrors.TurnedOff;
        }

        var done = new EmbeddedTasks(0, 0, 0, 0);
        Guid? after = null;
        while (true)
        {
            var batch = await Due(after).OrderBy(text => text.TaskId).Take(command.BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            // Each task at most once a run: one edited while its batch was at the model is still due, for the next run.
            after = batch[^1].TaskId;

            var sent = new List<string>(batch.Count);
            var shortened = 0;
            foreach (var text in batch)
            {
                var fitted = model.Fit(text.Text);
                if (fitted.Length < text.Text.Length)
                {
                    shortened++;
                    logger.LogWarning("The text of task {TaskId} is over the model's limit: only its start goes to the model", text.TaskId);
                }

                sent.Add(fitted);
            }

            EmbeddedTexts answer;
            try
            {
                answer = await model.EmbedAsync(sent, ct);
                if (answer.Vectors.Count != batch.Count)
                {
                    throw new EmbeddingFailedException($"The model answered {answer.Vectors.Count} vectors for {batch.Count} texts.");
                }
            }
            catch (EmbeddingFailedException failure)
            {
                // The message has the status, not the texts; the inner exception, with Azure's answer, stays out of the log.
                logger.LogWarning(
                    "The vectorization stopped at batch {BatchNumber}: {Reason} Saved before it: {TaskCount} vectors in {BatchCount} batches, {TokenCount} tokens",
                    done.Batches + 1,
                    failure.Message,
                    done.Tasks,
                    done.Batches,
                    done.Tokens);
                return EmbeddingErrors.ModelFailed;
            }

            await Save(batch, answer.Vectors, ct);
            done = new EmbeddedTasks(done.Tasks + batch.Count, done.Batches + 1, done.Tokens + answer.Tokens, done.Shortened + shortened);
            logger.LogDebug(
                "Vectorization batch {BatchNumber}: {TaskCount} vectors, {TokenCount} tokens",
                done.Batches,
                batch.Count,
                answer.Tokens);
        }

        logger.LogInformation(
            "The vectorization made {TaskCount} vectors in {BatchCount} batches, with {TokenCount} tokens; {ShortenedCount} texts were cut to the model's limit",
            done.Tasks,
            done.Batches,
            done.Tokens,
            done.Shortened);
        return done;
    }

    /// <summary>
    /// The tasks whose vector is missing or out of date, after the given id. The comparison takes the title's
    /// collation, ro-x-icu, which is deterministic: PostgreSQL compares the bytes, so a change of a single letter's
    /// case or accent counts.
    /// </summary>
    private IQueryable<TaskText> Due(Guid? after)
    {
        var modelName = model.Name;
        var texts = db.TaskItems.IgnoreQueryFilters().Select(TaskText.Of);
        if (after is { } last)
        {
            texts = texts.Where(text => text.TaskId > last);
        }

        return texts.Where(text => !db.TaskEmbeddings.Any(row => row.TaskId == text.TaskId
            && row.Source == EmbeddingSource.Task
            && row.Model == modelName
            && row.Content == text.Text));
    }

    /// <summary>
    /// The batch's vectors in one save, with the whole text kept even when only its start was sent: the next run
    /// compares the text, so a cut one isn't sent again until it changes. The rows stop being tracked after the save,
    /// so a long run doesn't hold every vector it made.
    /// </summary>
    private async Task Save(List<TaskText> batch, IReadOnlyList<IReadOnlyList<float>> vectors, CancellationToken ct)
    {
        var ids = batch.Select(text => text.TaskId).ToList();
        var rows = await db.TaskEmbeddings
            .Where(row => ids.Contains(row.TaskId) && row.Source == EmbeddingSource.Task)
            .ToDictionaryAsync(row => row.TaskId, ct);

        var now = clock.UtcNow;
        for (var i = 0; i < batch.Count; i++)
        {
            var text = batch[i];
            if (rows.TryGetValue(text.TaskId, out var row))
            {
                row.Replace(text.Text, vectors[i], model.Name, now);
            }
            else
            {
                row = new TaskEmbedding(text.TaskId, EmbeddingSource.Task, text.Text, vectors[i], model.Name, now);
                db.TaskEmbeddings.Add(row);
                rows[text.TaskId] = row;
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var row in rows.Values)
        {
            db.TaskEmbeddings.Entry(row).State = EntityState.Detached;
        }
    }
}
