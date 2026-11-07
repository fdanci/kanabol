using FluentValidation;

namespace Kanabol.Application.Features.Comments.Commands.AddComment;

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator() => RuleFor(command => command.Body).IsACommentBody();
}
