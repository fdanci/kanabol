using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Search.Commands.SearchTasks;

/// <summary>
/// The trial search by meaning (SPEC section 19.6): the tasks nearest in meaning to Question, with their distance,
/// among those the optional filters let through: a project, an assignee, created in [From, To), and archived ones
/// with IncludeArchived. A command, not a query: vectorizing the question is a call to the model, which the search
/// records in embedding_calls (section 19.5).
/// </summary>
public sealed record SearchTasksCommand(
    string Question,
    Guid? ProjectId = null,
    Guid? AssigneeId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeArchived = false,
    int Limit = SearchTasksCommand.DefaultLimit) : ICommand<FoundTasksDto>
{
    public const int MaxQuestionLength = 1000;

    public const int DefaultLimit = 10;

    public const int MaxLimit = 50;
}

/// <summary>The tasks found, nearest first, and the tokens the question used.</summary>
public sealed record FoundTasksDto(IReadOnlyList<FoundTaskDto> Tasks, int Tokens);

/// <summary>
/// A task found, with enough to open it and its cosine distance to the question: 0 for the same meaning, larger as
/// the texts drift apart. Archived when the task or its project is.
/// </summary>
public sealed record FoundTaskDto(
    Guid Id,
    string Key,
    string Title,
    Guid ProjectId,
    string ProjectKey,
    string ProjectName,
    bool Archived,
    double Distance);
