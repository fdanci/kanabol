using FluentValidation;
using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Common.Decorators;

public sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<Result<TResult>> Handle(TCommand command, CancellationToken ct)
    {
        var error = await Validation.Run(command, validators, ct);
        if (error is not null)
        {
            return error;
        }

        return await inner.Handle(command, ct);
    }
}
