using System.Security.Claims;
using System.Text.Json.Nodes;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Requests.Contracts;

public static class StageKinds
{
    public const string Approval = "approval";
    public const string Processing = "processing";

    public static readonly IReadOnlyList<string> All = [Approval, Processing];
}

/// <summary>Kinds of processing-stage actions (docs/design/requests.md §2.4).</summary>
public static class ActionKinds
{
    /// <summary>Moves to <see cref="StageAction.ToStage"/>, or completes the request when it is null.</summary>
    public const string Complete = "complete";

    /// <summary>A labelled action (Publish, Record filing…) moving to <see cref="StageAction.ToStage"/> (null: completes).</summary>
    public const string Custom = "custom";

    /// <summary>Back to the requester for correction.</summary>
    public const string Return = "return";

    /// <summary>Ends the request as rejected.</summary>
    public const string Reject = "reject";

    public static readonly IReadOnlyList<string> All = [Complete, Custom, Return, Reject];
}

/// <summary>An action of a processing stage, with an optional mini-form.</summary>
public sealed record StageAction
{
    public string Code { get; init; } = string.Empty;

    public string Kind { get; init; } = ActionKinds.Complete;

    public string? LabelAr { get; init; }

    public string? LabelEn { get; init; }

    /// <summary>Target stage code; null completes the request (for complete / custom).</summary>
    public string? ToStage { get; init; }

    public FormSchema? Form { get; init; }

    public bool RequiresComment { get; init; }

    /// <summary>Visual tone: primary, secondary, danger.</summary>
    public string Tone { get; init; } = "primary";
}

/// <summary>One stage of a request type.</summary>
public sealed record StageDefinition
{
    public string Code { get; init; } = string.Empty;

    public string Kind { get; init; } = StageKinds.Approval;

    public string? NameAr { get; init; }

    public string? NameEn { get; init; }

    /// <summary>Approval stages: the engine's process key.</summary>
    public string? ProcessKey { get; init; }

    /// <summary>Approval stages: fields the approver completes before the approval can complete (ADR 0081).</summary>
    public FormSchema? ApproveForm { get; init; }

    /// <summary>Processing stages: role keys whose holders process (empty: the system's office role).</summary>
    public IReadOnlyList<string> ProcessorRoleKeys { get; init; } = [];

    /// <summary>Processing stages: working hours to act (null: no SLA).</summary>
    public int? SlaHours { get; init; }

    public IReadOnlyList<StageAction> Actions { get; init; } = [];

    /// <summary>Built-in stage defined in code (cannot be removed in the builder).</summary>
    public bool System { get; init; }
}

/// <summary>Who may submit a type: everyone, or holders of roles, or members of units (and below).</summary>
public sealed record SubmitterRule(bool Everyone = true, IReadOnlyList<string>? RoleKeys = null, IReadOnlyList<Guid>? UnitIds = null);

/// <summary>A bilingual HTML template with merge fields (ADR 0088).</summary>
public sealed record PdfTemplate
{
    public string Code { get; init; } = string.Empty;

    public string? NameAr { get; init; }

    public string? NameEn { get; init; }

    public string? HtmlAr { get; init; }

    public string? HtmlEn { get; init; }

    /// <summary><c>requester</c>, <c>processor</c> or <c>admin</c>.</summary>
    public string Audience { get; init; } = "processor";
}

/// <summary>
/// A built-in request type declared in code by its owning module (ADR 0080): code-defined fields and stages; seeded once, then admins may
/// relabel, add extra fields and change catalogue / submitter / SLA settings.
/// </summary>
public sealed record RequestTypeRegistration(
    string Code,
    string SystemCode,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string NumberPrefix,
    string Icon,
    FormSchema Form,
    IReadOnlyList<StageDefinition> Stages,
    string? Category = null,
    bool ShowInCatalogue = true,
    int Sort = 100,
    IReadOnlyList<PdfTemplate>? Templates = null);

/// <summary>Request statuses (docs/design/requests.md §2.3).</summary>
public static class RequestStatuses
{
    public const string Draft = "draft";
    public const string InReview = "in_review";
    public const string Returned = "returned";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Draft, InReview, Returned, InProgress, Completed, Rejected, Cancelled];

    public static bool IsFinal(string status) => status is Completed or Rejected or Cancelled;

    public static bool IsOpen(string status) => !IsFinal(status);
}

