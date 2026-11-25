using FluentValidation;

namespace Kanabol.Application.Features.Search.Commands.EmbedTasks;

public sealed class EmbedTasksCommandValidator : AbstractValidator<EmbedTasksCommand>
{
    public EmbedTasksCommandValidator()
    {
        RuleFor(command => command.BatchSize).InclusiveBetween(1, EmbeddingsOptions.MaximumBatchSize);
    }
}
