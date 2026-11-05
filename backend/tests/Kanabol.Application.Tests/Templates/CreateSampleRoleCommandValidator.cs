using FluentValidation;

namespace Kanabol.Application.Tests.Templates;

public sealed class CreateSampleRoleCommandValidator : AbstractValidator<CreateSampleRoleCommand>
{
    public CreateSampleRoleCommandValidator() => RuleFor(command => command.Name).NotEmpty();
}
