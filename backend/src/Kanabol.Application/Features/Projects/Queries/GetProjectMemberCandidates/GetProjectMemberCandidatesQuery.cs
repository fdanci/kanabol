using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMemberCandidates;

/// <summary>Who an owner can add, matching Search in the email or the name when there is one.</summary>
public sealed record GetProjectMemberCandidatesQuery(Guid ProjectId, string? Search) : IQuery<MemberCandidateDto[]>
{
    public const int MaxSearchLength = 100;
    public const int MaxResults = 20;
}
