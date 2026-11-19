using Microsoft.AspNetCore.Mvc;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.Batches;

public class IndexModel : XactJobsPeriodPageModel
{
    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    protected override bool HasQueueFilter => false;

    [BindProperty(SupportsGet = true, Name = "state")]
    public string? StateName { get; set; }

    public BatchState State => Enum.TryParse<BatchState>(StateName, ignoreCase: true, out var state) && Enum.IsDefined(state)
        ? state
        : BatchState.Active;

    // the active batches by page number (the job table), the finished ones in a period, from the last one seen (the history)
    public override bool HasPeriodFilter => State == BatchState.Finished;

    public override bool UsesCursor => State == BatchState.Finished;

    public PagedResultDto<BatchDto> ActiveBatches { get; private set; } = new();

    public CursorPageDto<BatchDto> FinishedBatches { get; private set; } = new();

    public IReadOnlyList<BatchDto> Items => State == BatchState.Finished ? FinishedBatches.Items : ActiveBatches.Items;

    public static IReadOnlyList<(BatchState State, string Title, string Icon, string Description)> States { get; } =
    [
        (BatchState.Active, "Active", "bi-hourglass-split", "Batches with jobs left. Their continuations start when all of their jobs are finished."),
        (BatchState.Finished, "Finished", "bi-check2-all", "Batches without jobs left, by the time their last job finished, as long as their history is kept."),
    ];

    public (BatchState State, string Title, string Icon, string Description) Current => States.First(s => s.State == State);

    public string StateUrl(BatchState state) => PageUrl("/batches/" + state.ToString().ToLowerInvariant());

    protected override async Task LoadAsync(CancellationToken ct)
    {
        if (State == BatchState.Finished)
        {
            var (from, to) = GetPeriod();

            FinishedBatches = await Service.GetFinishedBatchesAsync(from, to, Cursor, Options.DefaultPageSize, ct);
        }
        else
        {
            ActiveBatches = await Service.GetActiveBatchesAsync(PageNumber, Options.DefaultPageSize, ct);
        }
    }
}
