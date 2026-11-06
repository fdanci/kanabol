using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.AddBoardColumn;

public sealed class AddBoardColumnCommandValidator : AbstractValidator<AddBoardColumnCommand>
{
    public AddBoardColumnCommandValidator() => RuleFor(command => command.Name).IsAColumnName();
}
