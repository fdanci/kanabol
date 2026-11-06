using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.RestoreProject;

public sealed record RestoreProjectCommand(Guid Id) : ICommand<ProjectDto>;
