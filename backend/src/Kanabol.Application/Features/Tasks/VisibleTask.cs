namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// A task the caller sees, and whether it, or its project, is archived. IsRealMember: a row in the project's
/// members, with an active account. Commenting and attaching need an open task and a real member (SPEC
/// section 4.4).
/// </summary>
public sealed record VisibleTask(Guid TaskId, Guid ProjectId, bool TaskArchived, bool ProjectArchived, bool IsRealMember)
{
    public bool IsOpen => !TaskArchived && !ProjectArchived;

    public bool CanAddTo => IsOpen && IsRealMember;
}
