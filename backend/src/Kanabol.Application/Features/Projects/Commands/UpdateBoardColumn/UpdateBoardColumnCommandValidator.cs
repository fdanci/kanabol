using FluentValidation;

namespace Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;

public sealed class UpdateBoardColumnCommandValidator : AbstractValidator<UpdateBoardColumnCommand>
{
    public UpdateBoardColumnCommandValidator() => RuleFor(command => command.Name).IsAColumnName();
}
