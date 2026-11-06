using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;

public sealed record RemoveProjectMemberCommand(Guid ProjectId, Guid UserId) : ICommand<Unit>;
