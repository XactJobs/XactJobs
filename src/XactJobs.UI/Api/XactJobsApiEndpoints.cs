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

        // Scheduled/Pending jobs, of a queue or of all of them: they're read in the order of the primary key, which starts with
        // the queue
        group.MapGet("/jobs", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            [FromQuery] string? search,
            CancellationToken ct) =>
            Results.Ok(await service.GetScheduledJobsAsync(queue, search, page ?? 1, pageSize ?? options.DefaultPageSize, ct)))
            .WithName("XactJobsGetScheduledJobs");

        group.MapGet("/queues", async (IXactJobsUIService service, CancellationToken ct) =>
            Results.Ok(await service.GetQueuesAsync(ct)))
            .WithName("XactJobsGetQueues");

        // a pending job is identified by its primary key: the id, and the queue and the scheduled time in the query string (see JobKey)
        group.MapGet("/jobs/{id:long}", async (
            IXactJobsUIService service, long id, [FromQuery] string? queue, [FromQuery] string? scheduledAt, CancellationToken ct) =>
        {
            if (!JobKey.TryParse(id, queue, scheduledAt, out var key)) return JobKeyRequired();

            var job = await service.GetJobAsync(key, ct);
            return job is not null ? Results.Ok(job) : Results.NotFound();
        }).WithName("XactJobsGetJobById");

        group.MapPost("/jobs/{id:long}/cancel", async (
            IXactJobsUIService service, HttpContext httpContext, long id, [FromQuery] string? queue, [FromQuery] string? scheduledAt, CancellationToken ct) =>
            JobKey.TryParse(id, queue, scheduledAt, out var key)
                ? ToResult(httpContext, await service.CancelJobAsync(key, httpContext.User.Identity?.Name, ct))
                : JobKeyRequired())
            .WithName("XactJobsCancelJob");

        group.MapPost("/jobs/{id:long}/run-now", async (
            IXactJobsUIService service, HttpContext httpContext, long id, [FromQuery] string? queue, [FromQuery] string? scheduledAt, CancellationToken ct) =>
            JobKey.TryParse(id, queue, scheduledAt, out var key)
                ? ToResult(httpContext, await service.RunJobNowAsync(key, ct))
                : JobKeyRequired())
            .WithName("XactJobsRunJobNow");

        // History endpoints
        group.MapGet("/history", async (
            IXactJobsUIService service,
            [FromQuery] string? category,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? cursor,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            [FromQuery] string? search,
            CancellationToken ct) =>
        {
            if (!TryParseCategory(category, out var historyCategory))
            {
                return Results.BadRequest(new { message = $"Unknown category, use one of: {string.Join(", ", Enum.GetNames<HistoryCategory>())}." });
            }

            if (!TryGetPeriod(from, to, out var period)) return PeriodInvalid();

            return Results.Ok(await service.GetHistoryAsync(
                historyCategory, period.From, period.To, queue, search, cursor, pageSize ?? options.DefaultPageSize, ct));
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
        group.MapGet("/batches/active", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
            Results.Ok(await service.GetActiveBatchesAsync(page ?? 1, pageSize ?? options.DefaultPageSize, ct)))
            .WithName("XactJobsGetActiveBatches");

        group.MapGet("/batches/finished", async (
            IXactJobsUIService service,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? cursor,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
        {
            if (!TryGetPeriod(from, to, out var period)) return PeriodInvalid();

            return Results.Ok(await service.GetFinishedBatchesAsync(period.From, period.To, cursor, pageSize ?? options.DefaultPageSize, ct));
        }).WithName("XactJobsGetFinishedBatches");

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
            [FromQuery] string? search,
            CancellationToken ct) =>
            Results.Ok(await service.GetPeriodicJobsAsync(
                search,
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

    private static IResult JobKeyRequired() => Results.BadRequest(new
    {
        message = "A pending job is identified by its primary key: the id in the route, and the queue and the scheduledAt (UTC, e.g. 2026-11-07T10:00:00.1234567Z) of the job in the query string."
    });

    /// <summary>The longest period of the history and the finished batches: the rows are read within it.</summary>
    private static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(31);

    /// <summary>
    /// The period's UTC times: by default the last 24 hours, and no end (until now, and the rows that arrive later). Invalid if it
    /// doesn't start before its end, or is longer than <see cref="MaxPeriod"/>.
    /// </summary>
    private static bool TryGetPeriod(DateTime? from, DateTime? to, out (DateTime From, DateTime? To) period)
    {
        var now = DateTime.UtcNow;
        var start = from?.ToUniversalTime() ?? now.AddHours(-24);
        var end = to?.ToUniversalTime();

        period = (start, end);

        return (end ?? now) > start && (end ?? now) - start <= MaxPeriod;
    }

    private static IResult PeriodInvalid() =>
        Results.BadRequest(new { message = "The period (from, to) must start before it ends, and be at most 31 days." });

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
