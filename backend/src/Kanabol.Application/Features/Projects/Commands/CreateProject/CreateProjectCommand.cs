using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.CreateProject;

public sealed record CreateProjectCommand(string Name, string Key, string? Description) : ICommand<ProjectDto>;
