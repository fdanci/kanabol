using Kanabol.Application.Features.Tasks;

namespace Kanabol.Application.Features.Comments;

/// <summary>
/// A comment as the task's panel shows it (UI spec section 7.5). UpdatedAt is null until it's edited, and
/// CanEdit says whether the caller can change it: its author, still a real member of an open project, the
/// task open (SPEC section 4.4).
/// </summary>
public sealed record CommentDto(
    Guid Id,
    Guid TaskId,
    TaskPersonDto Author,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit);
