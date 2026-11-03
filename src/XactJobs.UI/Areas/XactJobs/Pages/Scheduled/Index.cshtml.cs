using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Scheduled;

public class IndexModel : XactJobsPageModel
{
    private readonly IXactJobsUIService _service;

    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(options)
    {
        _service = service;
    }

    public PagedResultDto<JobDto> Jobs { get; private set; } = new();

    protected override async Task LoadAsync(CancellationToken ct)
    {
        Jobs = await _service.GetScheduledJobsAsync(PageNumber, Options.DefaultPageSize, Queue, ct);
    }
}
