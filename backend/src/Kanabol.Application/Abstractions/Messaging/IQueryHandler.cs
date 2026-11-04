using Kanabol.Application.Common;

namespace Kanabol.Application.Abstractions.Messaging;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken ct);
}
