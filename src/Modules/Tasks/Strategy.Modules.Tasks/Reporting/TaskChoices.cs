using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Reporting;

namespace Strategy.Modules.Tasks.Reporting;

/// <summary>AR / EN labels of Task Management's fixed codes for reports, dossiers and feeds (ADR 0209), as <c>web/src/i18n/*/tasks.json</c> words them.</summary>
internal static class TaskChoices
{
    /// <summary>Statuses as the full workflow names them (the simple workflow shares the codes).</summary>
    public static readonly IReadOnlyList<RecordFieldChoice> Statuses =
    [
        new(TaskStatuses.Draft, "مسودة", "Draft"),
        new(TaskStatuses.Assigned, "مسندة", "Assigned"),
        new(TaskStatuses.InProgress, "قيد التنفيذ", "In progress"),
        new(TaskStatuses.OnHold, "متوقفة", "On hold"),
        new(TaskStatuses.Submitted, "مسلّمة", "Submitted"),
        new(TaskStatuses.Approved, "معتمدة", "Approved"),
        new(TaskStatuses.Rejected, "مرفوضة", "Rejected"),
        new(TaskStatuses.ToDo, "للتنفيذ", "To do"),
        new(TaskStatuses.Done, "منجزة", "Done"),
        new(TaskStatuses.Cancelled, "ملغاة", "Cancelled"),
    ];

    public static readonly IReadOnlyList<RecordFieldChoice> Priorities =
    [
        new(TaskPriorities.High, "عالية", "High"),
        new(TaskPriorities.Medium, "متوسطة", "Medium"),
        new(TaskPriorities.Low, "منخفضة", "Low"),
    ];
}
