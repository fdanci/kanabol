using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Controllers;

public sealed record AddProjectMemberRequest(
    Guid UserId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))] ProjectRole Role);
