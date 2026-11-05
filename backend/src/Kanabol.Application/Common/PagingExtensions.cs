using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Common;

public static class PagingExtensions
{
    public static IRuleBuilderOptions<T, int> IsAPageNumber<T>(this IRuleBuilderInitial<T, int> rule) =>
        rule.InclusiveBetween(1, Paging.MaximumPageNumber);

    public static IRuleBuilderOptions<T, int> IsAPageSize<T>(this IRuleBuilderInitial<T, int> rule) =>
        rule.InclusiveBetween(1, Paging.MaximumPageSize);

    /// <summary>Order the rows before this, or the same row can turn up on two pages.</summary>
    public static async Task<Page<T>> ToPage<T>(this IQueryable<T> rows, int pageNumber, int pageSize, CancellationToken ct)
    {
        var totalCount = await rows.CountAsync(ct);
        var items = await rows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new Page<T>(items, pageNumber, pageSize, totalCount);
    }
}
