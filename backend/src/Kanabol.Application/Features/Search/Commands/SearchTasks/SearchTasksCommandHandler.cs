using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Search;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Features.Search.Commands.SearchTasks;

/// <summary>
/// For chat.use, checked on the endpoint, which reads over every project on its own (SPEC section 19.6), archived
/// tasks too when asked. The question is vectorized by the model the tasks were, and only that model's vectors are
/// compared. A project asked for has to exist; an assignee can be anyone, as in the reports. The question's call is
/// recorded before the search, so it counts whatever comes after. The logs hold numbers, never the question.
/// </summary>
public sealed class SearchTasksCommandHandler(
    IAppDbContext db,
    IEmbeddingModel model,
    ITaskSearch search,
    IOptions<EmbeddingsOptions> options,
    IClock clock,
    ILogger<SearchTasksCommandHandler> logger) : ICommandHandler<SearchTasksCommand, FoundTasksDto>
{
    public async Task<Result<FoundTasksDto>> Handle(SearchTasksCommand command, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return SearchErrors.TurnedOff;
        }

        if (command.ProjectId is { } asked
            && !await db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Id == asked, ct))
        {
            return ProjectErrors.NotFound;
        }

        var question = model.Fit(command.Question);
        if (question.Length < command.Question.Length)
        {
            logger.LogWarning("The search's question is over the model's limit: only its start goes to the model");
        }

        EmbeddedTexts answer;
        try
        {
            answer = await model.EmbedAsync([question], ct);
            if (answer.Vectors.Count != 1)
            {
                throw new EmbeddingFailedException($"The model answered {answer.Vectors.Count} vectors for one question.");
            }
        }
        catch (EmbeddingFailedException failure)
        {
            // The message has the status, not the question.
            logger.LogWarning("The search's question couldn't be vectorized: {Reason}", failure.Message);
            return SearchErrors.ModelFailed;
        }

        db.EmbeddingCalls.Add(new EmbeddingCall(EmbeddingPurpose.Search, model.Name, 1, answer.Tokens, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        var nearest = await search.NearestAsync(Filtered(command), answer.Vectors[0], model.Name, command.Limit, ct);
        var found = await Found(nearest, ct);
        logger.LogInformation(
            "The search found {TaskCount} tasks, with {TokenCount} tokens for the question",
            found.Count,
            answer.Tokens);
        return new FoundTasksDto(found, answer.Tokens);
    }

    /// <summary>
    /// The ids the filters let through, as a query for the search to put inside its own. Without IncludeArchived, an
    /// archived task and every task of an archived project stay out.
    /// </summary>
    private IQueryable<Guid> Filtered(SearchTasksCommand command)
    {
        var tasks = db.TaskItems.IgnoreQueryFilters();
        if (!command.IncludeArchived)
        {
            tasks = tasks.Where(task => task.ArchivedAt == null
                && db.Projects.IgnoreQueryFilters().Any(project => project.Id == task.ProjectId && project.ArchivedAt == null));
        }

        if (command.ProjectId is { } projectId)
        {
            tasks = tasks.Where(task => task.ProjectId == projectId);
        }

        if (command.AssigneeId is { } assigneeId)
        {
            tasks = tasks.Where(task => task.Assignees.Any(assignee => assignee.UserId == assigneeId));
        }

        if (command.From is { } from)
        {
            tasks = tasks.Where(task => task.CreatedAt >= from);
        }

        if (command.To is { } to)
        {
            tasks = tasks.Where(task => task.CreatedAt < to);
        }

        return tasks.Select(task => task.Id);
    }

    /// <summary>The tasks behind the distances, in the search's order.</summary>
    private async Task<List<FoundTaskDto>> Found(IReadOnlyList<TaskDistance> nearest, CancellationToken ct)
    {
        var ids = nearest.Select(found => found.TaskId).ToList();
        var rows = await (
                from task in db.TaskItems.IgnoreQueryFilters()
                join project in db.Projects.IgnoreQueryFilters() on task.ProjectId equals project.Id
                where ids.Contains(task.Id)
                select new
                {
                    task.Id,
                    task.Number,
                    task.Title,
                    task.ProjectId,
                    ProjectKey = project.Key,
                    ProjectName = project.Name,
                    Archived = task.ArchivedAt != null || project.ArchivedAt != null,
                })
            .ToDictionaryAsync(row => row.Id, ct);

        return
        [
            .. nearest
                .Where(found => rows.ContainsKey(found.TaskId))
                .Select(found =>
                {
                    var row = rows[found.TaskId];
                    return new FoundTaskDto(
                        row.Id,
                        TaskKey.Format(row.ProjectKey, row.Number),
                        row.Title,
                        row.ProjectId,
                        row.ProjectKey,
                        row.ProjectName,
                        row.Archived,
                        found.Distance);
                }),
        ];
    }
}
