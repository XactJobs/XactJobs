using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using XactJobs.UI.Security;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Api;

internal static class XactJobsApiEndpoints
{
    /// <summary>
    /// The htmx event the dashboard refreshes its tables on.
    /// </summary>
    public const string ChangedEvent = "xactjobs-changed";

    /// <summary>
    /// The htmx event the dashboard shows a message on.
    /// </summary>
    public const string MessageEvent = "xactjobs-message";

    public static RouteGroupBuilder MapXactJobsApi(this IEndpointRouteBuilder endpoints, XactJobsUIOptions options)
    {
        var group = endpoints.MapGroup(options.ApiBasePath)
            .WithTags("XactJobs");

        group.RequireDashboardAccess(options);

        if (options.RequireAntiforgery)
        {
            // safe methods (GET etc.) are always valid
            group.AddEndpointFilter(async (context, next) =>
            {
                var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();

                return await antiforgery.IsRequestValidAsync(context.HttpContext)
                    ? await next(context)
                    : Results.Json(new { message = "Missing or invalid antiforgery token. Reload the page and try again." },
                                   statusCode: StatusCodes.Status400BadRequest);
            });
        }

        // Dashboard endpoints
        group.MapGet("/stats", async (IXactJobsUIService service, CancellationToken ct) =>
            Results.Ok(await service.GetDashboardStatsAsync(ct)))
            .WithName("XactJobsGetDashboardStats");

        group.MapGet("/chart-data", async (
            IXactJobsUIService service,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? intervalMinutes,
            CancellationToken ct) =>
        {
            var interval = TimeSpan.FromMinutes(Math.Max(intervalMinutes ?? 60, 1));
            var toDate = to?.ToUniversalTime() ?? DateTime.UtcNow;

            // by default the last 24 intervals, aligned to the interval (e.g. full hours), the last one up to now
            var fromDate = from?.ToUniversalTime()
                ?? new DateTime(toDate.Ticks - toDate.Ticks % interval.Ticks, DateTimeKind.Utc) - 23 * interval;

            if (toDate - fromDate > TimeSpan.FromDays(31) || fromDate >= toDate)
            {
                return Results.BadRequest(new { message = "The range must be positive, and at most 31 days." });
            }

            return Results.Ok(await service.GetJobCountsOverTimeAsync(fromDate, toDate, interval, ct));
        }).WithName("XactJobsGetChartData");

        // Scheduled/Pending jobs
        group.MapGet("/jobs", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            CancellationToken ct) =>
            Results.Ok(await service.GetScheduledJobsAsync(
                page ?? 1,
                pageSize ?? options.DefaultPageSize,
                queue, ct)))
            .WithName("XactJobsGetScheduledJobs");

