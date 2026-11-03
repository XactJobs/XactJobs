namespace XactJobs.UI.Services.Models;

internal static class DisplayNames
{
    public static string Type(string typeName) => typeName.Split(',')[0].Split('.').LastOrDefault() ?? typeName;

    public static string Queue(string queue) => queue switch
    {
        QueueNames.Default => "Default",
        QueueNames.Priority => "Priority",
        QueueNames.LongRunning => "Long running",
        _ => queue,
    };
}
