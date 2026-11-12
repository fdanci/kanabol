using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>Written out, as the frontend and backend/CLAUDE.md know them: a wrong type here would pass every handler test.</summary>
public sealed class ColumnErrorsTests
{
    public static TheoryData<Error, string, ErrorType> Errors => new()
    {
        { ColumnErrors.ManagerRequired, "Column.ManagerRequired", ErrorType.Forbidden },
        { ColumnErrors.NotFound, "Column.NotFound", ErrorType.NotFound },
        { ColumnErrors.NotEmpty, "Column.NotEmpty", ErrorType.Conflict },
        { ColumnErrors.Fixed, "Column.Fixed", ErrorType.Conflict },
        { ColumnErrors.CrossesInProgress, "Column.CrossesInProgress", ErrorType.Conflict },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Error_WhenRead_HasItsCodeAndType(Error error, string code, ErrorType type)
    {
        error.Code.Should().Be(code);
        error.Type.Should().Be(type);
    }
}
