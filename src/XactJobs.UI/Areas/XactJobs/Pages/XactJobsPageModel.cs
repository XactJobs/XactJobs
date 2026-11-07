using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages;

/// <summary>
/// A dashboard page whose table is also served on its own (the "Table" handler), so that it refreshes without reloading the page.
/// </summary>
public abstract class XactJobsPageModel : PageModel
{
    protected XactJobsPageModel(IXactJobsUIService service, XactJobsUIOptions options)
    {
        Service = service;
        Options = options;
    }

    protected IXactJobsUIService Service { get; }

    public XactJobsUIOptions Options { get; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Queue { get; set; }

    /// <summary>
    /// The queues to filter by, or null if the page has no queue filter.
    /// </summary>
    public IReadOnlyList<string>? Queues { get; private set; }

    protected virtual bool HasQueueFilter => true;

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

    public string BatchUrl(Guid batchId) => PageUrl("/batch/" + batchId.ToString("D"));

    public static string DisplayBatch(Guid batchId) => DisplayNames.Batch(batchId);

    /// <summary>
    /// A UTC time, which the browser shows in its own time zone.
    /// </summary>
    public static IHtmlContent Time(DateTime? utc)
    {
        if (!utc.HasValue) return HtmlString.Empty;

        var iso = utc.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var text = utc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

        return new HtmlString($"<time class=\"text-nowrap\" datetime=\"{iso}\" title=\"{text}\">{text}</time>");
    }

    /// <summary>
    /// Below a scheduled time: the time the job was originally scheduled for, if it differs (a retry, or a run moved to now).
    /// </summary>
    public static IHtmlContent OriginalTime(DateTime? originallyScheduledAt)
    {
        if (!originallyScheduledAt.HasValue) return HtmlString.Empty;

        return new HtmlContentBuilder()
            .AppendHtml("<div class=\"small text-muted text-nowrap\" title=\"Originally scheduled for: its retries keep this time\">originally ")
            .AppendHtml(Time(originallyScheduledAt))
            .AppendHtml("</div>");
    }

    public static string DisplayQueue(string queue) => DisplayNames.Queue(queue);

    protected abstract Task LoadAsync(CancellationToken ct);

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (HasQueueFilter)
        {
            Queues = await Service.GetQueuesAsync(ct);
        }

        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnGetTableAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        return Partial("_Table", this);
    }
}
