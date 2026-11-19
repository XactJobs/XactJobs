namespace XactJobs.UI.Services.Models;

/// <summary>
/// A page of a list that is read from the last row seen, newest first: the history, and the finished batches. Unlike page numbers,
/// a page doesn't shift when new rows arrive, and an older page costs the same as the first one.
/// </summary>
public record CursorPageDto<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>The rows of all the pages, at most <c>MaxCount</c>: see <see cref="IsTotalCountCapped"/>.</summary>
    public int TotalCount { get; init; }

    /// <summary>There are more rows than <see cref="TotalCount"/>, which stopped at <c>MaxCount</c>.</summary>
    public bool IsTotalCountCapped { get; init; }

    public int PageSize { get; init; }

    /// <summary>The cursor of the newer rows, or null on the newest page.</summary>
    public string? NewerCursor { get; init; }

    /// <summary>The cursor of the older rows, or null on the oldest page.</summary>
    public string? OlderCursor { get; init; }
}
