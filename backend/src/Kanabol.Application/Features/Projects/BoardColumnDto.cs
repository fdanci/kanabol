using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects;

/// <summary>A column of the board, with its kind as text: Backlog, InProgress, Paused, Done or Custom (SPEC section 4.4).</summary>
public sealed record BoardColumnDto(
    Guid Id,
    string Name,
    int Position,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ColumnKind>))] ColumnKind Kind);
