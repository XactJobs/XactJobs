using System.Text.RegularExpressions;

namespace XactJobs.UI.Services;

/// <summary>
/// The dashboard's search: the rows whose type, method or periodic job contains the text, ignoring case, or only one of them with
/// <c>type:</c>, <c>method:</c> or <c>periodic:</c>. It only narrows a list that is already bounded (a queue, a period, the periodic
/// jobs): a search for text inside a value can't use an index, so the database reads the rows of the list until it fills a page.
/// </summary>
internal sealed partial record JobSearch(JobSearchField Field, string Term)
{
    /// <summary>The longest text searched for: the rest is ignored.</summary>
    public const int MaxLength = 200;

    /// <summary>Null for no text (also after a field's prefix).</summary>
    public static JobSearch? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (text.Length > MaxLength) text = text[..MaxLength];

        var match = FieldPrefix().Match(text);

        var field = !match.Success ? JobSearchField.Any : match.Groups[1].Value.ToLowerInvariant() switch
        {
            "type" => JobSearchField.Type,
            "method" => JobSearchField.Method,
            _ => JobSearchField.Periodic,
        };

        // lowercase here, and LOWER() in the database: PostgreSQL and Oracle compare case
        var term = (match.Success ? match.Groups[2].Value : text).Trim().ToLowerInvariant();

        return term.Length == 0 ? null : new JobSearch(field, term);
    }

    /// <summary>The jobs or history rows that match.</summary>
    public IQueryable<T> Apply<T>(IQueryable<T> query) where T : XactJobBase
    {
        var term = Term;

        return Field switch
        {
            JobSearchField.Type => query.Where(j => j.TypeName.ToLower().Contains(term)),
            JobSearchField.Method => query.Where(j => j.MethodName.ToLower().Contains(term)),
            JobSearchField.Periodic => query.Where(j => j.PeriodicJobId != null && j.PeriodicJobId.ToLower().Contains(term)),
            _ => query.Where(j => j.TypeName.ToLower().Contains(term)
                || j.MethodName.ToLower().Contains(term)
                || (j.PeriodicJobId != null && j.PeriodicJobId.ToLower().Contains(term))),
        };
    }

    /// <summary>The periodic jobs that match: their id is the periodic field.</summary>
    public IQueryable<XactJobPeriodic> Apply(IQueryable<XactJobPeriodic> query)
    {
        var term = Term;

        return Field switch
        {
            JobSearchField.Type => query.Where(p => p.TypeName.ToLower().Contains(term)),
            JobSearchField.Method => query.Where(p => p.MethodName.ToLower().Contains(term)),
            JobSearchField.Periodic => query.Where(p => p.Id.ToLower().Contains(term)),
            _ => query.Where(p => p.TypeName.ToLower().Contains(term)
                || p.MethodName.ToLower().Contains(term)
                || p.Id.ToLower().Contains(term)),
        };
    }

    [GeneratedRegex(@"^\s*(type|method|periodic)\s*:(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FieldPrefix();
}

internal enum JobSearchField { Any, Type, Method, Periodic }
