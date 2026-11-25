using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Search.Commands.EmbedTasks;

/// <summary>
/// One run of the vectorization (SPEC section 19.5), which EmbeddingsWorker sends every Embeddings:Interval, without
/// a user: the vectors of the tasks that have none, whose text changed or that another model made, BatchSize texts
/// to a call.
/// </summary>
public sealed record EmbedTasksCommand(int BatchSize) : ICommand<EmbeddedTasks>;

/// <summary>What a run did, in numbers only: the vectors saved, the calls, the tokens they used and the texts cut to the model's limit.</summary>
public sealed record EmbeddedTasks(int Tasks, int Batches, int Tokens, int Shortened);
