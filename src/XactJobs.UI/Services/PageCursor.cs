using System.Globalization;

namespace XactJobs.UI.Services;

/// <summary>
/// A <see cref="Models.CursorPageDto{T}"/>'s cursor: the time and the id of the row a page starts after, and whether it goes to the
/// older or the newer rows. Written as text, e.g. <c>o638675712000000000_42</c>.
/// </summary>
internal readonly record struct PageCursor(bool IsNewer, DateTime At, long Id)
{
    public static PageCursor Older(DateTime at, long id) => new(false, at, id);

    public static PageCursor Newer(DateTime at, long id) => new(true, at, id);

    public override string ToString() =>
        (IsNewer ? "n" : "o") + At.Ticks.ToString(CultureInfo.InvariantCulture) + "_" + Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Null for null or text that isn't a cursor: the newest page.</summary>
    public static PageCursor? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] is not ('o' or 'n')) return null;

        var separator = text.IndexOf('_');

        if (separator < 0
            || !long.TryParse(text.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
            || !long.TryParse(text.AsSpan(separator + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return new PageCursor(text[0] == 'n', new DateTime(ticks, DateTimeKind.Utc), id);
    }
}
