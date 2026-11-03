namespace XactJobs.UI.Pages;

public record PaginationModel(XactJobsPageModel Owner, int Page, int TotalPages, int TotalCount);
