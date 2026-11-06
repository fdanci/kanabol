using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMembers;

public sealed record GetProjectMembersQuery(Guid ProjectId) : IQuery<ProjectMemberDto[]>;
