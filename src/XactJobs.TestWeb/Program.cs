using Microsoft.EntityFrameworkCore;
using XactJobs;
using XactJobs.DependencyInjection;
using XactJobs.TestWeb;
using XactJobs.UI.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "xactjobs_test.db");

// Configure SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register sample job class for DI
builder.Services.AddTransient<SampleJobs>();

// Add XactJobs background processing
builder.Services.AddXactJobs<AppDbContext>(options =>
{
    options.WithPollingInterval(3);
    options.WithPeriodicJob(
        () => SampleJobs.StaticJob("Periodic ping"),
        "periodic-ping",
        Cron.MinuteInterval(1));
});

// Add XactJobs UI dashboard
builder.Services.AddXactJobsUI<AppDbContext>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Map a simple endpoint to enqueue test jobs
app.MapGet("/", () => Results.Redirect("/xactjobs"));

app.MapGet("/enqueue/quick", (AppDbContext db) =>
{
    db.JobEnqueue<SampleJobs>(x => x.QuickJob());
    db.SaveChanges();
    return Results.Ok("Quick job enqueued!");
});

app.MapGet("/enqueue/slow", (AppDbContext db) =>
{
    db.JobEnqueue<SampleJobs>(x => x.SlowJobAsync(3000, CancellationToken.None));
    db.SaveChanges();
    return Results.Ok("Slow job (3s) enqueued!");
});

app.MapGet("/enqueue/failing", (AppDbContext db) =>
{
    db.JobEnqueue<SampleJobs>(x => x.FailingJob());
    db.SaveChanges();
    return Results.Ok("Failing job enqueued!");
});

app.MapGet("/enqueue/batch", (AppDbContext db) =>
{
    for (int i = 1; i <= 10; i++)
    {
        db.JobEnqueue(() => SampleJobs.StaticJob($"Batch job #{i}"));
    }
    db.SaveChanges();
    return Results.Ok("10 batch jobs enqueued!");
});

app.MapGet("/enqueue/scheduled", (AppDbContext db) =>
{
    db.JobScheduleIn(() => SampleJobs.StaticJob("Scheduled for 30s later"), TimeSpan.FromSeconds(30));
    db.SaveChanges();
    return Results.Ok("Job scheduled to run in 30 seconds!");
});

// Enable XactJobs UI
app.UseXactJobsUI();
app.MapXactJobsUI();

app.Run();
