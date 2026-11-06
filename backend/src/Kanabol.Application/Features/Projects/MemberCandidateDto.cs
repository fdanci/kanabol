namespace Kanabol.Application.Features.Projects;

/// <summary>An active user who isn't in the project yet, whom an owner can add.</summary>
public sealed record MemberCandidateDto(Guid Id, string Email, string DisplayName);
