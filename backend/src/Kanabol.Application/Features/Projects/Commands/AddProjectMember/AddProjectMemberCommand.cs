using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects.Commands.AddProjectMember;

public sealed record AddProjectMemberCommand(Guid ProjectId, Guid UserId, ProjectRole Role) : ICommand<ProjectMemberDto>;
