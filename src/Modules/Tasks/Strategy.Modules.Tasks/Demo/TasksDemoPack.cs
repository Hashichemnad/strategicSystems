using Strategy.Modules.Tasks.Application;
using Strategy.Modules.Tasks.Domain;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.Tasks.Demo;

/// <summary>
/// Demo pack (ADR 0153): five manager-assignment tasks given by the demo Director General and the Strategy Director to their staff (one
/// overdue, one started, one a multi-assignee group). Removal: every open demo task is cancelled by its creator with a reason.
/// </summary>
internal sealed class TasksDemoPack : IDemoPackContributor
{
    private const string TaskType = "tasks.task";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    private sealed record Seed(string Creator, string TitleAr, string TitleEn, string Priority, int DueInDays, string[] Assignees, bool Start);

    private static readonly Seed[] Seeds =
    [
        new("director", "إعداد عرض الأداء الربعي (عرض)", "Prepare the quarterly performance briefing (demo)", "high", 10, ["strategyHead"], false),
        new("strategyHead", "تحديث بطاقات مؤشرات الإدارة (عرض)", "Update the department KPI cards (demo)", "medium", 5, ["analyst"], true),
        new("strategyHead", "مراجعة خطة المشروع قبل الاعتماد (عرض)", "Review the project plan before approval (demo)", "high", -3, ["pm"], false),
        new("director", "تقديم ملاحظات على مسودة الميزانية (عرض)", "Comment on the draft budget (demo)", "low", 15, ["financeHead", "accountant"], false),
        new("auditHead", "تجهيز ملف متابعة الملاحظات (عرض)", "Prepare the findings follow-up file (demo)", "medium", 7, ["auditor"], true),
    ];

    public string Code => "tasks";

    public int Order => 10;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var seed in Seeds)
        {
            await context.ActAsAsync(seed.Creator).ConfigureAwait(false);
            var created = (await context.SendAsync(new CreateTask(seed.TitleAr, seed.TitleEn, null, "manager-assignment", "manager-assignment", null, seed.Priority,
                false, context.Today.AddDays(Math.Min(0, seed.DueInDays) - 5), context.Today.AddDays(seed.DueInDays), 8m,
                seed.Assignees.Select(a => context.Person(a).EmployeeId).ToList()), cancellationToken).ConfigureAwait(false)).Require("task " + seed.TitleEn);
            foreach (var task in created.Tasks)
            {
                await context.TrackAsync(TaskType, task.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (seed.Start)
            {
                var task = created.Tasks[0];
                await context.ActAsAsync(seed.Assignees[0]).ConfigureAwait(false);
                var accepted = (await context.SendAsync(new ActOnTask(task.Id, TaskActions.Accept, task.Version), cancellationToken).ConfigureAwait(false))
                    .Require("accept task");
                if (accepted.Status != TaskStatuses.InProgress)
                {
                    (await context.SendAsync(new ActOnTask(task.Id, TaskActions.Start, accepted.Version), cancellationToken).ConfigureAwait(false)).Require("start task");
                }
            }
        }
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var record in await context.TrackedAsync(TaskType, cancellationToken).ConfigureAwait(false))
        {
            await context.ActAsAsync(record).ConfigureAwait(false);
            var task = await context.SendAsync(new GetTask(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (task.IsFailure || TaskStatuses.Closed.Contains(task.Value.Status))
            {
                continue;
            }

            (await context.SendAsync(new ActOnTask(record.ResourceId, TaskActions.Cancel, task.Value.Version, Reason), cancellationToken).ConfigureAwait(false))
                .Require("cancel task");
        }
    }
}
