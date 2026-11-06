using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Api.Controllers;

public sealed record SetTaskPriorityRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority);
