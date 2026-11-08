using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistoryEntry;

/// <summary>One entry of the task's history, whole: a description's texts too (SPEC section 4.4, "Textele descrierii").</summary>
public sealed record GetTaskHistoryEntryQuery(Guid TaskId, Guid EntryId) : IQuery<TaskHistoryEntryDto>;
