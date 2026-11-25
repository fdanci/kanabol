using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Search.Commands.SearchTasks;

public sealed class SearchTasksCommandValidator : AbstractValidator<SearchTasksCommand>
{
    public SearchTasksCommandValidator()
    {
        RuleFor(command => command.Question)
            .NotEmpty()
            .MaximumLength(SearchTasksCommand.MaxQuestionLength)
            .HasNoHiddenCharacters();
        RuleFor(command => command.Limit).InclusiveBetween(1, SearchTasksCommand.MaxLimit);

        // Either end may be left out; given both, as in the reports, DateTimeOffset compares instants.
        RuleFor(command => command.To)
            .Must((command, to) => command.From is null || to is null || to > command.From)
            .WithMessage("The interval ends after it starts.");
    }
}
