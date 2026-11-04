using Microsoft.AspNetCore.Mvc;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages.History;

public class IndexModel : XactJobsPageModel
{
    public IndexModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    [BindProperty(SupportsGet = true, Name = "category")]
    public string? CategoryName { get; set; }

    public HistoryCategory Category { get; private set; }

    public PagedResultDto<JobHistoryDto> Jobs { get; private set; } = new();

    public static IReadOnlyList<(HistoryCategory Category, string Title, string Icon, string Description)> Categories { get; } =
    [
        (HistoryCategory.Succeeded, "Succeeded", "bi-check-circle", "Jobs that completed successfully"),
        (HistoryCategory.Failed, "Failed", "bi-x-circle", "Jobs that ran out of retries. Retrying enqueues them again as new jobs."),
        (HistoryCategory.Cancelled, "Cancelled", "bi-slash-circle", "Jobs cancelled from the dashboard. Retrying enqueues them again as new jobs."),
        (HistoryCategory.Retried, "Retried attempts", "bi-arrow-repeat", "Failed attempts, after which the job was scheduled to run again automatically"),
        (HistoryCategory.Skipped, "Skipped", "bi-skip-forward-circle", "Runs of periodic jobs that were paused, deleted or changed"),
    ];

    public (HistoryCategory Category, string Title, string Icon, string Description) Current =>
        Categories.First(c => c.Category == Category);

    public bool ShowsErrors => Category is HistoryCategory.Failed or HistoryCategory.Cancelled or HistoryCategory.Retried;

    public string CategoryUrl(HistoryCategory category) => PageUrl("/history/" + category.ToString().ToLowerInvariant());

    protected override async Task LoadAsync(CancellationToken ct)
    {
        Category = Enum.TryParse<HistoryCategory>(CategoryName, ignoreCase: true, out var category) && Enum.IsDefined(category)
            ? category
            : HistoryCategory.Succeeded;

        Jobs = await Service.GetHistoryAsync(Category, PageNumber, Options.DefaultPageSize, Queue, ct);
    }
}
