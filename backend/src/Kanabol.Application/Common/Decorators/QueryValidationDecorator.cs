using FluentValidation;
using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Common.Decorators;

public sealed class QueryValidationDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<Result<TResult>> Handle(TQuery query, CancellationToken ct)
    {
        var error = await Validation.Run(query, validators, ct);
        if (error is not null)
        {
            return error;
        }

        return await inner.Handle(query, ct);
    }
}
