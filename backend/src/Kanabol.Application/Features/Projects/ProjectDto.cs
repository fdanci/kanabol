using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects;

/// <summary>
/// A project as the person asking sees it. MyRole is null for someone who sees it through a permission
/// alone, and CanManage says whether they pass as an owner, which the UI uses to show what they can change.
/// </summary>
public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Key,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))] ProjectRole? MyRole,
    bool CanManage,
    IReadOnlyList<BoardColumnDto> Columns);
