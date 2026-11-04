using System.Net.Http.Json;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Domain;

namespace Strategy.IntegrationTests.Kpi;

/// <summary>
/// Period windows with the travelling clock (SPEC K2, Appendix A Q-K2, ADR 0060): Q1 opens on 1 April for confirmed plans only, owners are notified,
/// reminded before the due date, told when overdue, the department manager is escalated to, and a late submission is accepted and flagged.
/// </summary>
public sealed class KpiWindowTests(KpiFixture fx) : IClassFixture<KpiFixture>
{
    private const string Base = KpiWorld.Base;

    private void At(int month, int day) => fx.Clock.Offset = new DateTimeOffset(2027, month, day, 6, 0, 0, TimeSpan.Zero) - DateTimeOffset.UtcNow;

    [Fact]
    public async Task Q1_opens_on_1_April_with_notification_reminder_overdue_escalation_and_a_late_flag()
    {
        var w = await KpiWorld.CreateAsync(fx.Factory);
        var confirmed = await w.CreateCardAsync(KpiWorld.Card("Window confirmed", w.OwnerA, w.DepartmentA));
        var draft = await w.CreateCardAsync(KpiWorld.Card("Window draft", w.OwnerA, w.DepartmentA));
        await KpiWorld.OkAsync<KpiYearStartedDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/years/2027/start",
            new { items = new[] { KpiWorld.Item(confirmed, 40), KpiWorld.Item(draft, 40) } }));
        var windows = (await w.C(w.Pmo).GetFromJsonAsync<List<KpiWindowDto>>($"{Base}/years/2027/windows"))!;
        windows.Single(x => x.PeriodCode == "q1").ShouldSatisfyAllConditions(x => x.OpensOn.ShouldBe(new DateOnly(2027, 4, 1)), x => x.DueOn.ShouldBe(new DateOnly(2027, 4, 15)));
        windows.Single(x => x.PeriodCode == "fy").ShouldSatisfyAllConditions(x => x.OpensOn.ShouldBe(new DateOnly(2028, 1, 1)), x => x.DueOn.ShouldBe(new DateOnly(2028, 1, 31)));
        var plan = await w.PlanAsync(confirmed.Id, 2027);
        await KpiWorld.OkAsync<int>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/plans/confirm", new { planIds = new[] { plan.Plan.Id } }));
        var q1 = plan.Periods.Single(p => p.PeriodCode == "q1");
        var draftQ1 = (await w.PlanAsync(draft.Id, 2027)).Periods.Single(p => p.PeriodCode == "q1").ResultId;
        q1.Status.ShouldBe(ResultStatuses.NotOpen);
        var dueAt = (await w.ResultAsync(q1.ResultId)).DueAt!.Value;
        dueAt.ShouldBe(new DateTimeOffset(2027, 4, 15, 15, 30, 0, TimeSpan.FromHours(4))); // end of working time, Asia/Dubai

        try
        {
            At(3, 31);
            await fx.RunWindowsAsync();
            await w.ReSignInAsync();
            (await w.ResultAsync(q1.ResultId, w.OwnerA)).Status.ShouldBe(ResultStatuses.NotOpen);

            At(4, 1);
            await fx.RunWindowsAsync();
            await w.ReSignInAsync();
            (await w.ResultAsync(q1.ResultId, w.OwnerA)).Status.ShouldBe(ResultStatuses.Open);
            (await w.ResultAsync(draftQ1, w.OwnerA)).Status.ShouldBe(ResultStatuses.NotOpen); // plan not confirmed
            (await w.ResultAsync(plan.Periods.Single(p => p.PeriodCode == "q2").ResultId, w.OwnerA)).Status.ShouldBe(ResultStatuses.NotOpen);
            var opened = await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "kpi.windowOpened");
            opened.Link.ShouldBe("/kpi/pending");
            var pending = (await w.C(w.OwnerA).GetFromJsonAsync<List<KpiResultListItemDto>>($"{Base}/results/pending"))!;
            pending.Single(p => p.Id == q1.ResultId).Overdue.ShouldBeFalse();

            await fx.RunWindowsAsync(); // idempotent: nothing more for this result
            (await w.ResultAsync(q1.ResultId, w.OwnerA)).Status.ShouldBe(ResultStatuses.Open);

            // Three working days before the due date: reminder.
            At(4, 12);
            await fx.RunWindowsAsync();
            await w.ReSignInAsync();
            await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "kpi.reminder");

            // The day after the due date: overdue notice, no escalation yet (two working days after due).
            At(4, 16);
            await fx.RunWindowsAsync();
            await w.ReSignInAsync();
            await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.OwnerA), "kpi.overdue");
            (await w.C(w.OwnerA).GetFromJsonAsync<List<KpiResultListItemDto>>($"{Base}/results/pending"))!.Single(p => p.Id == q1.ResultId).Overdue.ShouldBeTrue();
            var work = (await w.C(w.OwnerA).GetFromJsonAsync<Strategy.Platform.Workflow.Application.MyWorkDto>("/api/v1/workflow/my-work"))!;
            work.ActionItems.ShouldContain(i => i.ResourceId == q1.ResultId && i.MessageKey == "kpi.myWork.overdue");

            At(4, 20);
            await fx.RunWindowsAsync();
            await w.ReSignInAsync();
            var escalation = await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.ManagerA), "kpi.escalation");
            escalation.Link.ShouldBe($"/kpi/results/{q1.ResultId}");

            // Late submission is accepted and flagged with the days late.
            var submitted = await w.SubmitAsync(w.OwnerA, q1.ResultId, KpiWorld.Entry(10));
            (submitted.Status, submitted.Late, submitted.DaysLate).ShouldBe((ResultStatuses.PendingApproval, true, 5));
        }
        finally
        {
            fx.Clock.Offset = TimeSpan.Zero;
        }
    }
}
