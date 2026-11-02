using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Workflow;

/// <summary>Each approver rule kind resolves against the real Organisation and Access data (design note §4, ADR 0025).</summary>
public sealed class ApproverRuleTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private async Task<Resolution> ResolveAsync(ApproverRule rule, ApprovalWorld world, Guid? resourceId = null, Guid? unitId = null)
    {
        await using var scope = TestAccess.SystemScope(api.Factory);
        var resolver = scope.ServiceProvider.GetRequiredService<ApproverResolver>();
        var subject = new ApprovalSubject("sample.item", resourceId ?? Guid.CreateVersion7(), null, null, null, world.Submitter.EmployeeId, world.Submitter.Id,
            unitId ?? world.SectionId);
        return await resolver.ResolveAsync(rule, new ResolutionContext(SampleApprovalProcess.Definition, subject, world.Submitter.EmployeeId, new ApprovalSettings()),
            CancellationToken.None);
    }

    private static Guid[] Users(Resolution resolution) => [.. resolution.Approvers.Select(a => a.UserId).Order()];

    [Fact]
    public async Task Section_manager_and_department_director_follow_the_configured_levels()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.SectionManager), world)).ShouldBe([world.SectionManager.Id]);
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.DepartmentDirector), world)).ShouldBe([world.Director.Id]);

        // Record in the department itself: no section on the path.
        var none = await ResolveAsync(new ApproverRule(ApproverRuleKinds.SectionManager), world, unitId: world.DepartmentId);
        none.Approvers.ShouldBeEmpty();
        none.Notes.ShouldContain("workflow.preview.noUnitAtLevel");
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.DepartmentDirector), world, unitId: world.DepartmentId)).ShouldBe([world.Director.Id]);

        // "Of submitter": the submitter's unit even when the record sits elsewhere.
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.SectionManager, Of: ApproverRuleSubjects.Submitter), world, unitId: world.DepartmentId))
            .ShouldBe([world.SectionManager.Id]);

        // A section without a head says so.
        await ApprovalWorld.SetHeadAsync(api.Factory, world.SectionId, null);
        (await ResolveAsync(new ApproverRule(ApproverRuleKinds.SectionManager), world)).Notes.ShouldContain("workflow.preview.noHead");
    }

    [Fact]
    public async Task Head_of_unit_specific_employee_and_submitter_manager()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.HeadOfUnit, UnitId: world.DepartmentId), world)).ShouldBe([world.Director.Id]);
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.HeadOfUnit, Of: ApproverRuleSubjects.Record), world)).ShouldBe([world.SectionManager.Id]);
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.Employee, EmployeeId: world.Director.EmployeeId), world)).ShouldBe([world.Director.Id]);
        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.SubmitterManager), world)).ShouldBe([world.SectionManager.Id]);

        var resolved = (await ResolveAsync(new ApproverRule(ApproverRuleKinds.Employee, EmployeeId: world.Director.EmployeeId), world)).Approvers.Single();
        resolved.EmployeeId.ShouldBe(world.Director.EmployeeId);
        resolved.NameEn.ShouldStartWith("Test employee");
        resolved.NameAr.ShouldBe("موظف اختبار");
    }

    [Fact]
    public async Task Role_resolves_direct_holders_and_the_people_of_assigned_units_but_never_everyone()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var roleKey = await api.Factory.CreateRoleAsync("sample", (SamplePermissions.Read, DataScope.All));
        var direct = await api.Factory.CreateUserAsync();
        await api.Factory.GrantRoleAsync(direct.Id, roleKey);
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            // Unit assignment on the department: its people and those of units below (the section) hold the role.
            (await AccessGrants.AssignRoleAsync(scope.ServiceProvider, roleKey, AccessGrants.Unit, world.DepartmentId)).ShouldBeTrue();
            (await AccessGrants.AssignRoleAsync(scope.ServiceProvider, roleKey, AccessGrants.Everyone, null)).ShouldBeTrue();
        }

        var resolution = await ResolveAsync(new ApproverRule(ApproverRuleKinds.Role, RoleKey: roleKey), world);
        Users(resolution).ShouldBe(new[] { direct.Id, world.Director.Id, world.SectionManager.Id, world.Submitter.Id }.Order().ToArray());
        resolution.Notes.ShouldContain("workflow.preview.everyoneIgnored");

        var unknown = await ResolveAsync(new ApproverRule(ApproverRuleKinds.Role, RoleKey: "sample.nope"), world);
        unknown.Approvers.ShouldBeEmpty();
        unknown.Notes.ShouldContain("workflow.preview.unknownRole");
    }

    [Fact]
    public async Task Owning_unit_of_the_system_resolves_to_its_head()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var admin = await api.AdminClientAsync();
        var systems = (await admin.GetFromJsonAsync<List<SystemDto>>("/api/v1/access/systems"))!;
        var sample = systems.Single(s => s.Code == "sample");
        (await admin.PutAsJsonAsync("/api/v1/access/systems/sample", new { enabled = true, owningUnitId = world.DepartmentId, version = sample.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.OwningUnit), world)).ShouldBe([world.Director.Id]);
        (await ResolveAsync(new ApproverRule(ApproverRuleKinds.OwningUnit, SystemCode: "settings"), world)).Notes.ShouldContain("workflow.preview.noOwningUnit");
    }

    [Fact]
    public async Task Record_role_resolves_the_holders_on_that_record_and_team_role_waits_for_the_teams_module()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        var item = await world.CreateItemAsync();
        (await world.SubmitterClient.PostAsJsonAsync($"{ApprovalWorld.Items}/{item.Id}/editors", new { employeeId = world.Director.EmployeeId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        Users(await ResolveAsync(new ApproverRule(ApproverRuleKinds.RecordRole, RoleCode: SamplePermissions.EditorRole), world, item.Id))
            .ShouldBe([world.Director.Id]);
        (await ResolveAsync(new ApproverRule(ApproverRuleKinds.RecordRole, RoleCode: SamplePermissions.EditorRole), world)).Approvers.ShouldBeEmpty();

        var team = await ResolveAsync(new ApproverRule(ApproverRuleKinds.TeamRole, RoleCode: "chair"), world);
        team.Approvers.ShouldBeEmpty();
        team.Notes.ShouldContain("workflow.preview.teamRoleUnavailable");
    }

    [Fact]
    public async Task People_who_left_or_have_no_account_are_not_approvers()
    {
        var world = await ApprovalWorld.CreateAsync(api.Factory);
        Guid noAccount;
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var suffix = Guid.CreateVersion7().ToString("N")[^8..];
            noAccount = (await dispatcher.SendAsync(new CreateEmployee($"N{suffix}", "بدون حساب", "No account", $"n.{suffix}@example.test", null, null, null, null,
                world.DepartmentId, null, null))).Value.Id;
        }

        var resolution = await ResolveAsync(new ApproverRule(ApproverRuleKinds.Employee, EmployeeId: noAccount), world);
        resolution.Approvers.ShouldBeEmpty();
        resolution.Notes.ShouldContain("workflow.preview.noAccount");
    }
}
