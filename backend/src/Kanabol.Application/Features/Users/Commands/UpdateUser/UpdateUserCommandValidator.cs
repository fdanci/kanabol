using FluentValidation;

namespace Kanabol.Application.Features.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(command => command.Email).IsAnEmail();
        RuleFor(command => command.DisplayName).IsADisplayName();
    }
}
