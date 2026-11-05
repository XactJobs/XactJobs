using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using XactJobs.UI.Security;

namespace XactJobs.UI.Api;

/// <summary>
/// The dashboard's scripts, styles and fonts, embedded in the assembly (the Assets folder) and served under the dashboard's path.
/// So they work without static files middleware, in any environment, whether the application is published or not, and without a CDN.
/// </summary>
internal sealed class XactJobsAssets
{
    // the logical name of the embedded files, set in the project file
    private const string ResourcePrefix = "XactJobs.UI/Assets/";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly Assembly _assembly = typeof(XactJobsAssets).Assembly;
    private readonly Dictionary<string, string> _resources;
    private readonly ConcurrentDictionary<string, string> _versions = new(StringComparer.Ordinal);
    private readonly XactJobsUIOptions _options;

    public XactJobsAssets(XactJobsUIOptions options)
    {
        _options = options;

        // the directory separator in the names is the one of the machine that built the package
        _resources = _assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(name => name[ResourcePrefix.Length..].Replace('\\', '/'), name => name, StringComparer.Ordinal);
    }

    public string BasePath => _options.BasePath + "/assets";

    /// <summary>
    /// The URL of a file, relative to the application, with a version that changes with its content, so browsers can cache it for good.
    /// </summary>
    public string Path(string file)
    {
        return $"{BasePath}/{file}?v={GetVersion(file)}";
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup(BasePath)
            .RequireDashboardAccess(_options)
            .MapGet("/{**file}", (HttpContext httpContext, string file) => Serve(httpContext, file))
            .WithName("XactJobsAssets")
            .ExcludeFromDescription();
    }

    private IResult Serve(HttpContext httpContext, string file)
    {
        if (!_resources.TryGetValue(file, out var resourceName))
        {
            return Results.NotFound();
        }

        var version = GetVersion(file);

        // a URL with the current version never changes; any other is checked again each time (the ETag spares the download)
        httpContext.Response.Headers.CacheControl = httpContext.Request.Query["v"] == version
            ? "public, max-age=31536000, immutable"
            : "no-cache";

        if (!ContentTypes.TryGetContentType(file, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = _assembly.GetManifestResourceStream(resourceName)!;

        return Results.File(stream, contentType, entityTag: new EntityTagHeaderValue($"\"{version}\""));
    }

    private string GetVersion(string file)
    {
        return _versions.GetOrAdd(file, f =>
        {
            if (!_resources.TryGetValue(f, out var resourceName))
            {
                throw new InvalidOperationException($"The dashboard has no file '{f}'.");
            }

            using var stream = _assembly.GetManifestResourceStream(resourceName)!;

            return Convert.ToHexString(SHA256.HashData(stream).AsSpan(0, 8)).ToLowerInvariant();
        });
    }
}
