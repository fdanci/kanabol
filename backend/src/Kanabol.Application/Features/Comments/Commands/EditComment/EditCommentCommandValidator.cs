using FluentValidation;

namespace Kanabol.Application.Features.Comments.Commands.EditComment;

public sealed class EditCommentCommandValidator : AbstractValidator<EditCommentCommand>
{
    public EditCommentCommandValidator() => RuleFor(command => command.Body).IsACommentBody();
}
