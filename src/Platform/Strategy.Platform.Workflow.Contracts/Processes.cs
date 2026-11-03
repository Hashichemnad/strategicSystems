using Strategy.Platform.Kernel.Expressions;

namespace Strategy.Platform.Workflow;

/// <summary>
/// An approval process registered in code by its owning module (ADR 0024). Steps are configuration: <see cref="DefaultSteps"/> seed version 1,
/// after which the Approval Settings centre owns them.
/// </summary>
/// <param name="Key">Stable process key, e.g. <c>portfolio.charter</c>; starts with the system code.</param>
/// <param name="NameAr">Arabic name.</param>
/// <param name="NameEn">English name.</param>
/// <param name="SystemCode">Owning system (grouping, delegation scope, "owning unit of this system").</param>
/// <param name="ResourceType">Record type approved (e.g. <c>sample.item</c>).</param>
/// <param name="ReadPermission">Permission that lets someone read the record (instance visibility, ADR 0025).</param>
/// <param name="LinkTemplate">Deep link of the record in the SPA, <c>{id}</c> replaced by the record id.</param>
/// <param name="Variables">Variables the module supplies at submission (entry conditions may use them, with the built-in ones).</param>
/// <param name="DefaultSteps">Default chain (seeded once).</param>
/// <param name="ApproverRecordRole">Record role (declared by the module for <paramref name="ResourceType"/>) granted to approvers while their task is open.</param>
/// <param name="DescriptionAr">Optional Arabic description.</param>
/// <param name="DescriptionEn">Optional English description.</param>
public sealed record ApprovalProcessDefinition(
    string Key,
    string SystemCode,
    string NameAr,
    string NameEn,
    string ResourceType,
    string ReadPermission,
    string LinkTemplate,
    IReadOnlyList<ExpressionVariable> Variables,
    IReadOnlyList<ApprovalStepDefinition> DefaultSteps,
    string? ApproverRecordRole = null,
    string? DescriptionAr = null,
    string? DescriptionEn = null)
{
    public string LinkFor(Guid resourceId) => LinkTemplate.Replace("{id}", resourceId.ToString("D"), StringComparison.Ordinal);
}

/// <summary>Approver rule kinds (ADR 0025).</summary>
public static class ApproverRuleKinds
{
    public const string Role = "role";
    public const string SectionManager = "sectionManager";
    public const string DepartmentDirector = "departmentDirector";
    public const string HeadOfUnit = "headOfUnit";
    public const string OwningUnit = "owningUnit";
    public const string Employee = "employee";
    public const string RecordRole = "recordRole";
    public const string TeamRole = "teamRole";
    public const string SubmitterManager = "submitterManager";

    public static readonly IReadOnlyList<string> All =
        [Role, SectionManager, DepartmentDirector, HeadOfUnit, OwningUnit, Employee, RecordRole, TeamRole, SubmitterManager];
}

/// <summary>Whose unit an org-relation rule starts from.</summary>
public static class ApproverRuleSubjects
{
    public const string Record = "record";
    public const string Submitter = "submitter";

    /// <summary>The top unit of the record's unit path (the organisation): <c>headOfUnit</c> of it is the General Manager (ADR 0044).</summary>
    public const string Organisation = "organisation";
}

/// <summary>
/// Who approves a step. Only the parameters of <see cref="Kind"/> are used: <c>role</c> → <see cref="RoleKey"/>; <c>sectionManager</c>,
/// <c>departmentDirector</c> → <see cref="Of"/>; <c>headOfUnit</c> → <see cref="UnitId"/> or <see cref="Of"/>; <c>owningUnit</c> →
/// <see cref="SystemCode"/> (default: the process's system); <c>employee</c> → <see cref="EmployeeId"/>; <c>recordRole</c> / <c>teamRole</c>
/// → <see cref="RoleCode"/>.
/// </summary>
public sealed record ApproverRule(
    string Kind,
    string? RoleKey = null,
    string? Of = null,
    Guid? UnitId = null,
    Guid? EmployeeId = null,
    string? RoleCode = null,
    string? SystemCode = null);

/// <summary>Decisions on a task. Custom actions are labelled aliases of one of these outcomes.</summary>
public static class ApprovalActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Return = "return";

    public static readonly IReadOnlyList<string> All = [Approve, Reject, Return];
}

/// <summary>How a step's SLA hours count (ADR 0027): working hours of a business-hours record, or plain calendar hours.</summary>
public static class SlaModes
{
    public const string Working = "working";
    public const string Calendar = "calendar";

    public static readonly IReadOnlyList<string> All = [Working, Calendar];
}

/// <summary>Which business hours a working-hours SLA uses: the default, the record's unit (with inheritance), or a specific record.</summary>
public static class BusinessHoursSources
{
    public const string Default = "default";
    public const string Unit = "unit";
    public const string Specific = "specific";

    public static readonly IReadOnlyList<string> All = [Default, Unit, Specific];
}

public static class NoApproverBehaviours
{
    public const string Block = "block";
    public const string Skip = "skip";

    public static readonly IReadOnlyList<string> All = [Block, Skip];
}

/// <summary>A labelled extra button, e.g. "Approve with conditions" (outcome approve).</summary>
public sealed record CustomApprovalAction(string Code, string LabelAr, string LabelEn, string Outcome);

/// <summary>One step of a chain, as configured in the Approval Settings centre.</summary>
public sealed record ApprovalStepDefinition(
    string Code,
    string? NameAr,
    string? NameEn,
    ApproverRule Rule,
    string? Condition = null,
    IReadOnlyList<string>? Actions = null,
    IReadOnlyList<CustomApprovalAction>? CustomActions = null,
    bool CommentOnApprove = false,
    bool CommentOnReject = true,
    bool CommentOnReturn = true,
    int? SlaHours = null,
    ApproverRule? Escalation = null,
    string OnNoApprover = NoApproverBehaviours.Block,
    bool ExcludeSubmitter = true,
    string? NotificationTemplate = null,
    string SlaMode = SlaModes.Working,
    string BusinessHoursSource = BusinessHoursSources.Default,
    Guid? BusinessHoursId = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EffectiveActions => Actions is { Count: > 0 } ? Actions : ApprovalActions.All;
}

/// <summary>Names of the built-in condition variables computed for every submission (design note §4).</summary>
public static class BuiltInVariables
{
    public const string Submitter = "submitter";
    public const string SubmitterManager = "submitterManager";
    public const string SectionManager = "sectionManager";
    public const string DepartmentDirector = "departmentDirector";
    public const string UnitHead = "unitHead";
    public const string UnitLevel = "unitLevel";
    public const string SubmitterIsUnitHead = "submitterIsUnitHead";

    public static readonly IReadOnlyList<ExpressionVariable> All =
    [
        new(Submitter, ExpressionValueType.Text, "مقدم الطلب", "Submitter"),
        new(SubmitterManager, ExpressionValueType.Text, "مدير مقدم الطلب", "Submitter's manager"),
        new(SectionManager, ExpressionValueType.Text, "رئيس القسم", "Section manager"),
        new(DepartmentDirector, ExpressionValueType.Text, "مدير الإدارة", "Department director"),
        new(UnitHead, ExpressionValueType.Text, "رئيس الوحدة", "Head of the record's unit"),
        new(UnitLevel, ExpressionValueType.Text, "مستوى الوحدة", "Level of the record's unit"),
        new(SubmitterIsUnitHead, ExpressionValueType.Boolean, "مقدم الطلب رئيس الوحدة", "Submitter heads the unit"),
    ];
}
