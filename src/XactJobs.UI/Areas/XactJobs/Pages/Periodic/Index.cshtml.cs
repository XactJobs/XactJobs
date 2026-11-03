using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Periodic;

public class IndexModel : XactJobsPageModel
{
    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    protected override bool HasQueueFilter => false;

    public PagedResultDto<PeriodicJobDto> Jobs { get; private set; } = new();

    public string PeriodicApiUrl(PeriodicJobDto job, string action) =>
        ApiUrl("/periodic/" + Uri.EscapeDataString(job.Id) + action);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        Jobs = await Service.GetPeriodicJobsAsync(PageNumber, Options.DefaultPageSize, ct);
    }
}
