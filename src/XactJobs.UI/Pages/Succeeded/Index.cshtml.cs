using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Succeeded;

public class IndexModel : PageModel
{
    private readonly IXactJobsUIService _service;

    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
    {
        _service = service;
        Options = options;
    }

    public XactJobsUIOptions Options { get; }
    public PagedResultDto<JobHistoryDto> Jobs { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Queue { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Jobs = await _service.GetSucceededJobsAsync(PageNumber, Options.DefaultPageSize, Queue, ct);
    }
}
