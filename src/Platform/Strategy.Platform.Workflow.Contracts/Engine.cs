using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Workflow;

/// <summary>The record being approved, with the attributes visibility checks need (snapshotted on the instance).</summary>
public sealed record ApprovalSubject(
    string ResourceType,
    Guid ResourceId,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    Guid? OwnerEmployeeId,
    Guid? OwnerUserId,
    Guid? UnitId);

/// <summary>A submission: the process, the record, the module's variable values (validated against the declared variables) and a comment.</summary>
public sealed record ApprovalSubmission(string ProcessKey, ApprovalSubject Subject, IReadOnlyDictionary<string, object?> Variables, string? Comment = null);

public static class ApprovalInstanceStatuses
{
    public const string InProgress = "in_progress";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
}

public sealed record ApprovalInstanceSummary(
    Guid InstanceId,
    string ProcessKey,
    string Status,
    int Attempt,
    string? CurrentStepCode,
    string? CurrentStepNameAr,
    string? CurrentStepNameEn,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? CompletedAt);

/// <summary>One submission of a record and the decisions taken in it (oldest decision first).</summary>
public sealed record ApprovalAttempt(ApprovalInstanceSummary Instance, string? SubmitComment, IReadOnlyList<ApprovalDecision> Decisions);

/// <summary>A decision on a step: outcome code (approve / reject / return / custom), comment and when.</summary>
public sealed record ApprovalDecision(string? StepNameAr, string? StepNameEn, string Outcome, string? Comment, DateTimeOffset At);

public sealed record PersonName(Guid? UserId, string? NameAr, string? NameEn);

/// <summary>The pending step of a record as the current user sees it (for current-status providers).</summary>
public sealed record ApprovalStatusView(
    ApprovalInstanceSummary Instance,
    IReadOnlyList<PersonName> Approvers,
    DateTimeOffset? DueAt,
    Guid? MyTaskId,
    IReadOnlyList<string> MyActions);

/// <summary>Used by module handlers. Decisions on tasks go through the workflow API, not this interface.</summary>
public interface IApprovalEngine
{
    /// <summary>
    /// Starts an approval. <paramref name="transactionContext"/> is the module's DbContext with an open transaction: the engine joins it, so
    /// the record's state and the instance commit together (ADR 0023). Fails with <c>workflow.errors.alreadyInProgress</c>,
    /// <c>workflow.errors.noApprover</c>, <c>workflow.errors.unknownProcess</c> or a variable error.
    /// </summary>
    Task<Result<ApprovalInstanceSummary>> SubmitAsync(ApprovalSubmission submission, object transactionContext, CancellationToken cancellationToken = default);

    /// <summary>Cancels the instance in progress for the record (withdrawal, record cancelled).</summary>
    Task<Result<ApprovalInstanceSummary>> CancelAsync(
        string processKey, Guid resourceId, string reason, object transactionContext, CancellationToken cancellationToken = default);

    /// <summary>The newest instance for the record, or null.</summary>
    Task<ApprovalInstanceSummary?> GetLatestAsync(string processKey, Guid resourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every submission of the record, newest first, with the submitter's comment and the decisions taken (approver brief: "what changed since
    /// the last submission"). The caller checks the user may read the record first.
    /// </summary>
    Task<IReadOnlyList<ApprovalAttempt>> ListAttemptsAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default);

    /// <summary>The step in progress for the record with its approvers and the current user's actions; null when nothing is in progress.</summary>
    Task<ApprovalStatusView?> GetStatusViewAsync(string processKey, Guid resourceId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public static class ApprovalEventKinds
{
    public const string StepEntered = "step-entered";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
}

/// <summary>A transition the owning module reacts to (move its own state machine).</summary>
public sealed record ApprovalEvent(
    string Kind,
    string ProcessKey,
    Guid InstanceId,
    string ResourceType,
    Guid ResourceId,
    int Attempt,
    string? StepCode,
    string? Action,
    Guid? ActorUserId,
    Guid? OnBehalfOfUserId,
    string? Comment);

/// <summary>
/// Module hook for its process keys. Runs inside the engine's transaction: join it with
/// <c>db.JoinTransactionAsync(transactionContext)</c> before reading or saving. A failure rolls the decision back.
/// </summary>
public interface IApprovalHook
{
    IReadOnlyCollection<string> ProcessKeys { get; }

    Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken);
}

/// <summary>A value in both languages (status message parameters).</summary>
public sealed record BilingualText(string? Ar, string? En);

/// <summary>A next action offered on the current-status banner (code handled by the record page).</summary>
public sealed record CurrentStatusAction(string Code, string LabelKey, string Kind = "primary", Guid? TaskId = null);

/// <summary>
/// "Next action required + who" (SPEC §0.2) for one record: a translatable message with parameters, who acts, and the actions the
/// current user may take now.
/// </summary>
public sealed record CurrentStatus(
    string MessageKey,
    IReadOnlyDictionary<string, BilingualText> Params,
    string Tone,
    string? ActorKey,
    IReadOnlyList<PersonName> Actors,
    IReadOnlyList<CurrentStatusAction> NextActions,
    string? Stage = null);

/// <summary>Implemented per record type by the owning module. Returns null when the record does not exist or the user may not read it (404).</summary>
public interface ICurrentStatusProvider
{
    string ResourceType { get; }

    Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken);
}

/// <summary>An item that needs the user's action, shown on My Work.</summary>
public sealed record MyWorkItem(
    string SystemCode,
    string ResourceType,
    Guid ResourceId,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string MessageKey,
    string Link,
    DateTimeOffset? Since = null,
    DateOnly? DueDate = null);

/// <summary>"Needs my action" items of one system; only asked when the user can open that system.</summary>
public interface IMyWorkSource
{
    string SystemCode { get; }

    Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>Resolves a team role (Teams module, Phase 7) to employees for <c>teamRole</c> approver rules.</summary>
public interface ITeamRoleResolver
{
    Task<IReadOnlyList<Guid>> ResolveEmployeesAsync(string teamRoleCode, ApprovalSubject subject, CancellationToken cancellationToken);
}

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddApprovalProcess(this IServiceCollection services, ApprovalProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        return services;
    }

    public static IServiceCollection AddApprovalHook<THook>(this IServiceCollection services)
        where THook : class, IApprovalHook =>
        services.AddScoped<IApprovalHook, THook>();

    public static IServiceCollection AddCurrentStatusProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, ICurrentStatusProvider =>
        services.AddScoped<ICurrentStatusProvider, TProvider>();

    public static IServiceCollection AddMyWorkSource<TSource>(this IServiceCollection services)
        where TSource : class, IMyWorkSource =>
        services.AddScoped<IMyWorkSource, TSource>();
}
