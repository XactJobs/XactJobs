namespace XactJobs.UI.Services.Models;

public record PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }

    /// <summary>There are more rows than <see cref="TotalCount"/>, which stopped at <c>MaxCount</c> (the scheduled jobs).</summary>
    public bool IsTotalCountCapped { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
