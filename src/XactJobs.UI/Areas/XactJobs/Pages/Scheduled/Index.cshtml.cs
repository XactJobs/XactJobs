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

    // one queue at a time: the rows are read in the order of the primary key, which starts with the queue
    public override bool AllowsAllQueues => false;

    protected override async Task LoadAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Queue))
        {
            Queue = QueueNames.Default;
        }

        Jobs = await Service.GetScheduledJobsAsync(Queue, PageNumber, Options.DefaultPageSize, ct);
    }
}
