namespace Kanabol.Application.Abstractions;

/// <summary>
/// The search by meaning in SQL (SPEC section 19.6): the tasks nearest to a question's vector, among those the
/// filters let through, by pgvector's cosine distance. The filters are the caller's LINQ, and the implementation
/// adds the distance in the same query, since only Infrastructure knows pgvector. Only the vectors of the model
/// given are compared.
/// </summary>
public interface ITaskSearch
{
    Task<IReadOnlyList<TaskDistance>> NearestAsync(
        IQueryable<Guid> taskIds,
        IReadOnlyList<float> question,
        string model,
        int limit,
        CancellationToken ct);
}

/// <summary>0 for the same meaning, larger as the texts drift apart; at most 2.</summary>
public sealed record TaskDistance(Guid TaskId, double Distance);
