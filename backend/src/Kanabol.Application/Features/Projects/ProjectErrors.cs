using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects;

public static class ProjectErrors
{
    public static readonly Error MemberRequired =
        Error.Forbidden("Project.MemberRequired", "You don't have access to this project.");

    public static readonly Error OwnerRequired =
        Error.Forbidden("Project.OwnerRequired", "Only an owner of this project can do this.");
}
