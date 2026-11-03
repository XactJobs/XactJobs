using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace XactJobs.UI.Pages;

/// <summary>
/// A dashboard page whose table is also served on its own (the "Table" handler), so that it refreshes without reloading the page.
/// </summary>
public abstract class XactJobsPageModel : PageModel
{
    protected XactJobsPageModel(XactJobsUIOptions options)
    {
        Options = options;
    }

    public XactJobsUIOptions Options { get; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Queue { get; set; }

    /// <summary>
    /// The table of the current page, relative to the current URL.
    /// </summary>
    public string TableUrl => "?handler=Table" + PagingQuery(PageNumber);

    /// <summary>
    /// The query of a page of the table, relative to the current URL.
    /// </summary>
    public string PagingQuery(int pageNumber)
    {
        var query = "&p=" + pageNumber.ToString(CultureInfo.InvariantCulture);

        return string.IsNullOrEmpty(Queue) ? query : query + "&queue=" + Uri.EscapeDataString(Queue);
    }

    public string ApiUrl(string path) => Url.Content("~" + Options.ApiBasePath + path);

    public string PageUrl(string path) => Url.Content("~" + Options.BasePath + path);

    public static string FormatTime(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string FormatTime(DateTime? utc) => utc.HasValue ? FormatTime(utc.Value) : "";

    protected abstract Task LoadAsync(CancellationToken ct);

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        return Partial("_Table", this);
    }
}
