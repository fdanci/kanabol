using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMemberCandidates;

public sealed class GetProjectMemberCandidatesQueryValidator : AbstractValidator<GetProjectMemberCandidatesQuery>
{
    public GetProjectMemberCandidatesQueryValidator() =>
        RuleFor(query => query.Search!)
            .MaximumLength(GetProjectMemberCandidatesQuery.MaxSearchLength)
            .HasNoHiddenCharacters()
            .When(query => query.Search is not null);
}
