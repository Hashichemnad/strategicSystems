using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Access;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

/// <summary>Permission and record-role codes (docs/design/tasks.md §6).</summary>
public static class TaskPermissions
{
    public const string SystemCode = "tasks";
    public const string ResourceType = TaskItem.EntityType;

    public const string Read = "tasks.task.read";
    public const string Create = "tasks.task.create";
    public const string Manage = "tasks.task.manage";
    public const string Work = "tasks.task.work";
    public const string Comment = "tasks.task.comment";
    public const string Upload = "tasks.task.upload";
    public const string FollowUp = "tasks.task.follow-up";
    public const string OnBehalf = "tasks.task.on-behalf";
    public const string DashboardRead = "tasks.dashboard.read";
    public const string PerformanceRead = "tasks.performance.read";
    public const string SettingsManage = "tasks.settings.manage";
    public const string ReassignmentManage = "tasks.reassignment.manage";

    public const string AiFromText = "tasks.ai.from-text";
    public const string AiSummary = "tasks.ai.summary";
    public const string AiAssignee = "tasks.ai.assignee";
    public const string AiWorkload = "tasks.ai.workload";

    public const string CreatorRole = "creator";
    public const string AssigneeRole = "assignee";
    public const string FollowUpRole = "follow_up";
    public const string FollowUpCommentRole = "follow_up_comment";
    public const string EnteredByRole = "entered_by";

    public const string TaskNumbering = "tasks.task";
    public const string TimesheetNumbering = "tasks.timesheet";

    public const string TypeLookup = "tasks.type";
    public const string SourceLookup = "tasks.source";
    public const string CategoryLookup = "tasks.category";
}

/// <summary>Data scopes of a task (ADR 0015): the assignee is the owner employee, the creator the owner user, the assignee's unit the unit.</summary>
public static class TaskScope
{
    public static readonly ScopeMap<TaskItem> Map = new(
        TaskPermissions.ResourceType, x => x.Id, ownerEmployee: x => x.AssigneeEmployeeId, ownerUser: x => x.CreatorUserId, unit: x => x.UnitId);
}

