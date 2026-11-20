using Microsoft.AspNetCore.Http;

namespace XactJobs.UI.Pages;

/// <summary>
/// The filters live in the URL: the links to the other pages (the menu, the tabs) and to the table's pages carry all of them, also
/// the ones the page itself doesn't have (they're ignored there), so that they're kept from page to page, separately in each tab.
/// </summary>
internal static class FilterLinks
{
    public static readonly string[] Names = ["queue", "search", "period", "day", "tz"];

    /// <summary>The filters of the request, each one starting with &amp;.</summary>
    public static string Query(HttpRequest request)
    {
        return string.Concat(Names.Select(name => Part(name, request.Query[name])));
    }

    /// <summary>A link to a page, with the request's filters.</summary>
    public static string Url(string path, HttpRequest request)
    {
        var query = Query(request);

        return query.Length == 0 ? path : path + "?" + query[1..];
    }

    public static string Part(string name, string? value)
    {
        return string.IsNullOrEmpty(value) ? "" : "&" + name + "=" + Uri.EscapeDataString(value);
    }
}