public static class TransitionKinds
{
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
    public const string Action = "action";
}

/// <summary>A request as a handler sees it. <see cref="Data"/> holds decrypted values (server side only — never log or return it).</summary>
public sealed record RequestSnapshot(
    Guid Id,
    string Number,
    string TypeCode,
    Guid? RequesterUserId,
    Guid? RequesterEmployeeId,
    Guid? UnitId,
    string Status,
    string? Stage,
    FormValues Data,
    JsonObject ProcessData,
    string? SubjectType,
    Guid? SubjectId);

/// <summary>Context of a handler's validation / action filtering.</summary>
public sealed record RequestHandlerContext(RequestSnapshot Request, ClaimsPrincipal User);

/// <summary>
/// A transition the handler reacts to (inside the framework's transaction). <see cref="ActionValues"/>: the mini-form values of the action
/// or of the approve form (decrypted). <see cref="FromStage"/> / <see cref="ToStage"/> as the framework computed them.
/// </summary>
public sealed record RequestTransition(
    string Kind,
    RequestSnapshot Request,
    string? FromStage,
    string? ToStage,
    string? ActionCode,
    FormValues ActionValues,
    string? Comment,
    Guid? ActorUserId);

/// <summary>What a handler wants after a transition. Null members keep the framework's choice.</summary>
public sealed record RequestTransitionOutcome(
    string? SubjectType = null,
    Guid? SubjectId = null,
    string? Outcome = null,
    string? NextStage = null,
    string? TitleAr = null,
    string? TitleEn = null)
{
    public static readonly RequestTransitionOutcome None = new();
}

/// <summary>
/// Business logic of a built-in type (Document Registry, IP…). Called by the framework inside its transaction: join it with
/// <c>db.JoinTransactionAsync(transactionContext)</c>. A failure rolls the action back and is returned to the caller.
/// </summary>
public interface IRequestTypeHandler
{
    string TypeCode { get; }

    /// <summary>Module rules checked on submit (and on save for drafts when <paramref name="submitting"/> is false). Field paths <c>fields.&lt;code&gt;</c>.</summary>
    Task<IReadOnlyList<FieldError>> ValidateAsync(RequestHandlerContext context, bool submitting, CancellationToken cancellationToken);

    /// <summary>The actions of the current processing stage the module allows now (null: all of them).</summary>
    Task<IReadOnlyCollection<string>?> AvailableActionsAsync(RequestHandlerContext context, CancellationToken cancellationToken);

    Task<Result<RequestTransitionOutcome>> OnTransitionAsync(RequestTransition transition, object transactionContext, CancellationToken cancellationToken);
}

/// <summary>A request as another module may see it (badges, links).</summary>
public sealed record RequestBadge(Guid Id, string Number, string TypeCode, string? TitleAr, string? TitleEn, string Status, string? Stage, string Link);

/// <summary>Read access to requests for other modules (the caller's visibility applies; null outside it).</summary>
public interface IRequestDirectory
{
    Task<RequestBadge?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Requests (any visibility) whose subject is the record: the module's own pages show their review history.</summary>
    Task<IReadOnlyList<RequestBadge>> ForSubjectAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default);
}

/// <summary>Facts built-in types need to register their approval processes on requests (ADR 0081).</summary>
public static class RequestResource
{
    /// <summary>Resource type of every request (approval subject, record roles, audit).</summary>
    public const string Type = "requests.request";

    /// <summary>Permission that lets someone read a request (approval instance visibility).</summary>
    public const string ReadPermission = "requests.request.read";

    /// <summary>Record role granted to approvers while their task is open.</summary>
    public const string ApproverRole = "approver";

    public const string LinkTemplate = "/requests/{id}";

    /// <summary>Entry-condition variable with the request type code.</summary>
    public const string TypeVariable = "requestType";

    public static string Link(Guid id) => LinkTemplate.Replace("{id}", id.ToString("D"), StringComparison.Ordinal);
}
