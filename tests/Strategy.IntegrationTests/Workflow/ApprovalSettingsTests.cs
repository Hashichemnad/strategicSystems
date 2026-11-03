using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Workflow;

/// <summary>Approval Settings centre: registered processes, versioned chains, validation, live preview, level settings.</summary>
public sealed class ApprovalSettingsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Process = $"/api/v1/workflow/processes/{SampleApprovalProcess.Key}";

    [Fact]
    public async Task Lists_every_registered_process_with_its_seeded_chain()
    {
        var admin = await api.AdminClientAsync();
        var list = (await admin.GetFromJsonAsync<List<ProcessSummaryDto>>("/api/v1/workflow/processes"))!;
        var sample = list.Single(p => p.Key == SampleApprovalProcess.Key);
        sample.SystemNameEn.ShouldBe("Sample system");
        sample.NameAr.ShouldBe("اعتماد البند التجريبي");
        sample.VersionNo.ShouldBeGreaterThanOrEqualTo(1);

        var detail = (await admin.GetFromJsonAsync<ProcessDetailDto>(Process))!;
        detail.Variables.Select(v => v.Name).ShouldContain("amount");
        detail.Variables.Single(v => v.Name == "sectionManager").BuiltIn.ShouldBeTrue();
        detail.Roles.ShouldContain(r => r.Code == "sample.approver");
        detail.RecordRoles.Select(r => r.Code).ShouldBe(["editor", "approver"], ignoreOrder: true);
        detail.Templates.ShouldContain(t => t.Code == "workflow.taskAssigned");
        detail.Functions.ShouldContain("daysBetween");

        var version1 = (await admin.GetFromJsonAsync<ProcessDetailDto>($"{Process}/versions/1"))!;
        version1.Steps.Select(s => s.Code).ShouldBe(["section-manager", "department-director", "item-approver"]);
        version1.Steps[0].Condition.ShouldBe(SampleApprovalProcess.SectionManagerCondition);
        (await admin.GetAsync($"{Process}/versions/999")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/v1/workflow/processes/nope.process")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refuses_an_invalid_chain_with_field_errors()
    {
        var admin = await api.AdminClientAsync();
        var current = (await admin.GetFromJsonAsync<ProcessDetailDto>(Process))!;
        ApprovalStepDefinition[] steps =
        [
            new("first", "أولى", "First", new ApproverRule(ApproverRuleKinds.Role, RoleKey: "sample.unknown")),
            new("first", null, null, new ApproverRule(ApproverRuleKinds.DepartmentDirector), "amount >", Actions: ["reject"]),
            new("third", "ثالثة", "Third", new ApproverRule(ApproverRuleKinds.Employee), SlaHours: 0,
                Escalation: new ApproverRule(ApproverRuleKinds.SubmitterManager)),
        ];
        var response = await admin.PutAsJsonAsync(Process, new SaveProcessInput(steps, null, current.VersionNo));
        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");
        var errors = problem.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("field").GetString(), e.GetProperty("messageKey").GetString())).ToList();
        errors.ShouldContain(("steps[0].rule.roleKey", "validation.unknownReference"));
        errors.ShouldContain(("steps[1].code", "workflow.errors.duplicateStepCode"));
        errors.ShouldContain(("steps[1].nameEn", "validation.atLeastOneLanguage"));
        errors.ShouldContain(("steps[1].condition", "expressions.errors.unexpectedEnd"));
        errors.ShouldContain(("steps[1].actions", "workflow.errors.approveRequired"));
        errors.ShouldContain(("steps[2].rule.employeeId", "validation.unknownReference"));
        errors.ShouldContain(("steps[2].slaHours", "validation.inclusiveBetween"));
        var condition = problem.GetProperty("errors").EnumerateArray().First(e => e.GetProperty("field").GetString() == "steps[1].condition");
        condition.GetProperty("params").GetProperty("position").GetInt32().ShouldBe(8);

        (await admin.GetFromJsonAsync<ProcessDetailDto>(Process))!.VersionNo.ShouldBe(current.VersionNo);
    }

    [Fact]
    public async Task Admin_builds_a_three_step_chain_with_a_conditional_step_and_a_record_follows_it()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var admin = await api.AdminClientAsync();
        var current = (await admin.GetFromJsonAsync<ProcessDetailDto>(Process))!;
        ApprovalStepDefinition[] chain =
        [
            new("manager", "المدير المباشر", "Line manager", new ApproverRule(ApproverRuleKinds.SubmitterManager)),
            new("director", "مدير الإدارة", "Director", new ApproverRule(ApproverRuleKinds.HeadOfUnit, UnitId: world.DepartmentId), "amount >= 1000",
                CustomActions: [new("approve-conditions", "موافقة مشروطة", "Approve with conditions", ApprovalActions.Approve)], CommentOnApprove: false),
            new("finance", "المالية", "Finance", new ApproverRule(ApproverRuleKinds.Employee, EmployeeId: world.Director.EmployeeId), ExcludeSubmitter: false),
        ];

        // Stale base version → 409, nothing saved.
        (await admin.PutAsJsonAsync(Process, new SaveProcessInput(chain, "stale", current.VersionNo - 1))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var saved = await admin.PutAsJsonAsync(Process, new SaveProcessInput(chain, "three steps", current.VersionNo));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var detail = (await saved.Content.ReadFromJsonAsync<ProcessDetailDto>())!;
        detail.VersionNo.ShouldBe(current.VersionNo + 1);
        detail.Versions[0].Note.ShouldBe("three steps");
        detail.Versions[0].CreatedByName.ShouldNotBeNull();

        // Small amount: the conditional director step is skipped.
        var small = await world.CreateItemAsync(500m);
        await world.SubmitOkAsync(small);
        var task = await ApprovalWorld.MyTaskAsync(world.SectionManagerClient, small.Id);
        await ApprovalWorld.ActOkAsync(world.SectionManagerClient, task.TaskId, "approve");
        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, small.Id);
        ApprovalWorld.CurrentStep(panel).ShouldBe("finance");
        panel.Instances[0].Steps.Single(s => s.Code == "director").State.ShouldBe(StepStates.Skipped);

        // Large amount: all three steps; return at step 2 (custom action list offered), resubmit, approve through.
        var large = await world.CreateItemAsync(2500m);
        await world.SubmitOkAsync(large);
        await ApprovalWorld.ActOkAsync(world.SectionManagerClient, (await ApprovalWorld.MyTaskAsync(world.SectionManagerClient, large.Id)).TaskId, "approve");
        var director = await ApprovalWorld.MyTaskAsync(world.DirectorClient, large.Id);
        director.Actions.Select(a => a.Code).ShouldBe(["approve", "reject", "return", "approve-conditions"]);
        await ApprovalWorld.ActOkAsync(world.DirectorClient, director.TaskId, "return", "split the amount");
        var returned = await world.ItemAsync(large.Id);
        await world.SubmitOkAsync(returned, "split");
        await ApprovalWorld.ActOkAsync(world.SectionManagerClient, (await ApprovalWorld.MyTaskAsync(world.SectionManagerClient, large.Id)).TaskId, "approve");
        await ApprovalWorld.ActOkAsync(world.DirectorClient, (await ApprovalWorld.MyTaskAsync(world.DirectorClient, large.Id)).TaskId, "approve-conditions");
        var last = await ApprovalWorld.ActOkAsync(world.DirectorClient, (await ApprovalWorld.MyTaskAsync(world.DirectorClient, large.Id)).TaskId, "approve");
        last.InstanceStatus.ShouldBe(ApprovalInstanceStatuses.Approved);
        panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, large.Id);
        panel.Instances.Count.ShouldBe(2);
        panel.Instances[0].Steps.Select(s => s.State).ShouldAllBe(s => s == StepStates.Done);
        panel.Instances[0].Steps[1].Outcome.ShouldBe("approve-conditions");
        panel.Instances[0].History.Single(h => h.Event == "approved" && h.StepNameEn == "Director").Data!.Value.GetProperty("customAction").GetString()
            .ShouldBe("approve-conditions");
    }

    [Fact]
    public async Task An_approval_in_progress_keeps_the_version_it_started_with()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var admin = await api.AdminClientAsync();
        await ApprovalWorld.SaveChainAsync(admin,
            new ApprovalStepDefinition("one", "واحد", "One", new ApproverRule(ApproverRuleKinds.SubmitterManager)),
            new ApprovalStepDefinition("two", "اثنان", "Two", new ApproverRule(ApproverRuleKinds.DepartmentDirector)));
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);

        // The chain changes to a single step while the item waits at step one.
        await ApprovalWorld.SaveChainAsync(admin, new ApprovalStepDefinition("only", "وحيد", "Only", new ApproverRule(ApproverRuleKinds.DepartmentDirector)));
        await ApprovalWorld.ActOkAsync(world.SectionManagerClient, (await ApprovalWorld.MyTaskAsync(world.SectionManagerClient, item.Id)).TaskId, "approve");
        var panel = await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id);
        ApprovalWorld.CurrentStep(panel).ShouldBe("two");

        // A new submission uses the new chain.
        var next = await world.CreateItemAsync();
        await world.SubmitOkAsync(next);
        ApprovalWorld.CurrentStep(await ApprovalWorld.PanelAsync(world.SubmitterClient, next.Id)).ShouldBe("only");
    }

    [Fact]
    public async Task Preview_shows_who_would_approve_a_sample_record_without_saving()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var admin = await api.AdminClientAsync();
        var before = (await admin.GetFromJsonAsync<ProcessDetailDto>(Process))!.VersionNo;
        ApprovalStepDefinition[] steps =
        [
            new("section-manager", "رئيس القسم", "Section manager", new ApproverRule(ApproverRuleKinds.SectionManager), SampleApprovalProcess.SectionManagerCondition),
            new("director", "مدير", "Director", new ApproverRule(ApproverRuleKinds.DepartmentDirector)),
            new("big", "كبير", "Big", new ApproverRule(ApproverRuleKinds.Role, RoleKey: "sample.approver"), "amount > 100000"),
            new("nobody", "لا أحد", "Nobody", new ApproverRule(ApproverRuleKinds.TeamRole, RoleCode: "chair"), OnNoApprover: NoApproverBehaviours.Skip),
        ];
        var response = await admin.PostAsJsonAsync($"{Process}/preview",
            new { steps, submitterEmployeeId = world.Submitter.EmployeeId, variables = new Dictionary<string, object> { ["amount"] = 5000 } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var preview = (await response.Content.ReadFromJsonAsync<PreviewDto>())!;
        preview.Steps.Select(s => s.Outcome).ShouldBe([PreviewOutcomes.Included, PreviewOutcomes.Included, PreviewOutcomes.Skipped, PreviewOutcomes.NoApprover]);
        preview.Steps[0].ConditionResult.ShouldBe(true);
        preview.Steps[0].Approvers.Single().UserId.ShouldBe(world.SectionManager.Id);
        preview.Steps[1].Approvers.Single().UserId.ShouldBe(world.Director.Id);
        preview.Steps[3].Notes.ShouldContain("workflow.preview.teamRoleUnavailable");
        preview.Facts.Single(f => f.Name == "sectionManager").Value.ShouldBe(world.SectionManager.EmployeeId!.Value.ToString("D"));
        preview.Facts.Single(f => f.Name == "sectionManager").DisplayEn.ShouldStartWith("Test employee");
        preview.Facts.Single(f => f.Name == "unitLevel").Value.ShouldBe("section");
        preview.Result.ShouldBe(PreviewOutcomes.Included);

        // The section manager as submitter: Q-P4 skips their step; a large amount brings in the role step.
        var asManager = await admin.PostAsJsonAsync($"{Process}/preview",
            new { steps, submitterEmployeeId = world.SectionManager.EmployeeId, unitId = world.SectionId, variables = new { amount = "250000" } });
        var managerPreview = (await asManager.Content.ReadFromJsonAsync<PreviewDto>())!;
        managerPreview.Steps[0].Outcome.ShouldBe(PreviewOutcomes.Skipped);
        managerPreview.Steps[2].ConditionResult.ShouldBe(true);

        (await admin.GetFromJsonAsync<ProcessDetailDto>(Process))!.VersionNo.ShouldBe(before);
    }

    [Fact]
    public async Task Validates_a_condition_while_typing()
    {
        var admin = await api.AdminClientAsync();
        var ok = (await (await admin.PostAsJsonAsync($"{Process}/validate-condition", new { condition = "amount > 10 and unitLevel == 'section'" }))
            .Content.ReadFromJsonAsync<ConditionCheckDto>())!;
        ok.Valid.ShouldBeTrue();
        ok.Variables.ShouldBe(["amount", "unitLevel"]);

        var bad = (await (await admin.PostAsJsonAsync($"{Process}/validate-condition", new { condition = "salary > 10" }))
            .Content.ReadFromJsonAsync<JsonElement>());
        bad.GetProperty("valid").GetBoolean().ShouldBeFalse();
        var error = bad.GetProperty("errors")[0];
        error.GetProperty("messageKey").GetString().ShouldBe("expressions.errors.unknownVariable");
        error.GetProperty("params").GetProperty("name").GetString().ShouldBe("salary");
    }

    [Fact]
    public async Task Level_settings_drive_the_org_relation_rules()
    {
        var admin = await api.AdminClientAsync();
        var settings = (await admin.GetFromJsonAsync<ApprovalSettingsDto>("/api/v1/workflow/settings"))!;
        settings.Value.SectionLevelCode.ShouldBe("section");
        settings.Levels.Select(l => l.Code).ShouldContain("department");

        var bad = await admin.PutAsJsonAsync("/api/v1/workflow/settings", new { sectionLevelCode = "nope", departmentLevelCode = "department", version = settings.Version });
        await bad.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed");

        var saved = await admin.PutAsJsonAsync("/api/v1/workflow/settings", new { sectionLevelCode = "unit", departmentLevelCode = "department", version = settings.Version });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reverted = await admin.PutAsJsonAsync("/api/v1/workflow/settings",
            new { sectionLevelCode = "section", departmentLevelCode = "department", version = (await saved.Content.ReadFromJsonAsync<ApprovalSettingsDto>())!.Version });
        reverted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_approval_administrators_may_change_processes()
    {
        var viewer = await api.Factory.CreateSignedInClientAsync(roles: "workflow.viewer");
        (await viewer.GetAsync("/api/v1/workflow/processes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = (await viewer.GetFromJsonAsync<ProcessDetailDto>(Process))!;
        await (await viewer.PutAsJsonAsync(Process, new SaveProcessInput(current.Steps, null, current.VersionNo)))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        var user = await api.UserClientAsync();
        (await user.GetAsync("/api/v1/workflow/processes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
