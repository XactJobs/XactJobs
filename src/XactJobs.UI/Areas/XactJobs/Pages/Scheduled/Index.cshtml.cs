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

    protected override async Task LoadAsync(CancellationToken ct)
    {
        Jobs = await Service.GetScheduledJobsAsync(PageNumber, Options.DefaultPageSize, Queue, ct);
    }
}
