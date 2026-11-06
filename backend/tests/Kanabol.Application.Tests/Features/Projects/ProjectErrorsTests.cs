using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>Written out, as the frontend and backend/CLAUDE.md know them: a wrong type here would pass every handler test.</summary>
public sealed class ProjectErrorsTests
{
    public static TheoryData<Error, string, ErrorType> Errors => new()
    {
        { ProjectErrors.MemberRequired, "Project.MemberRequired", ErrorType.Forbidden },
        { ProjectErrors.OwnerRequired, "Project.OwnerRequired", ErrorType.Forbidden },
        { ProjectErrors.NotFound, "Project.NotFound", ErrorType.NotFound },
        { ProjectErrors.KeyTaken, "Project.KeyTaken", ErrorType.Conflict },
        { ProjectErrors.Archived, "Project.Archived", ErrorType.Conflict },
        { ProjectErrors.AlreadyAMember, "Project.AlreadyAMember", ErrorType.Conflict },
        { ProjectErrors.MemberNotFound, "Project.MemberNotFound", ErrorType.NotFound },
        { ProjectErrors.LastOwner, "Project.LastOwner", ErrorType.Conflict },
        { ProjectErrors.ChangedMeanwhile, "Project.ChangedMeanwhile", ErrorType.Conflict },
        { ProjectErrors.UserNotFound, "Validation", ErrorType.Validation },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Error_WhenRead_HasItsCodeAndType(Error error, string code, ErrorType type)
    {
        error.Code.Should().Be(code);
        error.Type.Should().Be(type);
    }
}
