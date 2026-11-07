namespace Kanabol.Application.Features.Tasks;

/// <summary>One of the people a task is assigned to, or who created it.</summary>
public sealed record TaskPersonDto(Guid UserId, string DisplayName);
