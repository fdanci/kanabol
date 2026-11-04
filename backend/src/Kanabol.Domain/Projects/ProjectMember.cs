namespace Kanabol.Domain.Projects;

public sealed class ProjectMember
{
    public ProjectMember(Guid projectId, Guid userId, ProjectRole role)
    {
        ProjectId = projectId;
        UserId = userId;
        Role = role;
    }

    private ProjectMember()
    {
    }

    public Guid ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public ProjectRole Role { get; private set; }
}
