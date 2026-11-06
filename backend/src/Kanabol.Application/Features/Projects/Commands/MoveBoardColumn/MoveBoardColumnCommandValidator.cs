using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;

public sealed class MoveBoardColumnCommandValidator : AbstractValidator<MoveBoardColumnCommand>
{
    public MoveBoardColumnCommandValidator() => RuleFor(command => command.Position).GreaterThanOrEqualTo(0);
}
