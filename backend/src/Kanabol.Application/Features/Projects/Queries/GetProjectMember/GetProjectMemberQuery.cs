using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMember;

public sealed record GetProjectMemberQuery(Guid ProjectId, Guid UserId) : IQuery<ProjectMemberDto>;
