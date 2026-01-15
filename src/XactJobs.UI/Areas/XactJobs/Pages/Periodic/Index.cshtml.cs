using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Periodic;

public class IndexModel : PageModel
{
    private readonly IXactJobsUIService _service;

    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
    {
        _service = service;
        Options = options;
    }

    public XactJobsUIOptions Options { get; }
    public PagedResultDto<PeriodicJobDto> Jobs { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Jobs = await _service.GetPeriodicJobsAsync(PageNumber, Options.DefaultPageSize, ct);
    }
}
