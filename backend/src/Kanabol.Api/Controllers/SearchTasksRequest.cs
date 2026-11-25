using Kanabol.Application.Features.Search.Commands.SearchTasks;

namespace Kanabol.Api.Controllers;

/// <summary>The question and the optional filters; created in [from, to), and archived tasks only with includeArchived.</summary>
public sealed record SearchTasksRequest(
    string Question,
    Guid? ProjectId = null,
    Guid? AssigneeId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeArchived = false,
    int Limit = SearchTasksCommand.DefaultLimit);
