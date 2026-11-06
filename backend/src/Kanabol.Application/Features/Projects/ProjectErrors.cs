using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects;

public static class ProjectErrors
{
    public static readonly Error MemberRequired =
        Error.Forbidden("Project.MemberRequired", "You don't have access to this project.");

    public static readonly Error OwnerRequired =
        Error.Forbidden("Project.OwnerRequired", "Only an owner of this project can do this.");

    public static readonly Error NotFound = Error.NotFound("Project.NotFound", "This project doesn't exist.");

    public static readonly Error KeyTaken = Error.Conflict("Project.KeyTaken", "Another project already has this key.");

    public static readonly Error Archived = Error.Conflict("Project.Archived", "This project is archived. Restore it first.");

    public static readonly Error AlreadyAMember = Error.Conflict("Project.AlreadyAMember", "This user is already in the project.");

    public static readonly Error MemberNotFound = Error.NotFound("Project.MemberNotFound", "This user isn't in the project.");

    public static readonly Error LastOwner =
        Error.Conflict("Project.LastOwner", "A project needs an owner. Make someone else an owner first.");

    public static readonly Error ChangedMeanwhile =
        Error.Conflict("Project.ChangedMeanwhile", "This project changed at the same moment. Try again.");

    public static readonly Error UserNotFound = Error.Validation(new Dictionary<string, string[]>
    {
        ["UserId"] = ["This user doesn't exist or is deactivated."],
    });
}
