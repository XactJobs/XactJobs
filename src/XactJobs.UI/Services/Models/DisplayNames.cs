namespace XactJobs.UI.Services.Models;

internal static class DisplayNames
{
    public static string Type(string typeName) => typeName.Split(',')[0].Split('.').LastOrDefault() ?? typeName;

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
