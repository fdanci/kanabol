using Kanabol.Application.Common;

namespace Kanabol.Application.Abstractions;

public interface IProjectAccess
{
    Task<Result> EnsureMemberAsync(Guid projectId, Guid userId, CancellationToken ct);

    Task<Result> EnsureOwnerAsync(Guid projectId, Guid userId, CancellationToken ct);
}
