using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects;

/// <summary>
/// A row of the project list (UI spec section 7.3). MemberPreview has the first four members, owners
/// first, for the avatars, and MemberCount all of them. Open tasks are those outside a done column and
/// overdue ones are open past their deadline, and finished ones have completed_at, none counting archived
/// tasks: the main panel's progress is finished / (open + finished). UpdatedAt is the last change to the
/// project or to one of its tasks.
/// </summary>
public sealed record ProjectSummaryDto(
    Guid Id,
    string Name,
    string Key,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))] ProjectRole? MyRole,
    IReadOnlyList<MemberSummaryDto> Owners,
    IReadOnlyList<MemberSummaryDto> MemberPreview,
    int MemberCount,
    int OpenTaskCount,
    int OverdueTaskCount,
    int CompletedTaskCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);
