namespace Strategy.Modules.Tasks.Contracts;

/// <summary>How another module reads the tasks of one of its records (by reference, never through the Tasks DbContext).</summary>
public interface ITaskDirectory
{
    /// <summary>Open tasks linked to the record (all of them, confidential included: a count reveals no content). For "close its tasks?" prompts.</summary>
    Task<LinkedTaskCounts> CountLinkedAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);
}

public sealed record LinkedTaskCounts(int Open, int Overdue, int Total);
