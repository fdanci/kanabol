using Kanabol.Application.Abstractions;
using Kanabol.Domain.Search;
using Kanabol.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Kanabol.Infrastructure.Search;

/// <summary>
/// The search's SQL (SPEC section 19.6): the filters' ids go in as task_id IN (SELECT ...), and the rows come out
/// by embedding &lt;=&gt; the question, nearest first, ties by task. The context is the one IAppDbContext gives the
/// handler, so the filters' query composes into this one.
/// </summary>
public sealed class TaskSearch(AppDbContext db) : ITaskSearch
{
    public async Task<IReadOnlyList<TaskDistance>> NearestAsync(
        IQueryable<Guid> taskIds,
        IReadOnlyList<float> question,
        string model,
        int limit,
        CancellationToken ct)
    {
        // As a Vector in a local: a float[] would go out as real[], which <=> doesn't take.
        var vector = new Vector(question.ToArray());
        return await db.TaskEmbeddings
            .Where(row => row.Source == EmbeddingSource.Task && row.Model == model && taskIds.Contains(row.TaskId))
            .Select(row => new
            {
                row.TaskId,
                Distance = EF.Property<Vector>(row, nameof(TaskEmbedding.Embedding)).CosineDistance(vector),
            })
            .OrderBy(row => row.Distance)
            .ThenBy(row => row.TaskId)
            .Take(limit)
            .Select(row => new TaskDistance(row.TaskId, row.Distance))
            .ToListAsync(ct);
    }
}
