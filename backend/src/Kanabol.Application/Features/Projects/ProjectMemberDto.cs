using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects;

public sealed record ProjectMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))] ProjectRole Role,
    bool IsActive);
