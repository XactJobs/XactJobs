using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Scheduled;

public class IndexModel : XactJobsPageModel
{
    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    public PagedResultDto<JobDto> Jobs { get; private set; } = new();

    public override bool HasSearch => true;

    // the default queue first: "All" is an empty queue in the query (queue=), kept in the page's links
    public override string FilterQuery =>
        (string.IsNullOrEmpty(Queue) ? "&queue=" : QueryPart("queue", Queue)) + QueryPart("search", Search);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Queue) && !Request.Query.ContainsKey("queue"))
        {
            Queue = QueueNames.Default;
        }

        Jobs = await Service.GetScheduledJobsAsync(Queue, Search, PageNumber, Options.DefaultPageSize, ct);
    }
}
