namespace XactJobs.UI.Pages;

public record PaginationModel(XactJobsPageModel Owner, int Page, int TotalPages, int TotalCount, bool IsTotalCountCapped)
{
    /// <summary>The count, or "10,000+" when it stopped there.</summary>
    public string DisplayTotal => CursorPaginationModel.FormatTotal(TotalCount, IsTotalCountCapped);
}

/// <summary>Newer and older pages of a list read from the last row seen (see <see cref="XactJobsPeriodPageModel"/>).</summary>
internal record CursorPaginationModel(XactJobsPeriodPageModel Owner, string? NewerCursor, string? OlderCursor, int TotalCount, bool IsTotalCountCapped)
{
    public string DisplayTotal => FormatTotal(TotalCount, IsTotalCountCapped);

    public static string FormatTotal(int count, bool isCapped) =>
        count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + (isCapped ? "+" : "");
}
