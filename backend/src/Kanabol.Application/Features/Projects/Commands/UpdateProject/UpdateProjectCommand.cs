using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.UpdateProject;

/// <summary>The key isn't here: it never changes, so the keys of the project's tasks stay valid.</summary>
public sealed record UpdateProjectCommand(Guid Id, string Name, string? Description) : ICommand<ProjectDto>;
