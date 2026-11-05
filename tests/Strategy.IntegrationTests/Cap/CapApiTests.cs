using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Risk;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Cap.Application;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Cap.Jobs;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Cap;

/// <summary>One host per test class with a travelling clock and a capturing PDF renderer; CAP, KPI, Risk and Portfolio open for everyone.</summary>
public sealed class CapFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public Strategy.IntegrationTests.Teams.CapturingPdfRenderer Pdf { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer>(Pdf);
        });
        foreach (var system in new[] { CapPermissions.SystemCode, KpiPermissions.SystemCode, RiskPermissions.SystemCode, "portfolio" })
        {
            await Factory.GrantSystemAccessAsync(system, AccessGrants.Everyone, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>
/// Department A with section A1 (head = section manager), department B. The PM Office (MFA), the section manager (default remark owner), an
/// employee of A1 (action assignee), a department manager of A (role on the unit), an employee of B (outsider of A).
/// </summary>
internal sealed class CapWorld
{
    public const string Base = "/api/v1/cap";

    public required ApiFactory Factory { get; init; }

    public required Guid DepartmentA { get; init; }

    public required Guid SectionA1 { get; init; }

    public required Guid DepartmentB { get; init; }

    public required TestUser Pmo { get; init; }

    public required TestUser SectionManager { get; init; }

    public required TestUser Assignee { get; init; }

    public required TestUser DepartmentManager { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static async Task<CapWorld> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^6..].ToUpperInvariant();
        Guid organisation, a, a1, b;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"CO{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            a = (await dispatcher.SendAsync(new CreateUnit($"CA{suffix}", "إدارة أ", $"Department A {suffix}", "department", organisation, null, null, null, null))).Value.Id;
            a1 = (await dispatcher.SendAsync(new CreateUnit($"CS{suffix}", "قسم أ١", $"Section A1 {suffix}", "section", a, null, null, null, null))).Value.Id;
            b = (await dispatcher.SendAsync(new CreateUnit($"CB{suffix}", "إدارة ب", $"Department B {suffix}", "department", organisation, null, null, null, null))).Value.Id;
        }

        var pmo = await factory.CreateUserAsync(withMfa: true, unitId: organisation, roles: CapPermissions.OfficeRoleKey);
        var manager = await factory.CreateUserAsync(unitId: a1);
        var assignee = await factory.CreateUserAsync(unitId: a1);
        var departmentManager = await factory.CreateUserAsync(unitId: a, roles: "cap.department-manager");
        var outsider = await factory.CreateUserAsync(unitId: b);
        await ApprovalWorld.SetHeadAsync(factory, a1, manager.EmployeeId);
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { pmo, manager, assignee, departmentManager, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new CapWorld
        {
            Factory = factory,
            DepartmentA = a,
            SectionA1 = a1,
            DepartmentB = b,
            Pmo = pmo,
            SectionManager = manager,
            Assignee = assignee,
            DepartmentManager = departmentManager,
            Outsider = outsider,
            Clients = clients,
        };
    }

    public static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<CapSourceDto> SourceAsync(string type = CapSourceTypes.Workshop, string title = "Excellence workshop") =>
        await OkAsync<CapSourceDto>(C(Pmo).PostAsJsonAsync($"{Base}/sources", new
        {
            titleAr = "ورشة " + title,
            titleEn = title,
            sourceTypeCode = type,
            description = "Findings of the workshop",
            receivedOn = new DateOnly(2026, 9, 1),
            ownerEmployeeId = Pmo.EmployeeId,
        }));

    public async Task<CapRemarkDto> RemarkAsync(
        CapSourceDto source, string text, string priority = CapPriorities.Medium, Guid? department = null, Guid? section = null, Guid? owner = null) =>
        await OkAsync<CapRemarkDto>(C(Pmo).PostAsJsonAsync($"{Base}/sources/{source.Id}/remarks", new
        {
            textAr = "ملاحظة " + text,
            textEn = text,
            typeCode = CapRemarkTypes.Improvement,
            priority,
            departmentUnitId = department ?? DepartmentA,
            sectionUnitId = section ?? SectionA1,
            ownerEmployeeId = owner,
        }));

    public async Task<CapRemarkDto> GetAsync(Guid remarkId, TestUser? user = null) => await OkAsync<CapRemarkDto>(C(user ?? Pmo).GetAsync($"{Base}/remarks/{remarkId}"));

    public async Task<CapActionDto> ActionAsync(Guid actionId, TestUser? user = null) => await OkAsync<CapActionDto>(C(user ?? Pmo).GetAsync($"{Base}/actions/{actionId}"));

    public object CapActionValues(string details, bool budgetRequired = false, bool? budgetAvailable = null, decimal? cost = null, DateOnly? end = null) => new
    {
        detailsAr = "إجراء " + details,
        detailsEn = details + " — publish the revised procedure and train the section staff",
        typeCode = CapActionTypes.Corrective,
        budgetRequired,
        budgetAvailable,
        estimatedCost = cost,
        relatedProcess = "Service delivery",
        assignedEmployeeId = Assignee.EmployeeId,
        plannedStart = new DateOnly(2026, 9, 15),
        plannedEnd = end ?? new DateOnly(2026, 11, 30),
    };

    public async Task<CapActionDto> AddActionAsync(Guid remarkId, object values) =>
        await OkAsync<CapActionDto>(C(SectionManager).PostAsJsonAsync($"{Base}/remarks/{remarkId}/actions", values));

    public async Task<CapRemarkDto> AcceptAndPlanAsync(CapRemarkDto remark, params object[] actions)
    {
        await OkAsync<CapRemarkDto>(C(SectionManager).PostAsync($"{Base}/remarks/{remark.Id}/accept", null));
        foreach (var a in actions)
        {
            await AddActionAsync(remark.Id, a);
        }

        return await OkAsync<CapRemarkDto>(C(SectionManager).PostAsJsonAsync($"{Base}/remarks/{remark.Id}/submit", new { comment = "Actions ready" }));
    }

    public async Task<CapActionDto> ValidateAsync(Guid actionId, string decision = "approve", string? comment = null, Guid? projectId = null) =>
        await OkAsync<CapActionDto>(C(Pmo).PostAsJsonAsync($"{Base}/actions/{actionId}/validate", new { decision, comment, projectId }));

    public async Task UploadEvidenceAsync(Guid actionId, TestUser user)
    {
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { file, "file", "evidence.pdf" } };
        var response = await C(user).PostAsync($"/api/v1/files/owners/{CapAction.EntityType}/{actionId}", form);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The PM Office decides the pending process of the action through the generic task screen.</summary>
    public async Task DecideAsync(Guid actionId, string process, string action = "approve", string? comment = null)
    {
        var panel = await OkAsync<SubjectApprovalsDto>(C(Pmo).GetAsync($"/api/v1/workflow/subjects/{CapAction.EntityType}/{actionId}"));
        var instance = panel.Instances.First(i => i.ProcessKey == process && i.Status == ApprovalInstanceStatuses.InProgress);
        instance.MyTask.ShouldNotBeNull();
        await ApprovalWorld.ActOkAsync(C(Pmo), instance.MyTask!.TaskId, action, comment);
    }

    public async Task<CapSettings> SettingsAsync(Func<CapSettings, CapSettings> change)
    {
        var current = await OkAsync<SettingsDocument<CapSettings>>(C(Pmo).GetAsync($"{Base}/settings"));
        var saved = await OkAsync<SettingsDocument<CapSettings>>(C(Pmo).PutAsJsonAsync($"{Base}/settings", new { settings = change(current.Value), version = current.Version }));
        return saved.Value;
    }
}

/// <summary>CAP end to end (docs/design/cap.md §15) on a real database.</summary>
public sealed class CapApiTests(CapFixture fx) : IClassFixture<CapFixture>
{
    private const string Base = CapWorld.Base;

    [Fact]
    public async Task Workshop_remarks_are_accepted_validated_updated_monthly_extended_and_completed_with_evidence()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        DateOnly today;
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            today = await scope.ServiceProvider.GetRequiredService<CapDeadlines>().TodayAsync(CancellationToken.None);
        }

        await w.SettingsAsync(s => s with
        {
            RejectionNeedsPmoConfirmation = false,
            EffectivenessVerification = false,
            MonthlyReminderDay = Math.Min(today.Day, 28),
            EscalatePmoWorkingDays = 0,
        });
        var source = await w.SourceAsync();
        source.Number.ShouldStartWith($"CAPS-{today.Year}-");
        var remark = await w.RemarkAsync(source, "Service charter is not published on the website", CapPriorities.High);

        // Number from the department code, owner = the section manager (head of the section), due date by priority (+60 days, working day).
        remark.Number.ShouldStartWith("CAP-CA");
        remark.Number.ShouldContain($"-{today.Year}-");
        remark.Owner!.Id.ShouldBe(w.SectionManager.EmployeeId!.Value);
        await using (var scope = TestAccess.SystemScope(fx.Factory))
        {
            var (expected, _) = await scope.ServiceProvider.GetRequiredService<CapDeadlines>().DueAsync(w.SectionA1, today, 60, false, CancellationToken.None);
            remark.DueOn.ShouldBe(expected);
        }

        remark.DueOn.DayNumber.ShouldBeGreaterThanOrEqualTo(today.DayNumber + 60);
        remark.Status.ShouldBe(CapRemarkStatuses.Assigned);
        remark.Stage.ShouldBe(RemarkStages.Analysis);
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.SectionManager), "cap.remarkAssigned")).Link.ShouldBe($"/cap/remarks/{remark.Id}");
        var status = await CapWorld.OkAsync<CurrentStatus>(w.C(w.SectionManager).GetAsync($"/api/v1/workflow/status/{Remark.EntityType}/{remark.Id}"));
        status.MessageKey.ShouldBe("cap.currentStatus.waitingAccept");
        status.NextActions.Select(a => a.Code).ShouldBe(["accept", "reject"]);

        // The assignee cannot respond; the owner accepts. High priority needs the root cause before submitting.
        (await w.C(w.Assignee).PostAsync($"{Base}/remarks/{remark.Id}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsync($"{Base}/remarks/{remark.Id}/accept", null));
        var end = today.AddDays(-1);
        var action = await w.AddActionAsync(remark.Id, w.CapActionValues("Publish the service charter", end: end));
        action.Number.ShouldBe($"{remark.Number}-1");
        action.ValidationStatus.ShouldBe(ValidationStatuses.Draft);
        action.QualityFlags.ShouldContain(ActionQuality.EndInPast);
        await (await w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{remark.Id}/submit", new { comment = "x" }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "cap.rootCauseRequired");
        var current = await w.GetAsync(remark.Id, w.SectionManager);
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PutAsJsonAsync($"{Base}/remarks/{remark.Id}/root-cause", new
        {
            values = new { method = RootCauseMethods.FiveWhys, whys = new[] { "The charter was never approved", "No owner was named", "The procedure is silent" }, text = "No owner" },
            version = current.Version,
        }));
        var submitted = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{remark.Id}/submit", new { comment = "Ready" }));
        submitted.Status.ShouldBe(CapRemarkStatuses.Validation);
        submitted.RootCause.Whys.Count.ShouldBe(3);

        // The PM Office returns, the owner corrects and resubmits, the PM Office approves.
        var view = await CapWorld.OkAsync<CapValidationViewDto>(w.C(w.Pmo).GetAsync($"{Base}/remarks/{remark.Id}/validation"));
        view.Actions.Single().Can.Validate.ShouldBeTrue();
        (await w.C(w.SectionManager).GetAsync($"{Base}/remarks/{remark.Id}/validation")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/actions/{action.Id}/validate", new { decision = "return" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await w.ValidateAsync(action.Id, "return", "Name the web page");
        (await w.GetAsync(remark.Id)).Status.ShouldBe(CapRemarkStatuses.Correction);
        var returned = await w.ActionAsync(action.Id, w.SectionManager);
        returned.ValidationStatus.ShouldBe(ValidationStatuses.Returned);
        await CapWorld.OkAsync<CapActionDto>(w.C(w.SectionManager).PutAsJsonAsync($"{Base}/actions/{action.Id}",
            new { values = w.CapActionValues("Publish the service charter on the Services page", end: end), version = returned.Version }));
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{remark.Id}/submit", new { comment = "Corrected" }));
        var approved = await w.ValidateAsync(action.Id);
        approved.ValidationStatus.ShouldBe(ValidationStatuses.Approved);
        approved.Status.ShouldBe(ActionStatuses.Late, "approved with an end date already past");
        (await w.GetAsync(remark.Id)).Status.ShouldBe(CapRemarkStatuses.Implementation);
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Assignee), "cap.actionAssigned")).Link.ShouldBe($"/cap/actions/{action.Id}");

        // The monitor: monthly reminder on the configured day (once), the action is past its end → late, owner told, PM Office escalated.
        var run = await RunMonitorAsync();
        run.MonthlyReminders.ShouldBeGreaterThanOrEqualTo(1);
        run.LateNotices.ShouldBeGreaterThanOrEqualTo(1);
        run.Escalations.ShouldBeGreaterThanOrEqualTo(1);
        var again = await RunMonitorAsync();
        again.MonthlyReminders.ShouldBe(0);
        again.LateNotices.ShouldBe(0);
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Assignee), "cap.monthlyReminder")).Link.ShouldBe("/cap/my-actions");
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.SectionManager), "cap.actionLate")).Link.ShouldBe($"/cap/actions/{action.Id}");
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Pmo), "cap.actionEscalated")).Link.ShouldBe($"/cap/actions/{action.Id}");
        var late = await w.ActionAsync(action.Id, w.Assignee);
        late.Status.ShouldBe(ActionStatuses.Late);

        // Achievement updates are kept as history; an outsider cannot update.
        var progressed = await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress",
            new { pct = 0.4m, note = "Draft charter written", actualStart = today.AddDays(-10), version = late.Version }));
        progressed.Status.ShouldBe(ActionStatuses.Late, "still past its end");
        (await w.C(w.Outsider).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress", new { pct = 0.5m, version = progressed.Version })).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Extension: not later → refused; approved by the PM Office → revised end, original kept, late cleared.
        (await w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/extensions", new { requestedEnd = end.AddDays(-5), justification = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var newEnd = today.AddDays(30);
        await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/extensions",
            new { requestedEnd = newEnd, justification = "Waiting for the web team" }));
        await w.DecideAsync(action.Id, CapProcesses.Extension);
        var extended = await w.ActionAsync(action.Id, w.Assignee);
        extended.RevisedEnd.ShouldBe(newEnd);
        extended.Values.PlannedEnd.ShouldBe(end, "the original planned end is kept");
        extended.Status.ShouldBe(ActionStatuses.InProgress);
        (await CapWorld.OkAsync<List<CapExtensionDto>>(w.C(w.Assignee).GetAsync($"{Base}/actions/{action.Id}/extensions"))).Single().Status.ShouldBe(ExtensionStatuses.Approved);

        // 100 % without evidence is refused; with evidence the PMO approves the completion → action and remark completed, reported back.
        await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress",
            new { pct = 1m, note = "Published", actualEnd = today, version = extended.Version }));
        await (await w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/request-completion", new { note = "Done" }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "cap.evidenceRequired");
        await w.UploadEvidenceAsync(action.Id, w.Assignee);
        (await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/request-completion", new { note = "Done" })))
            .PendingProcess.ShouldBe(CapProcesses.Completion);
        await w.DecideAsync(action.Id, CapProcesses.Completion);
        var completed = await w.ActionAsync(action.Id);
        completed.Status.ShouldBe(ActionStatuses.Completed);
        completed.EvidenceCount.ShouldBe(1);
        var done = await w.GetAsync(remark.Id);
        done.Status.ShouldBe(CapRemarkStatuses.Completed);
        done.Stage.ShouldBe(RemarkStages.Closed);
        (await CapWorld.OkAsync<List<CapAchievementUpdateDto>>(w.C(w.Pmo).GetAsync($"{Base}/actions/{action.Id}/updates"))).Count.ShouldBe(2);

        // Reports and dashboard.
        var dashboard = await CapWorld.OkAsync<CapDashboardDto>(w.C(w.Pmo).GetAsync($"{Base}/dashboard?unitId={w.DepartmentA}"));
        dashboard.CompletedRemarks.ShouldBe(1);
        dashboard.ByDepartment.Single().ClosureRate.ShouldBe(1m);
        (await w.C(w.Pmo).GetAsync($"{Base}/reports/register?format=pdf&sourceId={source.Id}&language=ar")).StatusCode.ShouldBe(HttpStatusCode.OK);
        fx.Pdf.Html.ShouldContain(h => h.Contains(action.Number, StringComparison.Ordinal));
        (await w.C(w.Pmo).GetAsync($"{Base}/reports/departmental?format=xlsx&unitId={w.DepartmentA}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await w.SettingsAsync(s => s with { MonthlyReminderDay = 1, EscalatePmoWorkingDays = 5 });
    }

    [Fact]
    public async Task Rejection_is_final_by_default_and_waits_for_the_pm_office_when_confirmation_is_on()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        await w.SettingsAsync(s => s with { RejectionNeedsPmoConfirmation = false });
        var source = await w.SourceAsync();
        var first = await w.RemarkAsync(source, "Not our process");
        (await w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{first.Id}/reject", new { reason = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rejected = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{first.Id}/reject",
            new { reason = "Belongs to another department" }));
        rejected.Status.ShouldBe(CapRemarkStatuses.Rejected);
        rejected.Stage.ShouldBe(RemarkStages.Closed);
        rejected.RejectionReason.ShouldBe("Belongs to another department");
        (await w.C(w.SectionManager).PostAsync($"{Base}/remarks/{first.Id}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/remarks/{first.Id}/confirm-rejection", new { confirm = true })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.Pmo), "cap.remarkRejected")).Link.ShouldBe($"/cap/remarks/{first.Id}");

        // Switched on: the rejection waits for the PM Office, who sends one back and confirms the other.
        await w.SettingsAsync(s => s with { RejectionNeedsPmoConfirmation = true });
        var second = await w.RemarkAsync(source, "Duplicate of an older finding");
        var pending = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{second.Id}/reject", new { reason = "Duplicate" }));
        pending.Status.ShouldBe(CapRemarkStatuses.RejectionPending);
        pending.Stage.ShouldBe(RemarkStages.Analysis);
        (await w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{second.Id}/confirm-rejection", new { confirm = true })).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Pmo).PostAsJsonAsync($"{Base}/remarks/{second.Id}/confirm-rejection", new { confirm = false })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var sentBack = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/remarks/{second.Id}/confirm-rejection",
            new { confirm = false, note = "It is a new case, please plan actions" }));
        sentBack.Status.ShouldBe(CapRemarkStatuses.Assigned);
        sentBack.PmoDecision.ShouldBe("sent-back");
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.SectionManager), "cap.remarkSentBack")).Link.ShouldBe($"/cap/remarks/{second.Id}");
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{second.Id}/reject", new { reason = "Still a duplicate" }));
        var confirmed = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/remarks/{second.Id}/confirm-rejection", new { confirm = true, note = "Agreed" }));
        confirmed.Status.ShouldBe(CapRemarkStatuses.Rejected);
        confirmed.PmoDecision.ShouldBe("confirmed");
        await w.SettingsAsync(s => s with { RejectionNeedsPmoConfirmation = false });
    }

    [Fact]
    public async Task One_action_linked_to_two_remarks_serves_both_and_completes_both()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        var source = await w.SourceAsync();
        var first = await w.RemarkAsync(source, "Complaints are not tracked", CapPriorities.Low);
        var second = await w.RemarkAsync(source, "No complaint statistics are reported", CapPriorities.Low);
        await w.AcceptAndPlanAsync(first, w.CapActionValues("Introduce a complaints register"));
        var action = (await CapWorld.OkAsync<List<CapActionDto>>(w.C(w.SectionManager).GetAsync($"{Base}/remarks/{first.Id}/actions"))).Single();
        await w.ValidateAsync(action.Id);

        // The second remark links the existing action instead of creating a duplicate; it is already validated.
        await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsync($"{Base}/remarks/{second.Id}/accept", null));
        var linkable = await CapWorld.OkAsync<List<CapActionListItemDto>>(w.C(w.SectionManager).GetAsync($"{Base}/remarks/{second.Id}/linkable-actions?search=register"));
        linkable.ShouldContain(a => a.Id == action.Id);
        var linked = await CapWorld.OkAsync<CapActionDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{second.Id}/link-action", new { actionId = action.Id }));
        linked.Remarks.Select(r => r.Id).ShouldBe([first.Id, second.Id], ignoreOrder: true);
        linked.Number.ShouldBe($"{first.Number}-1", "the action keeps its home number");
        (await CapWorld.OkAsync<List<CapActionDto>>(w.C(w.SectionManager).GetAsync($"{Base}/remarks/{second.Id}/actions"))).Single().Id.ShouldBe(action.Id);
        await (await w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{second.Id}/submit", new { }))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "cap.noActions");

        // Completing the shared action completes both remarks.
        var current = await w.ActionAsync(action.Id, w.Assignee);
        var full = await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress", new { pct = 1m, version = current.Version }));
        full.AchievedPct.ShouldBe(1m);
        (await w.ActionAsync(action.Id, w.Assignee)).AchievedPct.ShouldBe(1m);
        await w.UploadEvidenceAsync(action.Id, w.Assignee);
        await CapWorld.OkAsync<CapActionDto>(w.C(w.Assignee).PostAsJsonAsync($"{Base}/actions/{action.Id}/request-completion", new { note = "Register live" }));
        await w.DecideAsync(action.Id, CapProcesses.Completion);
        (await w.GetAsync(first.Id)).Status.ShouldBe(CapRemarkStatuses.Completed);
        // The second remark was accepted (planning) — linking the approved action and completing it moves it on as well.
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var db = scope.ServiceProvider.GetRequiredService<CapDbContext>();
        (await db.RemarkActions.CountAsync(l => l.ActionId == action.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Due_dates_follow_the_priority_in_working_time()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        await w.SettingsAsync(s => s with { CountWorkingDays = false });
        var source = await w.SourceAsync();
        fx.Clock.Frozen = new DateTimeOffset(2026, 9, 10, 6, 0, 0, TimeSpan.Zero); // Thursday 10 September 2026 in Dubai
        try
        {
            // Critical +30 = Saturday 10 October → Monday 12 October, due at the end of working time (15:30 Dubai = 11:30 UTC).
            var critical = await w.RemarkAsync(source, "Critical finding", CapPriorities.Critical);
            critical.DueOn.ShouldBe(new DateOnly(2026, 10, 12));
            critical.DueAt.ShouldBe(new DateTimeOffset(2026, 10, 12, 11, 30, 0, TimeSpan.Zero));
            (await w.RemarkAsync(source, "High finding", CapPriorities.High)).DueOn.ShouldBe(new DateOnly(2026, 11, 9));
            (await w.RemarkAsync(source, "Medium finding")).DueOn.ShouldBe(new DateOnly(2026, 12, 9));
            (await w.RemarkAsync(source, "Low finding", CapPriorities.Low)).DueOn.ShouldBe(new DateOnly(2027, 3, 9));

            // Editable in Settings: working days instead of calendar days (30 working days from Thursday 10 September = Thursday 22 October).
            await w.SettingsAsync(s => s with { CountWorkingDays = true });
            (await w.RemarkAsync(source, "Critical, working days", CapPriorities.Critical)).DueOn.ShouldBe(new DateOnly(2026, 10, 22));

            // Changing the priority before any action is approved recomputes the due date.
            var medium = await w.RemarkAsync(source, "Priority changes");
            var raised = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.Pmo).PutAsJsonAsync($"{Base}/remarks/{medium.Id}",
                new { values = medium.Values with { Priority = CapPriorities.Critical }, version = medium.Version }));
            raised.DueOn.ShouldBe(new DateOnly(2026, 10, 22));
        }
        finally
        {
            await w.SettingsAsync(s => s with { CountWorkingDays = false });
            fx.Clock.Frozen = null;
        }
    }

    [Fact]
    public async Task A_budget_needing_action_without_budget_creates_a_draft_program_request_with_source_action_plan()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        var source = await w.SourceAsync();
        var remark = await w.RemarkAsync(source, "The portal cannot accept online payments", CapPriorities.Low);
        await w.AcceptAndPlanAsync(remark,
            w.CapActionValues("Build the online payment gateway", budgetRequired: true, budgetAvailable: false, cost: 250000m),
            w.CapActionValues("Publish the payment procedure", budgetRequired: true, budgetAvailable: true, cost: 5000m));
        var actions = await CapWorld.OkAsync<List<CapActionDto>>(w.C(w.Pmo).GetAsync($"{Base}/remarks/{remark.Id}/actions"));
        var unfunded = actions.Single(a => a.Values.BudgetAvailable == false);
        var funded = actions.Single(a => a.Values.BudgetAvailable == true);
        unfunded.QualityFlags.ShouldNotContain(ActionQuality.NoCost);
        await w.ValidateAsync(funded.Id);
        (await w.ActionAsync(funded.Id)).ProgramRequestId.ShouldBeNull("budget available: no request");
        var routed = await w.ValidateAsync(unfunded.Id);
        routed.ProgramRequestId.ShouldNotBeNull();
        routed.ProgramRequestNumber.ShouldNotBeNull();
        (await w.GetAsync(remark.Id)).Status.ShouldBe(CapRemarkStatuses.Implementation);

        await using var scope = TestAccess.SystemScope(fx.Factory);
        var portfolio = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var request = await portfolio.Requests.AsNoTracking().SingleAsync(r => r.Id == routed.ProgramRequestId);
        request.Status.ShouldBe("draft");
        request.SuggestionSourceCode.ShouldBe("action-plan");
        request.Source.ShouldBe("action-plan");
        request.EstimatedBudget.ShouldBe(250000m);
        request.RequesterEmployeeId.ShouldBe(w.SectionManager.EmployeeId);
        request.UnitId.ShouldBe(w.DepartmentA);
        request.NameEn!.ShouldContain("online payment gateway");

        // Linked back (Links registry) and visible to the requester as their draft; the owner was told.
        var links = await scope.ServiceProvider.GetRequiredService<ILinkStore>().ListFromAsync(new LinkRef(CapAction.EntityType, unfunded.Id));
        links.ShouldContain(l => l.ToType == "portfolio.request" && l.ToId == request.Id);
        (await w.C(w.SectionManager).GetAsync($"/api/v1/portfolio/requests/{request.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApprovalFlowTests.WaitForNotificationAsync(w.C(w.SectionManager), "cap.budgetRouted")).Link.ShouldBe($"/portfolio/requests/{request.Id}");
        (await w.ActionAsync(unfunded.Id, w.SectionManager)).ProgramRequest!.Number.ShouldBe(request.Number);
    }

    [Fact]
    public async Task A_missed_kpi_target_and_a_risk_event_each_raise_a_remark_linked_to_their_source()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        await w.SettingsAsync(s => s with { KpiMissMode = RaiseModes.Manual, KpiMissConsecutive = 1, RiskEventMode = RaiseModes.Automatic });
        try
        {
            // KPI: the miss becomes a suggestion; the PMO raises it in one click → source for the card, remark for the result.
            var cardId = Guid.CreateVersion7();
            var resultId = Guid.CreateVersion7();
            await PublishAsync(new KpiTargetMissed(resultId, cardId, "KPI-77", "رضا المتعاملين", "Customer satisfaction", 2026, "q2", 61m, 80m, 0.76m, RagCodes.Red, 2,
                w.DepartmentA, w.SectionManager.EmployeeId, DateTimeOffset.UtcNow));
            var suggestion = await WaitForAsync(async () => (await CapWorld.OkAsync<PagedResult<CapSuggestionDto>>(w.C(w.Pmo).GetAsync($"{Base}/suggestions")))
                .Items.FirstOrDefault(s => s.ResourceId == resultId));
            suggestion.Kind.ShouldBe(SuggestionKinds.KpiMiss);
            suggestion.Priority.ShouldBe(CapPriorities.High, "two consecutive red periods");
            (await w.C(w.SectionManager).GetAsync($"{Base}/suggestions")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var raised = await CapWorld.OkAsync<CapSuggestionDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/suggestions/{suggestion.Id}/raise", new { }));
            raised.Status.ShouldBe(SuggestionStatuses.Raised);
            var again = await CapWorld.OkAsync<CapSuggestionDto>(w.C(w.Pmo).PostAsJsonAsync($"{Base}/suggestions/{suggestion.Id}/raise", new { }));
            again.RemarkId.ShouldBe(raised.RemarkId, "raising twice returns the same remark");
            var kpiRemark = await w.GetAsync(raised.RemarkId!.Value);
            kpiRemark.SourceTypeCode.ShouldBe(CapSourceTypes.KpiPerformance);
            kpiRemark.Values.TextEn!.ShouldContain("Customer satisfaction");
            kpiRemark.Owner!.Id.ShouldBe(w.SectionManager.EmployeeId!.Value, "no section: head of the department… or the responsible employee");
            await using (var scope = TestAccess.SystemScope(fx.Factory))
            {
                var db = scope.ServiceProvider.GetRequiredService<CapDbContext>();
                var source = await db.Sources.SingleAsync(s => s.Id == kpiRemark.SourceId);
                source.LinkedType.ShouldBe("kpi.card");
                source.LinkedId.ShouldBe(cardId);
                var entity = await db.Remarks.SingleAsync(r => r.Id == kpiRemark.Id);
                entity.LinkedType.ShouldBe("kpi.result");
                entity.LinkedId.ShouldBe(resultId);
            }

            // Risk: an approved risk logs an event → remark raised automatically, linked to the risk, and the event shows the remark.
            var r = await RiskWorld.CreateAsync(fx.Factory);
            var risk = await r.SubmitAndApproveAsync(await r.CreateAsync(RiskWorld.Values("Payment outage", r.DepartmentA, r.OwnerA, 4, 4)));
            var logged = await RiskWorld.OkAsync<JsonElement>(r.C(r.CoordinatorA).PostAsJsonAsync("/api/v1/risk/events", new
            {
                values = new { occurredOn = new DateOnly(2026, 9, 3), description = "Payment gateway down for 4 hours", nearMiss = false, financialImpact = 12000, currency = "AED", unitId = r.DepartmentA, riskIds = new[] { risk.Id } },
            }));
            var eventId = logged.GetProperty("id").GetGuid();
            var remarkId = await WaitForAsync(async () =>
            {
                await using var scope = TestAccess.SystemScope(fx.Factory);
                return await scope.ServiceProvider.GetRequiredService<IRiskDirectory>().GetEventAsync(eventId, StrategyClaims.CreateSystemPrincipal()) is { CapRemarkId: { } id }
                    ? id.ToString()
                    : null;
            });
            var riskRemark = await w.GetAsync(Guid.Parse(remarkId));
            riskRemark.SourceTypeCode.ShouldBe(CapSourceTypes.RiskEvent);
            riskRemark.Values.DepartmentUnitId.ShouldBe(r.DepartmentA);
            riskRemark.Values.Priority.ShouldBe(CapPriorities.High);
            riskRemark.SourceLinked.ShouldNotBeNull();
            riskRemark.SourceLinked!.Type.ShouldBe("risk.risk");
            riskRemark.SourceLinked.Id.ShouldBe(risk.Id);
            riskRemark.SourceLinked.Visibility.ShouldBe(LinkVisibility.Restricted, "the PM Office may not read the risk: the badge carries no content");
        }
        finally
        {
            await w.SettingsAsync(s => s with { RiskEventMode = RaiseModes.Manual });
        }
    }

    [Fact]
    public async Task Scope_rules_follow_the_remark_owner_assignee_department_manager_and_pm_office()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        var source = await w.SourceAsync();
        var a = await w.RemarkAsync(source, "Remark of department A", CapPriorities.Low);
        var b = await w.RemarkAsync(source, "Remark of department B", CapPriorities.Low, w.DepartmentB, w.DepartmentB);
        await w.AcceptAndPlanAsync(a, w.CapActionValues("Action of department A"));
        var action = (await CapWorld.OkAsync<List<CapActionDto>>(w.C(w.Pmo).GetAsync($"{Base}/remarks/{a.Id}/actions"))).Single();
        await w.ValidateAsync(action.Id);

        async Task<IReadOnlyList<Guid>> VisibleAsync(TestUser user) =>
            (await CapWorld.OkAsync<PagedResult<CapRemarkListItemDto>>(w.C(user).GetAsync($"{Base}/remarks?view=all&sourceId={source.Id}"))).Items.Select(i => i.Id).ToList();

        (await VisibleAsync(w.Pmo)).ShouldBe([a.Id, b.Id], ignoreOrder: true);
        (await VisibleAsync(w.SectionManager)).ShouldBe([a.Id], "the owner sees their remark");
        (await VisibleAsync(w.Assignee)).ShouldBe([a.Id], "the assignee sees the remark of their action (record role)");
        (await VisibleAsync(w.DepartmentManager)).ShouldBe([a.Id], "department manager: unit and below");
        (await VisibleAsync(w.Outsider)).ShouldBeEmpty();

        // Out of scope → 404 everywhere; visible but forbidden → 403.
        (await w.C(w.Outsider).GetAsync($"{Base}/remarks/{a.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/actions/{action.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"{Base}/remarks/{a.Id}/history")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await w.C(w.Outsider).GetAsync($"/api/v1/files/owners/{CapAction.EntityType}/{action.Id}")).StatusCode.ShouldBeOneOf([HttpStatusCode.NotFound, HttpStatusCode.Forbidden]);
        (await w.C(w.DepartmentManager).PostAsync($"{Base}/remarks/{a.Id}/accept", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.Assignee).PostAsJsonAsync($"{Base}/remarks/{a.Id}/actions", w.CapActionValues("Not mine"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var assigneeView = await w.ActionAsync(action.Id, w.Assignee);
        assigneeView.Can.Progress.ShouldBeTrue();
        assigneeView.Can.Edit.ShouldBeFalse();
        var managerView = await w.ActionAsync(action.Id, w.DepartmentManager);
        managerView.Can.Progress.ShouldBeFalse();
        (await w.C(w.DepartmentManager).PostAsJsonAsync($"{Base}/actions/{action.Id}/progress", new { pct = 0.2m, version = managerView.Version })).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.SectionManager).PostAsJsonAsync($"{Base}/actions/{action.Id}/validate", new { decision = "approve" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.SectionManager).GetAsync($"{Base}/dashboard")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.C(w.DepartmentManager).GetAsync($"{Base}/dashboard")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Reassignment within the unit: the owner hands the remark to the assignee (same section), not to an employee of B.
        var b2 = await w.RemarkAsync(source, "Reassigned remark", CapPriorities.Low);
        (await w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{b2.Id}/reassign", new { ownerEmployeeId = w.Outsider.EmployeeId })).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest);
        var reassigned = await CapWorld.OkAsync<CapRemarkDto>(w.C(w.SectionManager).PostAsJsonAsync($"{Base}/remarks/{b2.Id}/reassign", new { ownerEmployeeId = w.Assignee.EmployeeId }));
        reassigned.Owner!.Id.ShouldBe(w.Assignee.EmployeeId!.Value);
        (await w.GetAsync(b2.Id, w.Assignee)).Can.Respond.ShouldBeTrue();

        // My Work: remarks to accept for the owner, the PMO sees nothing pending here (all validated).
        var work = (await w.C(w.Assignee).GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.ActionItems.ShouldContain(i => i.ResourceId == b2.Id && i.MessageKey == "cap.myWork.toAccept");
    }

    [Fact]
    public async Task Mcp_tools_list_my_remarks_and_actions_and_the_write_tool_is_off_by_default()
    {
        var w = await CapWorld.CreateAsync(fx.Factory);
        var source = await w.SourceAsync();
        var remark = await w.RemarkAsync(source, "MCP remark", CapPriorities.Low);
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var tools = scope.ServiceProvider.GetServices<IAppTool>().ToDictionary(t => t.Definition.Name);
        tools["cap_my_remarks"].Definition.DefaultEnabled.ShouldBeTrue();
        tools["cap_update_achievement_draft"].Definition.DefaultEnabled.ShouldBeFalse();
        tools["cap_update_achievement_draft"].Definition.Kind.ShouldBe(ToolKind.Write);
        tools.Keys.ShouldNotContain(k => k.StartsWith("cap_", StringComparison.Ordinal) && k.Contains("approve", StringComparison.Ordinal));
        _ = remark;
    }

    private async Task<CapMonitorResult> RunMonitorAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        return await scope.ServiceProvider.GetRequiredService<CapMonitorRunner>().RunAsync(CancellationToken.None);
    }

    private async Task PublishAsync(IIntegrationEvent e)
    {
        await using var scope = TestAccess.SystemScope(fx.Factory);
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await (Task)typeof(IIntegrationEventPublisher).GetMethod(nameof(IIntegrationEventPublisher.PublishAsync))!.MakeGenericMethod(e.GetType())
            .Invoke(publisher, [e, null, CancellationToken.None])!;
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe)
        where T : class
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            await Task.Delay(250);
        }

        (await probe()).ShouldNotBeNull("the outbox should have delivered the event");
        return (await probe())!;
    }
}
