using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Import;

namespace Strategy.IntegrationTests.Organisation;

public class OrganisationApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Units = "/api/v1/org/units";
    private const string Employees = "/api/v1/org/employees";

    private static string Code(string prefix) => $"{prefix}-{Guid.CreateVersion7().ToString("N")[^8..]}".ToUpperInvariant();

    private static async Task<OrgUnitDto> CreateUnitAsync(HttpClient client, string code, string level, Guid? parentId, string effectiveFrom = "2024-01-01")
    {
        var response = await client.PostAsJsonAsync(Units, new { code, nameAr = "وحدة " + code, nameEn = "Unit " + code, levelCode = level, parentId, effectiveFrom });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrgUnitDto>())!;
    }

    [Fact]
    public async Task Admin_builds_four_levels_reparents_a_unit_and_history_keeps_the_old_structure()
    {
        var admin = await api.AdminClientAsync();
        var org = await CreateUnitAsync(admin, Code("ORG"), "organisation", null);
        var sectorA = await CreateUnitAsync(admin, Code("SA"), "sector", org.Id);
        var sectorB = await CreateUnitAsync(admin, Code("SB"), "sector", org.Id);
        var department = await CreateUnitAsync(admin, Code("DEP"), "department", sectorA.Id);
        var section = await CreateUnitAsync(admin, Code("SEC"), "section", department.Id);
        section.Path.Select(p => p.Id).ShouldBe([org.Id, sectorA.Id, department.Id, section.Id]);

        var move = await admin.PostAsJsonAsync($"{Units}/{department.Id}/move",
            new { newParentId = sectorB.Id, effectiveDate = "2025-01-01", version = department.Version });
        move.StatusCode.ShouldBe(HttpStatusCode.OK, await move.Content.ReadAsStringAsync());

        var pathNow = await admin.GetFromJsonAsync<List<OrgUnitPathItemDto>>($"{Units}/{section.Id}/path");
        var pathBefore = await admin.GetFromJsonAsync<List<OrgUnitPathItemDto>>($"{Units}/{section.Id}/path?asOf=2024-06-30");
        pathNow!.Select(p => p.Id).ShouldBe([org.Id, sectorB.Id, department.Id, section.Id]);
        pathBefore!.Select(p => p.Id).ShouldBe([org.Id, sectorA.Id, department.Id, section.Id]);

        var treeBefore = await admin.GetFromJsonAsync<List<OrgTreeNodeDto>>($"{Units}/tree?asOf=2024-06-30");
        treeBefore!.Single(n => n.Id == department.Id).ParentId.ShouldBe(sectorA.Id);
        var treeNow = await admin.GetFromJsonAsync<List<OrgTreeNodeDto>>($"{Units}/tree");
        treeNow!.Single(n => n.Id == department.Id).ParentId.ShouldBe(sectorB.Id);

        var history = await admin.GetFromJsonAsync<List<OrgUnitVersionDto>>($"{Units}/{department.Id}/history");
        history!.Count.ShouldBe(2);
        history[0].ValidFrom.ShouldBe(new DateOnly(2025, 1, 1));
        history[1].ValidTo.ShouldBe(new DateOnly(2025, 1, 1));
        history[1].Path.Select(p => p.Id).ShouldContain(sectorA.Id);
    }

    [Fact]
    public async Task Structure_rules_are_enforced()
    {
        var admin = await api.AdminClientAsync();
        var org = await CreateUnitAsync(admin, Code("ORG"), "organisation", null);
        var department = await CreateUnitAsync(admin, Code("DEP"), "department", org.Id);
        var section = await CreateUnitAsync(admin, Code("SEC"), "section", department.Id);

        // A section cannot sit above a department (level order).
        var wrongLevel = await admin.PostAsJsonAsync(Units, new { code = Code("BAD"), nameEn = "Bad", levelCode = "sector", parentId = section.Id });
        await wrongLevel.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.unit.levelOrder");

        // Moving a unit below its own descendant is a cycle.
        var current = await admin.GetFromJsonAsync<OrgUnitDto>($"{Units}/{department.Id}");
        var cycle = await admin.PostAsJsonAsync($"{Units}/{department.Id}/move", new { newParentId = section.Id, version = current!.Version });
        await cycle.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.unit.levelOrder");

        // Duplicate code.
        var duplicate = await admin.PostAsJsonAsync(Units, new { code = org.Code, nameEn = "Dup", levelCode = "organisation" });
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "org.unit.duplicateCode");

        // Effective date before the current version.
        var early = await admin.PutAsJsonAsync($"{Units}/{section.Id}", new
        {
            code = section.Code,
            nameEn = "Renamed",
            levelCode = "section",
            effectiveDate = "2023-01-01",
            version = section.Version,
        });
        await early.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.unit.effectiveDateTooEarly");

        // Deactivating a unit with active children is refused.
        current = await admin.GetFromJsonAsync<OrgUnitDto>($"{Units}/{department.Id}");
        var deactivate = await admin.PostAsJsonAsync($"{Units}/{department.Id}/deactivate", new { version = current!.Version });
        await deactivate.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.unit.hasActiveChildren");
    }

    [Fact]
    public async Task Moving_below_a_descendant_is_refused()
    {
        var admin = await api.AdminClientAsync();
        var org = await CreateUnitAsync(admin, Code("ORG"), "organisation", null);
        var a = await CreateUnitAsync(admin, Code("A"), "department", org.Id);
        var b = await CreateUnitAsync(admin, Code("B"), "section", a.Id);

        var cycle = await admin.PostAsJsonAsync($"{Units}/{a.Id}/move", new { newParentId = b.Id, version = a.Version });

        // Level order catches it first by default; the domain also refuses cycles (unit tests).
        (await cycle.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBeOneOf("org.unit.levelOrder", "org.unit.cycle");
    }

    [Fact]
    public async Task Stale_version_returns_409()
    {
        var admin = await api.AdminClientAsync();
        var unit = await CreateUnitAsync(admin, Code("ORG"), "organisation", null);
        var body = new { code = unit.Code, nameEn = "First", levelCode = "organisation", version = unit.Version };

        (await admin.PutAsJsonAsync($"{Units}/{unit.Id}", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var stale = await admin.PutAsJsonAsync($"{Units}/{unit.Id}", body with { nameEn = "Second" });

        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "record.concurrencyConflict");
    }

    [Fact]
    public async Task Employees_are_created_searched_and_their_placement_history_is_kept()
    {
        var admin = await api.AdminClientAsync();
        var org = await CreateUnitAsync(admin, Code("ORG"), "organisation", null);
        var unitA = await CreateUnitAsync(admin, Code("DA"), "department", org.Id);
        var unitB = await CreateUnitAsync(admin, Code("DB"), "department", org.Id);
        var number = Code("E");

        var manager = await CreateEmployeeAsync(admin, Code("M"), "مدير الإدارة", unitA.Id, null);
        var created = await CreateEmployeeAsync(admin, number, "أحمد عبدالله", unitA.Id, manager.Id);
        created.Manager!.Id.ShouldBe(manager.Id);

        // Arabic-normalised search: "احمد" finds "أحمد".
        var found = await admin.GetFromJsonAsync<PagedResult<EmployeeListItemDto>>($"{Employees}?search=احمد عبدالله");
        found!.Items.ShouldContain(e => e.Id == created.Id);
        var below = await admin.GetFromJsonAsync<PagedResult<EmployeeListItemDto>>($"{Employees}?unitId={org.Id}&includeDescendants=true&pageSize=200");
        below!.Items.ShouldContain(e => e.Id == created.Id);

        var update = await admin.PutAsJsonAsync($"{Employees}/{created.Id}", new
        {
            employeeNo = number,
            nameAr = "أحمد عبدالله",
            email = created.Email,
            unitId = unitB.Id,
            managerEmployeeId = manager.Id,
            jobTitleEn = "Senior analyst",
            effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            version = created.Version,
        });
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());

        var history = await admin.GetFromJsonAsync<List<EmployeeAssignmentDto>>($"{Employees}/{created.Id}/history");
        history!.Count.ShouldBe(2);
        history[0].Unit.Id.ShouldBe(unitB.Id);
        history[1].Unit.Id.ShouldBe(unitA.Id);

        // A manager cannot report to their own subordinate.
        var current = await admin.GetFromJsonAsync<EmployeeDto>($"{Employees}/{manager.Id}");
        var cycle = await admin.PutAsJsonAsync($"{Employees}/{manager.Id}", new
        {
            employeeNo = current!.EmployeeNo,
            nameAr = current.NameAr,
            email = current.Email,
            unitId = unitA.Id,
            managerEmployeeId = created.Id,
            version = current.Version,
        });
        await cycle.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.employee.managerCycle");

        var duplicate = await admin.PostAsJsonAsync(Employees, new { employeeNo = number, nameEn = "Dup", email = "dup@example.test", unitId = unitA.Id });
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "org.employee.duplicateNumber");
    }

    [Fact]
    public async Task Regular_users_can_read_the_directory_but_not_change_it()
    {
        var user = await api.UserClientAsync();

        (await user.GetAsync($"{Units}/tree")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await user.GetAsync(Employees)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await user.PostAsJsonAsync(Units, new { code = "X", nameEn = "X", levelCode = "organisation" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
    }

    [Fact]
    public async Task Photo_accepts_images_only_and_is_served_back()
    {
        var admin = await api.AdminClientAsync();
        var org = await CreateUnitAsync(admin, Code("ORG"), "organisation", null);
        var employee = await CreateEmployeeAsync(admin, Code("P"), "صورة", org.Id, null);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

        var refused = await admin.PutAsync($"{Employees}/{employee.Id}/photo", File("<svg onload=alert(1)>"u8.ToArray(), "x.png", "image/png"));
        await refused.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.photo.invalid");

        (await admin.PutAsync($"{Employees}/{employee.Id}/photo", File(png, "me.png", "image/jpeg"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var served = await admin.GetAsync($"{Employees}/{employee.Id}/photo");
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png"); // from the content, not the declared type
        (await served.Content.ReadAsByteArrayAsync()).ShouldBe(png);
        (await admin.GetFromJsonAsync<EmployeeDto>($"{Employees}/{employee.Id}"))!.HasPhoto.ShouldBeTrue();
    }

    [Fact]
    public async Task Delegations_are_self_service_and_hidden_from_others()
    {
        var factory = api.Factory;
        var delegatorUser = await factory.CreateUserAsync();
        var delegateUser = await factory.CreateUserAsync();
        var outsiderUser = await factory.CreateUserAsync();
        var delegator = await factory.SignInAsync(delegatorUser);
        var outsider = await factory.SignInAsync(outsiderUser);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await delegator.PostAsJsonAsync("/api/v1/org/delegations", new
        {
            delegateEmployeeId = delegateUser.EmployeeId,
            fromDate = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            toDate = today.AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            scope = new[] { "portfolio" },
            reason = "Annual leave",
        });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var delegation = (await created.Content.ReadFromJsonAsync<DelegationDto>())!;

        (await delegator.GetFromJsonAsync<List<DelegationDto>>("/api/v1/org/delegations/mine"))!.ShouldContain(d => d.Id == delegation.Id);
        (await outsider.GetFromJsonAsync<List<DelegationDto>>("/api/v1/org/delegations/mine"))!.ShouldNotContain(d => d.Id == delegation.Id);

        // Someone else's delegation: 404, never 403 (no existence leak).
        await (await outsider.PostAsJsonAsync($"/api/v1/org/delegations/{delegation.Id}/cancel", new { reason = "x", version = delegation.Version }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "record.notFound");

        // Creating one for another person needs the administration permission.
        await (await outsider.PostAsJsonAsync("/api/v1/org/delegations", new
        {
            delegatorEmployeeId = delegatorUser.EmployeeId,
            delegateEmployeeId = outsiderUser.EmployeeId,
            fromDate = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            toDate = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");

        // Overlap rule.
        await (await delegator.PostAsJsonAsync("/api/v1/org/delegations", new
        {
            delegateEmployeeId = outsiderUser.EmployeeId,
            fromDate = today.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            toDate = today.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        })).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "org.delegation.overlap");

        var cancelled = await delegator.PostAsJsonAsync($"/api/v1/org/delegations/{delegation.Id}/cancel", new { reason = "Back early", version = delegation.Version });
        (await cancelled.Content.ReadFromJsonAsync<DelegationDto>())!.Status.ShouldBe("cancelled");
    }

    [Fact]
    public async Task Import_reports_every_problem_then_commits_all_or_nothing()
    {
        var admin = await api.AdminClientAsync();
        var suffix = Code("I");
        var template = await admin.GetAsync("/api/v1/org/import/templates/units");
        template.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var invalid = Csv(
            "code,parent_code,name_ar,name_en,level_code,active",
            $"{suffix}-ROOT,,جذر,Root,organisation,Y",
            $"{suffix}-A,{suffix}-MISSING,أ,A,sector,Y",
            $"{suffix}-B,{suffix}-ROOT,,,department,maybe",
            $"{suffix}-C,{suffix}-ROOT,ج,C,nolevel,Y");
        var report = await ImportAsync(admin, "units", "validate", invalid, "units.csv");
        report.Committed.ShouldBeFalse();
        report.Issues.Select(i => (i.Row, i.Column, i.Code)).ShouldBe(
        [
            (4, "name_ar", "validation.atLeastOneLanguage"),
            (4, "active", "import.yesNo"),
            (5, "level_code", "org.unit.unknownLevel"),
        ], ignoreOrder: true);

        // Commit refuses while problems remain, and nothing is written.
        (await ImportAsync(admin, "units", "commit", invalid, "units.csv")).Committed.ShouldBeFalse();
        var tree = await admin.GetFromJsonAsync<List<OrgTreeNodeDto>>($"{Units}/tree");
        tree!.ShouldNotContain(n => n.Code == $"{suffix}-ROOT");

        var reportFile = await admin.PostAsync("/api/v1/org/import/units/report", Form(invalid, "units.csv"));
        reportFile.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Children before parents is fine; one valid file commits everything.
        var valid = Csv(
            "code,parent_code,name_ar,name_en,level_code",
            $"{suffix}-DEP,{suffix}-SEC,إدارة,Department,department",
            $"{suffix}-SEC,{suffix}-ROOT,قطاع,Sector,sector",
            $"{suffix}-ROOT,,جذر,Root,organisation");
        var committed = await ImportAsync(admin, "units", "commit", valid, "units.csv");
        committed.Issues.ShouldBeEmpty();
        committed.Committed.ShouldBeTrue();
        committed.Created.ShouldBe(3);

        var employees = Csv(
            "employee_no,name_ar,name_en,email,unit_code,manager_employee_no,status,join_date,create_user",
            $"{suffix}-2,موظف,Staff,staff.{suffix}@example.test,{suffix}-DEP,{suffix}-1,active,2024-02-01,N",
            $"{suffix}-1,مدير,Manager,manager.{suffix}@example.test,{suffix}-DEP,,active,2023-05-01,Y");
        var people = await ImportAsync(admin, "employees", "commit", employees, "employees.csv");
        people.Issues.ShouldBeEmpty();
        people.Created.ShouldBe(2);
        var staff = await admin.GetFromJsonAsync<PagedResult<EmployeeListItemDto>>($"{Employees}?search={suffix}-2");
        var detail = await admin.GetFromJsonAsync<EmployeeDto>($"{Employees}/{staff!.Items.Single().Id}");
        detail!.Manager!.EmployeeNo.ShouldBe($"{suffix}-1");

        // create_user = Y provisioned an account and an invitation.
        var users = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/identity/admin/users?search=manager.{suffix}");
        users.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    private static async Task<EmployeeDto> CreateEmployeeAsync(HttpClient client, string number, string nameAr, Guid unitId, Guid? managerId)
    {
        var response = await client.PostAsJsonAsync(Employees, new
        {
            employeeNo = number,
            nameAr,
            nameEn = "Employee " + number,
            email = $"{number.ToLowerInvariant()}@example.test",
            unitId,
            managerEmployeeId = managerId,
            joinDate = "2024-01-01",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<EmployeeDto>())!;
    }

    private static async Task<ImportReportDto> ImportAsync(HttpClient client, string kind, string mode, string content, string fileName)
    {
        var response = await client.PostAsync($"/api/v1/org/import/{kind}?mode={mode}", Form(content, fileName));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportReportDto>())!;
    }

    private static string Csv(params string[] lines) => string.Join("\r\n", lines);

    private static MultipartFormDataContent Form(string content, string fileName) => File(Encoding.UTF8.GetBytes(content), fileName, "text/csv");

    private static MultipartFormDataContent File(byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }
}
