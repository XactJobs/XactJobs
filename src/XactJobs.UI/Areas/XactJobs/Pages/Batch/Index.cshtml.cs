using Microsoft.AspNetCore.Mvc;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Batch;

public class IndexModel : XactJobsPageModel
{
    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    protected override bool HasQueueFilter => false;

    [BindProperty(SupportsGet = true, Name = "id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Null if nothing is left of the batch.
    /// </summary>
    public BatchDetailsDto? Details { get; private set; }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        Details = await Service.GetBatchAsync(Id, ct);
    }
}
