using Strategy.Modules.Requests.Contracts;

namespace Strategy.Modules.Requests.Domain;

/// <summary>Lifecycle actions of a request (docs/design/requests.md §2.3).</summary>
public static class LifecycleActions
{
    public const string Save = "save";
    public const string Submit = "submit";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string StageAction = "stage-action";
    public const string Cancel = "cancel";

    public static readonly IReadOnlyList<string> All = [Save, Submit, Approved, Rejected, Returned, StageAction, Cancel];
}

/// <summary>Explicit transition table (unit-tested for every allowed and forbidden pair). The target stage comes from the type's stages.</summary>
public static class RequestLifecycle
{
    private static readonly Dictionary<(string Status, string Action), bool> Allowed = new()
    {
        [(RequestStatuses.Draft, LifecycleActions.Save)] = true,
        [(RequestStatuses.Draft, LifecycleActions.Submit)] = true,
        [(RequestStatuses.Draft, LifecycleActions.Cancel)] = true,
        [(RequestStatuses.Returned, LifecycleActions.Save)] = true,
        [(RequestStatuses.Returned, LifecycleActions.Submit)] = true,
        [(RequestStatuses.Returned, LifecycleActions.Cancel)] = true,
        [(RequestStatuses.InReview, LifecycleActions.Approved)] = true,
        [(RequestStatuses.InReview, LifecycleActions.Rejected)] = true,
        [(RequestStatuses.InReview, LifecycleActions.Returned)] = true,
        [(RequestStatuses.InReview, LifecycleActions.Cancel)] = true,
        [(RequestStatuses.InProgress, LifecycleActions.StageAction)] = true,
        [(RequestStatuses.InProgress, LifecycleActions.Cancel)] = true,
    };

    public static bool Allows(string status, string action) => Allowed.ContainsKey((status, action));

    /// <summary>The status of a request entering <paramref name="stage"/> (null: completed).</summary>
    public static string StatusOf(StageDefinition? stage) => stage is null
        ? RequestStatuses.Completed
        : stage.Kind == StageKinds.Approval ? RequestStatuses.InReview : RequestStatuses.InProgress;

    /// <summary>The stage after <paramref name="current"/> in the type's order (null: none — the request completes).</summary>
    public static StageDefinition? After(IReadOnlyList<StageDefinition> stages, string? current)
    {
        if (current is null)
        {
            return stages.Count > 0 ? stages[0] : null;
        }

        var index = stages.ToList().FindIndex(s => s.Code == current);
        return index >= 0 && index + 1 < stages.Count ? stages[index + 1] : null;
    }
}
