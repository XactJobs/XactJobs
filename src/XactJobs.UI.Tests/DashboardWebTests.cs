using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XactJobs.UI.DependencyInjection;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Tests;

public partial class DashboardWebTests : IAsyncDisposable
{
    private const string Local = "127.0.0.1";
    private const string Remote = "203.0.113.7";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("/XactJobs")]
    [InlineData("/XactJobs/scheduled")]
    [InlineData("/XactJobs/scheduled?handler=Table&p=1")]
    [InlineData("/XactJobs/history/succeeded")]
    [InlineData("/XactJobs/history/failed")]
    [InlineData("/XactJobs/history/cancelled")]
    [InlineData("/XactJobs/history/retried?handler=Table&p=1")]
    [InlineData("/XactJobs/history/skipped")]
    [InlineData("/XactJobs/periodic")]
    [InlineData("/XactJobs/periodic?handler=Table&p=1")]
    [InlineData("/api/XactJobs/stats")]
    [InlineData("/api/XactJobs/jobs")]
    [InlineData("/api/XactJobs/history?category=failed")]
    [InlineData("/api/XactJobs/periodic")]
    public async Task Pages_RenderWithData(string url)
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedAsync();

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");

        // the tables are served on their own, without the layout
        Assert.Equal(!url.Contains("handler=Table") && !url.StartsWith("/api/"), body.Contains("<html"));
    }

    private const string SeedBatch = "3f2a6c1e-8d4b-4c7a-9e0f-1b2c3d4e5f60";

    [Theory]
    [InlineData("/XactJobs/batches")]
    [InlineData("/XactJobs/batches/finished")]
    [InlineData("/XactJobs/batches/active?handler=Table&p=1")]
    [InlineData("/XactJobs/batch/" + SeedBatch)]
    [InlineData("/XactJobs/batch/" + SeedBatch + "?handler=Table&p=1")]
    [InlineData("/XactJobs/batch/00000000-0000-0000-0000-000000000001")]
    [InlineData("/XactJobs/history/skipped")]
    [InlineData("/api/XactJobs/batches?state=finished")]
    [InlineData("/api/XactJobs/batches/" + SeedBatch)]
    public async Task BatchPages_RenderWithData(string url)
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedBatchAsync();

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        Assert.Equal(!url.Contains("handler=Table") && !url.StartsWith("/api/"), body.Contains("<html"));
    }

    [Fact]
    public async Task ScheduledPage_ShowsAwaitingContinuations_AndLinksToTheirBatch()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedBatchAsync();

        var table = await client.GetStringAsync("/XactJobs/scheduled?handler=Table&p=1");

        Assert.Contains("Awaiting batch", table);
        Assert.Contains("1 left", table);
        Assert.Contains("After its batch", table);
        Assert.Contains("The continuation starts when its batch is finished", table);
        Assert.Contains($"href=\"/XactJobs/batch/{SeedBatch}\"", table);
        Assert.Contains("on failure", table);
    }

    [Fact]
    public async Task BatchesPage_ShowsTheProgress_AndSkippedContinuationsCanBeRetried()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedBatchAsync();

        var batches = await client.GetStringAsync("/XactJobs/batches?handler=Table&p=1");

        Assert.Contains("<code>3f2a6c1e</code>", batches);
        Assert.Contains("2 of 3 done", batches);
        Assert.Contains("1 failed", batches);
        Assert.Contains("1 waiting", batches);

        var skipped = await client.GetStringAsync("/XactJobs/history/skipped?handler=Table&p=1");

        Assert.Contains("/api/XactJobs/history/302/retry", skipped);
        Assert.Contains("had no failed jobs", skipped);
    }

    [Fact]
    public async Task BatchApi_RejectsAnUnknownState_AndAnUnknownBatchIsNotFound()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/XactJobs/batches?state=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/XactJobs/batches/" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Pages_LoadNothingFromOtherSites_AndShowTimesForTheBrowser()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedAsync();

        var html = await client.GetStringAsync("/XactJobs/scheduled");

        Assert.DoesNotContain("https://", html);
        Assert.Matches(@"/XactJobs/assets/lib/htmx/htmx\.min\.js\?v=[0-9a-f]{16}""", html);
        Assert.Matches(@"<time class=""text-nowrap"" datetime=""\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z""", html);
    }

    [Fact]
    public async Task Assets_AreServedFromTheAssembly_AndCachedByVersion()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        var html = await client.GetStringAsync("/XactJobs/scheduled");

        var urls = Regex.Matches(html, @"(?:href|src)=""(/XactJobs/assets/[^""]+)""").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

        Assert.Equal(7, urls.Count);

        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode}");
            Assert.Equal(url.Contains(".css") ? "text/css" : "text/javascript", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
            Assert.NotNull(response.Headers.ETag);
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        }

        // a font, referenced from the icons' stylesheet without the version: checked again each time
        var font = await client.GetAsync("/XactJobs/assets/lib/bootstrap-icons/fonts/bootstrap-icons.woff2");

        Assert.Equal(HttpStatusCode.OK, font.StatusCode);
        Assert.Equal("font/woff2", font.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", font.Headers.CacheControl?.ToString());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/XactJobs/assets/lib/bootstrap-icons/fonts/bootstrap-icons.woff2");
        conditional.Headers.IfNoneMatch.Add(font.Headers.ETag!);

        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/XactJobs/assets/js/missing.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/XactJobs/assets/%2E%2E/XactJobs.UI.dll")).StatusCode);
    }

    [Fact]
    public async Task QueueFilter_ShowsOnlyTheJobsOfTheQueue()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedAsync();

        await WithDbAsync(async db =>
        {
            db.JobEnqueue<SampleJobs>(x => x.Run(), "emails");
            return await db.SaveChangesAsync();
        });

        var all = await client.GetStringAsync("/XactJobs/scheduled");
        var emails = await client.GetStringAsync("/XactJobs/scheduled?queue=emails");

        Assert.Contains("<option value=\"emails\">", all);
        Assert.Contains("<option value=\"emails\" selected=\"selected\">", emails);
        Assert.Equal(3, Regex.Matches(all, "<code>\\d+</code>").Count);
        Assert.Single(Regex.Matches(emails, "<code>\\d+</code>"));

        // the table keeps the filter when it refreshes
        Assert.Contains("?handler=Table&amp;p=1&amp;queue=emails", emails);
    }

    [Fact]
    public async Task MethodColumn_ShowsTheName_WithTheSignatureAsTooltip()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await WithDbAsync(async db =>
        {
            db.JobEnqueue<SampleJobs>(x => x.Send("a", 1));
            return await db.SaveChangesAsync();
        });

        var table = await client.GetStringAsync("/XactJobs/scheduled?handler=Table&p=1");

        Assert.Contains("<code title=\"Send(System.String, System.Int32)\">Send</code>", table);
    }

    [Fact]
    public async Task TypeColumn_ShowsGenericTypes_WithTheirArguments()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await WithDbAsync(async db =>
        {
            db.JobEnqueue<GenericJobs<Dictionary<string, SampleJobs>>>(x => x.Run());
            return await db.SaveChangesAsync();
        });

        var table = await client.GetStringAsync("/XactJobs/scheduled?handler=Table&p=1");

        Assert.Contains(">GenericJobs&lt;Dictionary&lt;String, SampleJobs&gt;&gt;</td>", table);
    }

    [Theory]
    [InlineData("/XactJobs/scheduled")]
    [InlineData("/api/XactJobs/stats")]
    [InlineData("/XactJobs/assets/js/xactjobs.js")]
    public async Task ByDefault_OnlyLocalRequestsAreAccepted(string url)
    {
        var client = await StartAsync(configure: null);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(client, url, remote: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(client, url, Remote)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, url, Local)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, url, "::1")).StatusCode);
    }

    [Theory]
    [InlineData("/XactJobs/periodic")]
    [InlineData("/api/XactJobs/periodic")]
    [InlineData("/XactJobs/assets/css/xactjobs.css")]
    public async Task WithAPolicy_OnlyAuthorizedUsersAreAccepted(string url)
    {
        var client = await StartAsync(o => o.RequireAuthorizationPolicy("Admins"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, url, Local)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(client, url, Remote, user: "bob")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, url, Remote, user: "alice", roles: "admin")).StatusCode);
    }

    [Fact]
    public async Task ChangingData_RequiresTheAntiforgeryToken()
    {
        var client = await StartAsync(o => o.AllowAnonymous());

        await SeedAsync();

        var jobId = await WithDbAsync(db => db.Jobs.Where(j => j.PeriodicJobId == null).Select(j => j.Id).SingleAsync());

        var page = await client.GetAsync("/XactJobs/scheduled");
        var html = await page.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(AntiforgeryMeta().Match(html).Groups["token"].Value);
        var cookie = page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(".AspNetCore.Antiforgery"));

        Assert.NotEmpty(token);

        // the page's Cancel button: the job's primary key, written from the row SQLite returned, and read back by the API
        var cancelUrl = WebUtility.HtmlDecode(HxPost().Matches(html).Select(m => m.Groups["url"].Value).Single(u => u.StartsWith($"/api/XactJobs/jobs/{jobId}/cancel?")));

        var withoutToken = await client.PostAsync(cancelUrl, null);

        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(1, await WithDbAsync(db => db.Jobs.CountAsync(j => j.Id == jobId)));

        using var request = new HttpRequestMessage(HttpMethod.Post, cancelUrl);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("RequestVerificationToken", token);

        var withToken = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Contains("xactjobs-changed", withToken.Headers.GetValues("HX-Trigger").Single());
        Assert.Equal(0, await WithDbAsync(db => db.Jobs.CountAsync(j => j.Id == jobId)));
    }

    [Fact]
    public async Task Conflicts_AreReportedWithAMessage()
    {
        var client = await StartAsync(o => o.AllowAnonymous().DisableAntiforgery());

        await SeedAsync();

        var run = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId != null).SingleAsync());

        var response = await client.PostAsync($"/api/XactJobs/jobs/{run.Id}/cancel{JobKey.Of(run).ToQueryString()}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Pause the periodic job", await response.Content.ReadAsStringAsync());
        Assert.Contains("danger", response.Headers.GetValues("HX-Trigger").Single());
    }

    /// <summary>
    /// A pending job is identified by its primary key: on PostgreSQL, SQL Server and Oracle, a lookup by the id alone would read the whole table.
    /// </summary>
    [Fact]
    public async Task JobRoutes_NeedTheJobsPrimaryKey()
    {
        var client = await StartAsync(o => o.AllowAnonymous().DisableAntiforgery());

        await SeedAsync();

        var job = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == null).SingleAsync());

        foreach (var request in new[]
        {
            new HttpRequestMessage(HttpMethod.Get, $"/api/XactJobs/jobs/{job.Id}"),
            new HttpRequestMessage(HttpMethod.Get, $"/api/XactJobs/jobs/{job.Id}?queue={Uri.EscapeDataString(job.Queue)}"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/XactJobs/jobs/{job.Id}/run-now"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/XactJobs/jobs/{job.Id}/cancel?queue=x&scheduledAt=not-a-time"),
        })
        {
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("primary key", await response.Content.ReadAsStringAsync());
        }

        var found = await client.GetAsync($"/api/XactJobs/jobs/{job.Id}{JobKey.Of(job).ToQueryString()}");

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Contains($"\"id\":{job.Id}", await found.Content.ReadAsStringAsync());

        // another scheduled time: another row
        var otherTime = new JobKey(job.Queue, job.ScheduledAt.AddSeconds(1), job.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/XactJobs/jobs/{job.Id}{otherTime.ToQueryString()}")).StatusCode);
    }

    [Fact]
    public async Task PeriodicIds_WithSlashes_CanBeUsedInTheApi()
    {
        var client = await StartAsync(o => o.AllowAnonymous().DisableAntiforgery());

        await WithDbAsync(async db =>
        {
            await db.JobEnsurePeriodicAsync<SampleJobs>(x => x.Run(), "reports/daily", "0 0 0 * * *", CancellationToken.None);
            return await db.SaveChangesAsync();
        });

        var response = await client.PostAsync("/api/XactJobs/periodic/reports%2Fdaily/pause", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await WithDbAsync(db => db.PeriodicJobs.Select(p => p.IsActive).SingleAsync()));
    }

    [GeneratedRegex("""<meta name="xactjobs-antiforgery" content="(?<token>[^"]+)""")]
    private static partial Regex AntiforgeryMeta();

    [GeneratedRegex("""hx-post="(?<url>[^"]+)""")]
    private static partial Regex HxPost();

    private async Task<HttpClient> StartAsync(Action<XactJobsUIOptionsBuilder>? configure)
    {
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // the Razor Pages are discovered from the application assembly and its dependencies
            ApplicationName = typeof(XactJobsUIOptions).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<UiTestDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddXactJobsUI<UiTestDbContext>(configure);

        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization(o => o.AddPolicy("Admins", p => p.RequireRole("admin")));

        _app = builder.Build();

        _app.Use((context, next) =>
        {
            var remote = context.Request.Headers["X-Test-Remote"].ToString();
            context.Connection.RemoteIpAddress = string.IsNullOrEmpty(remote) ? null : IPAddress.Parse(remote);
            return next(context);
        });

        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapXactJobsUI();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());

        await _app.StartAsync();

        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote", Local);
        return client;
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string? remote, string? user = null, string? roles = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // replaces the default local address
        request.Headers.Add("X-Test-Remote", remote ?? "");

        if (user != null) request.Headers.Add(TestAuthHandler.UserHeader, user);
        if (roles != null) request.Headers.Add(TestAuthHandler.RolesHeader, roles);

        return client.SendAsync(request);
    }

    /// <summary>
    /// A batch with a pending job, a job that succeeded and one that failed, a waiting continuation, and a skipped one.
    /// </summary>
    private async Task SeedBatchAsync()
    {
        var batchId = Guid.Parse(SeedBatch);
        var typeName = typeof(SampleJobs).AssemblyQualifiedName!;

        await WithDbAsync(async db =>
        {
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddHours(1), typeName, "Run()", "[]", QueueNames.Default) { BatchId = batchId });

            db.JobBatch(batchId).ContinueWith<SampleJobs>(x => x.Run(), XactJobContinuation.OnFailure);

            db.JobHistory.Add(new XactJobHistory(300, DateTime.UtcNow.AddMinutes(-5), XactJobStatus.Completed, DateTime.UtcNow.AddMinutes(-6),
                                                 typeName, "Run()", "[]", QueueNames.Default) { BatchId = batchId });
            db.JobHistory.Add(new XactJobHistory(301, DateTime.UtcNow.AddMinutes(-4), XactJobStatus.Cancelled, DateTime.UtcNow.AddMinutes(-6),
                                                 typeName, "Run()", "[]", QueueNames.Default, errorCount: 3, errorMessage: "Boom", errorStackTrace: "at Somewhere()") { BatchId = batchId });
            db.JobHistory.Add(new XactJobHistory(302, DateTime.UtcNow.AddMinutes(-3), XactJobStatus.Skipped, DateTime.UtcNow.AddMinutes(-3),
                                                 typeName, "Run()", "[]", QueueNames.Default, errorMessage: "Skipped: the batch 1 had no failed jobs")
            {
                AwaitsBatchId = Guid.NewGuid(),
                ContinuationCondition = XactJobContinuation.OnFailure
            });

            return await db.SaveChangesAsync();
        });
    }

    private async Task SeedAsync()
    {
        await WithDbAsync(async db =>
        {
            await db.JobEnsurePeriodicAsync<SampleJobs>(x => x.Run(), "daily", "0 0 0 * * *", CancellationToken.None);

            db.JobEnqueue<SampleJobs>(x => x.Run());

            foreach (var status in Enum.GetValues<XactJobStatus>())
            {
                db.JobHistory.Add(new XactJobHistory(100 + (int)status, DateTime.UtcNow.AddMinutes(-5), status, DateTime.UtcNow.AddMinutes(-6),
                                                     typeof(SampleJobs).AssemblyQualifiedName!, nameof(SampleJobs.Run), "[]", QueueNames.Default,
                                                     errorCount: 1, errorMessage: "Boom <b>\"quoted\"</b>", errorStackTrace: "at Somewhere()"));
            }

            return await db.SaveChangesAsync();
        });
    }

    private async Task<T> WithDbAsync<T>(Func<UiTestDbContext, Task<T>> action)
    {
        using var scope = _app!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<UiTestDbContext>());
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";
        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].ToString();

            if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.Name, user) };
            claims.AddRange(Request.Headers[RolesHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
