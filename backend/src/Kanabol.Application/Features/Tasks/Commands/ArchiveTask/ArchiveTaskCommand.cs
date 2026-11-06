using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Commands.ArchiveTask;

public sealed record ArchiveTaskCommand(Guid TaskId) : ICommand<Unit>;
