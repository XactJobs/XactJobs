using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using XactJobs.UI.Services;

namespace XactJobs.UI.Api;

public static class XactJobsApiEndpoints
{
    public static IEndpointRouteBuilder MapXactJobsApi(
        this IEndpointRouteBuilder endpoints,
        string basePath = "/api/xactjobs")
    {
        var group = endpoints.MapGroup(basePath)
            .WithTags("XactJobs");

        // Dashboard endpoints
        group.MapGet("/stats", async (IXactJobsUIService service, CancellationToken ct) =>
            Results.Ok(await service.GetDashboardStatsAsync(ct)))
            .WithName("GetDashboardStats");

        group.MapGet("/chart-data", async (
            IXactJobsUIService service,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? intervalMinutes,
            CancellationToken ct) =>
        {
            var fromDate = from ?? DateTime.UtcNow.AddDays(-7);
            var toDate = to ?? DateTime.UtcNow;
            var interval = TimeSpan.FromMinutes(intervalMinutes ?? 60);
            return Results.Ok(await service.GetJobCountsOverTimeAsync(fromDate, toDate, interval, ct));
        }).WithName("GetChartData");

        // Scheduled/Pending jobs
        group.MapGet("/jobs", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            CancellationToken ct) =>
            Results.Ok(await service.GetScheduledJobsAsync(
                page ?? 1,
                pageSize ?? 20,
                queue, ct)))
            .WithName("GetScheduledJobs");

        group.MapGet("/jobs/{id:long}", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var job = await service.GetJobByIdAsync(id, ct);
            return job is not null ? Results.Ok(job) : Results.NotFound();
        }).WithName("GetJobById");

        group.MapDelete("/jobs/{id:long}", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var deleted = await service.DeleteJobAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).WithName("DeleteJob");

        group.MapPost("/jobs/{id:long}/requeue", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var success = await service.RequeueJobAsync(id, ct);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RequeueJob");

        // History endpoints
        group.MapGet("/history/succeeded", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            CancellationToken ct) =>
            Results.Ok(await service.GetSucceededJobsAsync(
                page ?? 1,
                pageSize ?? 20,
                queue, ct)))
            .WithName("GetSucceededJobs");

        group.MapGet("/history/failed", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? queue,
            CancellationToken ct) =>
            Results.Ok(await service.GetFailedJobsAsync(
                page ?? 1,
                pageSize ?? 20,
                queue, ct)))
            .WithName("GetFailedJobs");

        group.MapGet("/history/{id:long}", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var history = await service.GetJobHistoryByIdAsync(id, ct);
            return history is not null ? Results.Ok(history) : Results.NotFound();
        }).WithName("GetHistoryById");

        group.MapPost("/history/{id:long}/retry", async (
            IXactJobsUIService service, long id, CancellationToken ct) =>
        {
            var success = await service.RetryFailedJobAsync(id, ct);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("RetryFailedJob");

        // Periodic job endpoints
        group.MapGet("/periodic", async (
            IXactJobsUIService service,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
            Results.Ok(await service.GetPeriodicJobsAsync(
                page ?? 1,
                pageSize ?? 20, ct)))
            .WithName("GetPeriodicJobs");

        group.MapGet("/periodic/{id}", async (
            IXactJobsUIService service, string id, CancellationToken ct) =>
        {
            var periodicJob = await service.GetPeriodicJobByIdAsync(id, ct);
            return periodicJob is not null ? Results.Ok(periodicJob) : Results.NotFound();
        }).WithName("GetPeriodicJobById");

        group.MapPost("/periodic/{id}/toggle", async (
            IXactJobsUIService service,
            string id,
            [FromQuery] bool isActive,
            CancellationToken ct) =>
        {
            var success = await service.TogglePeriodicJobAsync(id, isActive, ct);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("TogglePeriodicJob");

        group.MapPost("/periodic/{id}/trigger", async (
            IXactJobsUIService service, string id, CancellationToken ct) =>
        {
            var success = await service.TriggerPeriodicJobNowAsync(id, ct);
            return success ? Results.Ok() : Results.NotFound();
        }).WithName("TriggerPeriodicJob");

        group.MapDelete("/periodic/{id}", async (
            IXactJobsUIService service, string id, CancellationToken ct) =>
        {
            var deleted = await service.DeletePeriodicJobAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).WithName("DeletePeriodicJob");

        return endpoints;
    }
}
