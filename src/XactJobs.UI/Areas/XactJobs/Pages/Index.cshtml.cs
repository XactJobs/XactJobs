using Microsoft.AspNetCore.Mvc.RazorPages;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages;

public class IndexModel : PageModel
{
    private readonly IXactJobsUIService _service;

    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
    {
        _service = service;
        Options = options;
    }

    public XactJobsUIOptions Options { get; }
    public DashboardStatsDto Stats { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Stats = await _service.GetDashboardStatsAsync(ct);
    }
}
