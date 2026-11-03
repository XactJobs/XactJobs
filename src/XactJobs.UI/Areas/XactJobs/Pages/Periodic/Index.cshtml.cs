using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Periodic;

public class IndexModel : XactJobsPageModel
{
    private readonly IXactJobsUIService _service;

    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(options)
    {
        _service = service;
    }

    public PagedResultDto<PeriodicJobDto> Jobs { get; private set; } = new();

    public string PeriodicApiUrl(PeriodicJobDto job, string action) =>
        ApiUrl("/periodic/" + Uri.EscapeDataString(job.Id) + action);

    protected override async Task LoadAsync(CancellationToken ct)
    {
        Jobs = await _service.GetPeriodicJobsAsync(PageNumber, Options.DefaultPageSize, ct);
    }
}
