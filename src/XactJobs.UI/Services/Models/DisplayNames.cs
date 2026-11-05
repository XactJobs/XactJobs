namespace XactJobs.UI.Services.Models;

internal static class DisplayNames
{
    /// <summary>
    /// The first 8 hex digits of a batch id, like a short git hash. The full id is in the tooltip and the link.
    /// </summary>
    public static string Batch(Guid batchId) => batchId.ToString("N")[..8];

    public static string? Condition(XactJobContinuation? condition) => condition switch
    {
        null => null,
        XactJobContinuation.OnSuccess => "on success",
        XactJobContinuation.OnFailure => "on failure",
        XactJobContinuation.OnFinish => "on finish",
        _ => condition.ToString(),
    };

    /// <summary>
    /// The type's name without its namespace and assembly, e.g. "OrderJobs",
    /// and "Maintenance&lt;AppDbContext&gt;" for "Ns.Maintenance`1[[Ns.AppDbContext, App]], Lib".
    /// </summary>
    public static string Type(string typeName)
    {
        var tick = typeName.IndexOf('`');
        var comma = typeName.IndexOf(',');

        if (tick < 0 || (comma >= 0 && comma < tick))
        {
            return WithoutNamespace(comma < 0 ? typeName : typeName[..comma]);
        }

        var name = WithoutNamespace(typeName[..tick]);
        var argsStart = typeName.IndexOf('[', tick);

        if (argsStart < 0)
        {
            return name;
        }

        // the generic arguments are "[[Arg1, Assembly],[Arg2, Assembly]]": take the ones at depth 2
        var args = new List<string>();
        var depth = 0;
        var argStart = 0;

        for (var i = argsStart; i < typeName.Length; i++)
        {
            if (typeName[i] == '[')
            {
                if (++depth == 2) argStart = i + 1;
            }
            else if (typeName[i] == ']')
            {
                if (depth-- == 2) args.Add(Type(typeName[argStart..i]));
                if (depth == 0) break;
            }
        }

        return $"{name}<{string.Join(", ", args)}>";
    }

    private static string WithoutNamespace(string typeName) => typeName.Trim().Split('.').Last();

    /// <summary>
    /// The method's name without its signature (jobs store it as e.g. "Send(System.String, System.Int32)").
    /// </summary>
    public static string Method(string methodName)
    {
        var signatureStart = methodName.IndexOf('(');

        return signatureStart < 0 ? methodName : methodName[..signatureStart];
    }

    public static string Queue(string queue) => queue switch
    {
        QueueNames.Default => "Default",
        QueueNames.Priority => "Priority",
        QueueNames.LongRunning => "Long running",
        _ => queue,
    };
}
