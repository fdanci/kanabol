using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects.Commands.ChangeProjectMemberRole;

public sealed record ChangeProjectMemberRoleCommand(Guid ProjectId, Guid UserId, ProjectRole Role) : ICommand<ProjectMemberDto>;
