namespace Kanabol.Domain.Projects;

/// <summary>What a change to a project's members came to. Expected refusals are answers, not exceptions.</summary>
public enum MembershipChange
{
    Done,
    NotAMember,
    AlreadyAMember,
    LastOwner,
}