/// <summary>Catalogue of the core Tasks system (always on, ADR 0036).</summary>
internal sealed class TasksAccessCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(TaskPermissions.SystemCode, SystemKind.Platform, "إدارة المهام", "Task management", 60);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(TaskPermissions.Read, "عرض المهام", "View tasks"),
        new(TaskPermissions.Create, "إنشاء المهام", "Create tasks", Scopable: false),
        new(TaskPermissions.Manage, "إدارة المهام التي أنشأها (تعديل، إعادة إسناد، إلغاء، اعتماد)", "Manage tasks they created (edit, reassign, cancel, approve)"),
        new(TaskPermissions.Work, "العمل على المهام المسندة (قبول، إيقاف، تسليم)", "Work on assigned tasks (accept, pause, submit)"),
        new(TaskPermissions.Comment, "التعليق على المهام", "Comment on tasks"),
        new(TaskPermissions.Upload, "رفع الملفات على المهام", "Upload files to tasks"),
        new(TaskPermissions.FollowUp, "إضافة موظفي المتابعة", "Add follow-up employees"),
        new(TaskPermissions.OnBehalf, "إنشاء المهام نيابة عن المدير", "Create tasks on behalf of the manager", Scopable: false),
        new(TaskPermissions.DashboardRead, "عرض لوحة المهام", "View the task dashboard", Scopable: false),
        new(TaskPermissions.PerformanceRead, "عرض مؤشرات أداء الموظفين في المهام", "View employees' task performance input"),
        new(TaskPermissions.SettingsManage, "إدارة إعدادات المهام", "Manage task settings", Scopable: false),
        new(TaskPermissions.ReassignmentManage, "إعادة إسناد المهام المفتوحة لموظف", "Reassign the open tasks of an employee"),
        new(TaskPermissions.AiFromText, "إنشاء مسودات مهام من نص بالذكاء الاصطناعي", "Draft tasks from text with AI", Scopable: false),
        new(TaskPermissions.AiSummary, "تلخيص المهام بالذكاء الاصطناعي", "Summarise tasks with AI", Scopable: false),
        new(TaskPermissions.AiAssignee, "اقتراح المكلفين", "Suggest assignees", Scopable: false),
        new(TaskPermissions.AiWorkload, "رؤى عبء العمل", "Workload insights", Scopable: false),
        ResponsibilityPermissions.Definition(TaskPermissions.SystemCode),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("employee", "موظف (المهام)", "Employee (tasks)",
            [new(TaskPermissions.Read, DataScope.Own), new(TaskPermissions.Create), new(TaskPermissions.AiFromText), new(TaskPermissions.AiSummary),
                new(TaskPermissions.AiAssignee)],
            AssignToEveryone: true),
        new("manager", "مدير (مهام الفريق)", "Manager (team's tasks)",
            [new(TaskPermissions.Read, DataScope.Team), new(TaskPermissions.DashboardRead), new(TaskPermissions.PerformanceRead, DataScope.Team),
                new(TaskPermissions.AiWorkload)],
            AssignToEveryone: true),
        new("secretary", "سكرتير الإدارة", "Department secretary",
            [new(TaskPermissions.Read, DataScope.Unit), new(TaskPermissions.FollowUp, DataScope.Unit), new(TaskPermissions.OnBehalf)]),
        new("hr", "الموارد البشرية (أداء المهام)", "HR (task performance)", [new(TaskPermissions.PerformanceRead, DataScope.All)]),
        new("administrator", "مسؤول المهام", "Task administrator",
            [new(TaskPermissions.Read, DataScope.All), new(TaskPermissions.DashboardRead), new(TaskPermissions.SettingsManage), new(TaskPermissions.AiWorkload),
                new(TaskPermissions.ReassignmentManage, DataScope.All), new(ResponsibilityPermissions.Manage(TaskPermissions.SystemCode), DataScope.All)],
            RequiresMfa: true),
    ];

    public IEnumerable<RecordRoleDefinition> RecordRoles =>
    [
        new(TaskPermissions.ResourceType, TaskPermissions.CreatorRole, "منشئ المهمة", "Task creator",
            [TaskPermissions.Read, TaskPermissions.Manage, TaskPermissions.Comment, TaskPermissions.Upload]),
        new(TaskPermissions.ResourceType, TaskPermissions.AssigneeRole, "المكلف بالمهمة", "Task assignee",
            [TaskPermissions.Read, TaskPermissions.Work, TaskPermissions.Comment, TaskPermissions.Upload, TaskPermissions.FollowUp]),
        new(TaskPermissions.ResourceType, TaskPermissions.FollowUpRole, "موظف متابعة", "Follow-up employee",
            [TaskPermissions.Read, TaskPermissions.Comment, TaskPermissions.Upload]),
        new(TaskPermissions.ResourceType, TaskPermissions.FollowUpCommentRole, "موظف متابعة (تعليق فقط)", "Follow-up employee (comment only)",
            [TaskPermissions.Read, TaskPermissions.Comment]),
        new(TaskPermissions.ResourceType, TaskPermissions.EnteredByRole, "مدخل المهمة نيابة عن المدير", "Entered on behalf of the manager",
            [TaskPermissions.Read, TaskPermissions.Comment, TaskPermissions.Upload]),
    ];

    public static readonly NumberingDefinition TaskNumbering = new(
        TaskPermissions.TaskNumbering, TaskPermissions.SystemCode, "رقم المهمة", "Task number", "TASK-{SEQ:5}");

    public static readonly NumberingDefinition TimesheetNumbering = new(
        TaskPermissions.TimesheetNumbering, TaskPermissions.SystemCode, "رقم فترة سجل الوقت", "Timesheet period number", "TS-{SEQ:7}");

    public static readonly LookupTypeDefinition Types = new(TaskPermissions.TypeLookup, TaskPermissions.SystemCode, "أنواع المهام", "Task types",
    [
        new("manager-assignment", "تكليف من المدير", "Manager assignment"),
        new("project", "مهمة مشروع", "Project task"),
        new("meeting-decision", "قرار اجتماع", "Meeting decision"),
        new("personal", "مهمة شخصية", "Personal to-do"),
    ]);

    public static readonly LookupTypeDefinition Sources = new(TaskPermissions.SourceLookup, TaskPermissions.SystemCode, "مصادر المهام", "Task sources",
    [
        new("manager-assignment", "تكليف من المدير", "Manager assignment"),
        new("dg-office", "مكتب المدير العام", "DG office"),
        new("committee-decision", "قرار لجنة", "Committee decision"),
        new("project", "مشروع", "Project"),
        new("other", "أخرى", "Other"),
    ]);

    public static readonly LookupTypeDefinition Categories = new(TaskPermissions.CategoryLookup, TaskPermissions.SystemCode, "فئات المهام", "Task categories",
    [
        new("general", "عامة", "General"),
    ]);
}
