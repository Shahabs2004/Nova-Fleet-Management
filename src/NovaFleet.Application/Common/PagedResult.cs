namespace NovaFleet.Application.Common;

/// <summary>یک صفحه از داده همراه تعداد کل؛ شماره صفحه از ۱ شروع می‌شود.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}

/// <summary>محدودکردن ورودی صفحه‌بندی به بازه امن.</summary>
public static class Paging
{
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Normalize(int page, int pageSize)
        => (Math.Max(page, 1), Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, MaxPageSize));
}
