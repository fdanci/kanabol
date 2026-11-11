using Kanabol.Application.Common;

namespace Kanabol.Application.Abstractions;

public interface IProjectAccess
{
    Task<Result> EnsureMemberAsync(Guid projectId, Guid userId, CancellationToken ct);

    Task<Result> EnsureOwnerAsync(Guid projectId, Guid userId, CancellationToken ct);

    /// <summary>A row in project_members and an active account: no permission stands in for it (SPEC section 4.4).</summary>
    Task<Result> EnsureRealMemberAsync(Guid projectId, Guid userId, CancellationToken ct);

    Task<Result> EnsureColumnManagerAsync(Guid projectId, Guid userId, CancellationToken ct);
}
