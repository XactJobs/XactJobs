namespace XactJobs.UI.Services.Models;

public enum ActionStatus
{
    Done,
    NotFound,
    /// <summary>The job is in a state that doesn't allow the action, e.g. it's running.</summary>
    Conflict,
}

/// <summary>
/// The outcome of a dashboard action, with a message to show to the user.
/// </summary>
public record ActionResultDto(ActionStatus Status, string Message)
{
    public static ActionResultDto Done(string message) => new(ActionStatus.Done, message);
    public static ActionResultDto NotFound(string message) => new(ActionStatus.NotFound, message);
    public static ActionResultDto Conflict(string message) => new(ActionStatus.Conflict, message);
}
