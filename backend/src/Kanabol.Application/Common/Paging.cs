namespace Kanabol.Application.Common;

public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    // So the offset of the last page still fits in an int.
    public const int MaximumPageNumber = int.MaxValue / MaximumPageSize;
}