        group.MapGet("/jobs/{id:long}", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var job = await service.GetJobByIdAsync(id, ct);
            return job is not null ? Results.Ok(job) : Results.NotFound();
        }).WithName("XactJobsGetJobById");

        group.MapPost("/jobs/{id:long}/cancel", async (
            IXactJobsUIService service, HttpContext httpContext, long id, CancellationToken ct) =>
            ToResult(httpContext, await service.CancelJobAsync(id, httpContext.User.Identity?.Name, ct)))
            .WithName("XactJobsCancelJob");

        group.MapPost("/jobs/{id:long}/run-now", async (
            IXactJobsUIService service, HttpContext httpContext, long id, CancellationToken ct) =>
            ToResult(httpContext, await service.RunJobNowAsync(id, ct)))
            .WithName("XactJobsRunJobNow");

        // History endpoints
        group.MapGet("/history", async (
            IXactJobsUIService service,
            [FromQuery] string? category,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            CancellationToken ct) =>
        {
            if (!TryParseCategory(category, out var historyCategory))
            {
                return Results.BadRequest(new { message = $"Unknown category, use one of: {string.Join(", ", Enum.GetNames<HistoryCategory>())}." });
            }

            return Results.Ok(await service.GetHistoryAsync(
                historyCategory,
                page ?? 1,
                pageSize ?? options.DefaultPageSize,
                queue, ct));
        }).WithName("XactJobsGetHistory");

        group.MapGet("/history/{id:long}", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var history = await service.GetJobHistoryByIdAsync(id, ct);
            return history is not null ? Results.Ok(history) : Results.NotFound();
        }).WithName("XactJobsGetHistoryById");

        group.MapPost("/history/{id:long}/retry", async (
            IXactJobsUIService service, HttpContext httpContext, long id, CancellationToken ct) =>
            ToResult(httpContext, await service.RetryJobAsync(id, ct)))
            .WithName("XactJobsRetryJob");

        // Batches
        group.MapGet("/batches", async (
            IXactJobsUIService service,
            [FromQuery] string? state,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
        {
            var batchState = BatchState.Active;

            if (!string.IsNullOrEmpty(state) && !(Enum.TryParse(state, ignoreCase: true, out batchState) && Enum.IsDefined(batchState)))
            {
                return Results.BadRequest(new { message = $"Unknown state, use one of: {string.Join(", ", Enum.GetNames<BatchState>())}." });
            }

            return Results.Ok(await service.GetBatchesAsync(batchState, page ?? 1, pageSize ?? options.DefaultPageSize, ct));
        }).WithName("XactJobsGetBatches");

        group.MapGet("/batches/{id:guid}", async (
            IXactJobsUIService service, Guid id, CancellationToken ct) =>
        {
            var batch = await service.GetBatchAsync(id, ct);
            return batch is not null ? Results.Ok(batch) : Results.NotFound();
        }).WithName("XactJobsGetBatch");

        // Periodic job endpoints
        group.MapGet("/periodic", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
            Results.Ok(await service.GetPeriodicJobsAsync(
                page ?? 1,
                pageSize ?? options.DefaultPageSize, ct)))
            .WithName("XactJobsGetPeriodicJobs");

        group.MapGet("/periodic/{id}", async (
            IXactJobsUIService service, string id, CancellationToken ct) =>
        {
            var periodicJob = await service.GetPeriodicJobByIdAsync(DecodeId(id), ct);
            return periodicJob is not null ? Results.Ok(periodicJob) : Results.NotFound();
        }).WithName("XactJobsGetPeriodicJobById");

        group.MapPost("/periodic/{id}/pause", async (
            IXactJobsUIService service, HttpContext httpContext, string id, CancellationToken ct) =>
            ToResult(httpContext, await service.SetPeriodicJobActiveAsync(DecodeId(id), false, ct)))
            .WithName("XactJobsPausePeriodicJob");

        group.MapPost("/periodic/{id}/resume", async (
            IXactJobsUIService service, HttpContext httpContext, string id, CancellationToken ct) =>
            ToResult(httpContext, await service.SetPeriodicJobActiveAsync(DecodeId(id), true, ct)))
            .WithName("XactJobsResumePeriodicJob");

        group.MapPost("/periodic/{id}/trigger", async (
            IXactJobsUIService service, HttpContext httpContext, string id, CancellationToken ct) =>
            ToResult(httpContext, await service.TriggerPeriodicJobAsync(DecodeId(id), ct)))
            .WithName("XactJobsTriggerPeriodicJob");

        group.MapDelete("/periodic/{id}", async (
            IXactJobsUIService service, HttpContext httpContext, string id, CancellationToken ct) =>
            ToResult(httpContext, await service.DeletePeriodicJobAsync(DecodeId(id), ct)))
            .WithName("XactJobsDeletePeriodicJob");

        return group;
    }

    /// <summary>
    /// Returns the message as JSON, and as htmx events, so that the dashboard shows it and refreshes its tables.
    /// The tables refresh on errors too: a conflict usually means they are out of date.
    /// </summary>
    private static IResult ToResult(HttpContext httpContext, ActionResultDto result)
    {
        var isDone = result.Status == ActionStatus.Done;

        // the default encoder escapes non-ASCII characters, which aren't allowed in a header
        httpContext.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [ChangedEvent] = null,
            [MessageEvent] = new { text = result.Message, level = isDone ? "success" : "danger" },
        });

        var body = new { message = result.Message };

        return result.Status switch
        {
            ActionStatus.Done => Results.Ok(body),
            ActionStatus.NotFound => Results.NotFound(body),
            _ => Results.Conflict(body),
        };
    }

    private static bool TryParseCategory(string? value, out HistoryCategory category)
    {
        category = HistoryCategory.Succeeded;

        return string.IsNullOrEmpty(value)
            || (Enum.TryParse(value, ignoreCase: true, out category) && Enum.IsDefined(category));
    }

    /// <summary>
    /// Routing leaves an encoded slash encoded, and periodic job ids may contain slashes.
    /// </summary>
    private static string DecodeId(string id)
    {
        return id.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
    }
}
