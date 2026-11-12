using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Requests.Reporting;

/// <summary>
/// Labels of the Request Framework's codes for reports, dossiers and feeds (ADR 0209): fixed codes as <c>web/src/i18n/*/requests.json</c> words
/// them; request types and stages are configuration, named by <see cref="RequestDimensionLabels"/>.
/// </summary>
internal static class RequestChoices
{
    public static readonly FieldLabelSource Types = new(RequestPermissions.SystemCode, RequestDimensionLabels.Type);

    public static readonly FieldLabelSource Stages = new(RequestPermissions.SystemCode, RequestDimensionLabels.Stage);

    public static readonly IReadOnlyList<RecordFieldChoice> Statuses =
    [
        new(RequestStatuses.Draft, "مسودة", "Draft"),
        new(RequestStatuses.InReview, "قيد المراجعة", "In review"),
        new(RequestStatuses.Returned, "معاد للتصحيح", "Returned for correction"),
        new(RequestStatuses.InProgress, "قيد المعالجة", "In progress"),
        new(RequestStatuses.Completed, "مكتمل", "Completed"),
        new(RequestStatuses.Rejected, "مرفوض", "Rejected"),
        new(RequestStatuses.Cancelled, "ملغى", "Cancelled"),
    ];

    /// <summary>The channel a request came from.</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> Channels =
    [
        new("ui", "التطبيق", "Application"),
        new("embed", "صفحة الشبكة الداخلية", "Intranet page"),
        new("mcp", "مساعد ذكي (MCP)", "AI assistant (MCP)"),
        new("ai", "الذكاء الاصطناعي", "AI"),
        new("system", "النظام", "System"),
    ];

    /// <summary>Outcomes the built-in request types report (Document Registry, IP Registry).</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> Outcomes =
    [
        new("published", "منشور", "Published"),
        new("registered", "مسجّل", "Registered"),
        new("withdrawn", "مسحوب", "Withdrawn"),
    ];
}
