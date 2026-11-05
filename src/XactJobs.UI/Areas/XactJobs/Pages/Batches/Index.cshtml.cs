using Microsoft.AspNetCore.Mvc;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Batches;

public class IndexModel : XactJobsPageModel
{
    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    protected override bool HasQueueFilter => false;

    [BindProperty(SupportsGet = true, Name = "state")]
    public string? StateName { get; set; }

    public BatchState State { get; private set; }

    public PagedResultDto<BatchDto> Batches { get; private set; } = new();

    public static IReadOnlyList<(BatchState State, string Title, string Icon, string Description)> States { get; } =
    [
        (BatchState.Active, "Active", "bi-hourglass-split", "Batches with jobs left. Their continuations start when all of their jobs are finished."),
        (BatchState.Finished, "Finished", "bi-check2-all", "Batches without jobs left, as long as their history is kept."),
    ];

    public (BatchState State, string Title, string Icon, string Description) Current => States.First(s => s.State == State);

    public string StateUrl(BatchState state) => PageUrl("/batches/" + state.ToString().ToLowerInvariant());

    protected override async Task LoadAsync(CancellationToken ct)
    {
        State = Enum.TryParse<BatchState>(StateName, ignoreCase: true, out var state) && Enum.IsDefined(state)
            ? state
            : BatchState.Active;

        Batches = await Service.GetBatchesAsync(State, PageNumber, Options.DefaultPageSize, ct);
    }
}
