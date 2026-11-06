using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;

namespace Kanabol.Api.Controllers;

public sealed record ChangeProjectMemberRoleRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ProjectRole>))] ProjectRole Role);
