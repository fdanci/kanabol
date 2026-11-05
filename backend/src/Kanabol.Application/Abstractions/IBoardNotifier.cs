namespace Kanabol.Application.Abstractions;

public interface IBoardNotifier
{
    Task NotifyProjectAsync(Guid projectId, string eventName, object payload, CancellationToken ct);

    Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct);
}
