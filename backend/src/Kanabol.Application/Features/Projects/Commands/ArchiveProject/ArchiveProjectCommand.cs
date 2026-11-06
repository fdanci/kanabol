using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Commands.ArchiveProject;

public sealed record ArchiveProjectCommand(Guid Id) : ICommand<Unit>;
