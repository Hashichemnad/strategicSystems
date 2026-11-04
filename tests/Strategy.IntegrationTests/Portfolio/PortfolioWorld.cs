using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Portfolio;

/// <summary>One host per test class with its own database (the Portfolio roles and process are installation-wide).</summary>
public sealed class PortfolioFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Factory = await ApiFactory.CreateAsync(postgres);

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>
/// An organisation (head: General Manager) with a department (head: Director) and its section (head: Section manager); a second department.
/// Requesters in the department (no section step, Q-P4) and in the section (section step); the PM Office, DG Office, Budget Section and
/// Administration role holders; a department viewer; a requester of the other department; an outsider in no unit (requester role only).
/// </summary>
internal sealed class PortfolioWorld
{
    public const string Base = "/api/v1/portfolio";
    public const string Objective = "digital-services";

    public required ApiFactory Factory { get; init; }

    public required string Suffix { get; init; }

    public required Guid DepartmentId { get; init; }

    public required Guid SectionId { get; init; }

    public required Guid OtherDepartmentId { get; init; }

    public required TestUser Director { get; init; }

    public required TestUser SectionManager { get; init; }

    public required TestUser Requester { get; init; }

    public required TestUser SectionRequester { get; init; }

    public required TestUser PmOffice { get; init; }

    public required TestUser DgOffice { get; init; }

    public required TestUser Budget { get; init; }

    public required TestUser Administration { get; init; }

    public required TestUser DepartmentViewer { get; init; }

    public required TestUser OtherRequester { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static int Year => DateTime.UtcNow.Year;

    public static async Task<PortfolioWorld> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        Guid organisation, department, section, other;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"ORG-{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            department = (await dispatcher.SendAsync(new CreateUnit($"DEP-{suffix}", "إدارة", $"Department {suffix}", "department", organisation, null, null, null, null))).Value.Id;
            section = (await dispatcher.SendAsync(new CreateUnit($"SEC-{suffix}", "قسم", $"Section {suffix}", "section", department, null, null, null, null))).Value.Id;
            other = (await dispatcher.SendAsync(new CreateUnit($"OTH-{suffix}", "إدارة أخرى", $"Other {suffix}", "department", organisation, null, null, null, null))).Value.Id;

            // Strategic objectives are configuration (seeded empty, Q-P7) held by the KPI strategy hierarchy (ADR 0075).
            await scope.ServiceProvider.GetRequiredService<IKpiStrategyCatalog>().EnsureObjectivesAsync(
            [
                new(Objective, "خدمات رقمية", "Digital services", true, 10),
                new("efficiency", "الكفاءة", "Efficiency", true, 20),
            ]);

            // Portfolio is a business system: everyone may open it (the requester role is everyone's).
            await AccessGrants.GrantSystemAccessAsync(scope.ServiceProvider, PortfolioPermissions.SystemCode, AccessGrants.Everyone, null);
        }

        var director = await factory.CreateUserAsync(unitId: department);
        var manager = await factory.CreateUserAsync(unitId: section, managerEmployeeId: director.EmployeeId);
        var requester = await factory.CreateUserAsync(unitId: department, managerEmployeeId: director.EmployeeId);
        var sectionRequester = await factory.CreateUserAsync(unitId: section, managerEmployeeId: manager.EmployeeId);
        var pmOffice = await factory.CreateUserAsync(unitId: organisation, roles: PortfolioPermissions.PmOfficeRoleKey);
        var dg = await factory.CreateUserAsync(unitId: organisation, roles: PortfolioPermissions.DgOfficeRoleKey);
        var budget = await factory.CreateUserAsync(unitId: organisation, roles: PortfolioPermissions.BudgetSectionRoleKey);
        var administration = await factory.CreateUserAsync(unitId: organisation, roles: PortfolioPermissions.AdministrationRoleKey);
        var viewer = await factory.CreateUserAsync(unitId: department, roles: "portfolio.department-viewer");
        var otherDirector = await factory.CreateUserAsync(unitId: other);
        var otherRequester = await factory.CreateUserAsync(unitId: other, managerEmployeeId: otherDirector.EmployeeId);
        var outsider = await factory.CreateUserAsync(withEmployee: false);
        await ApprovalWorld.SetHeadAsync(factory, department, director.EmployeeId);
        await ApprovalWorld.SetHeadAsync(factory, section, manager.EmployeeId);
        await ApprovalWorld.SetHeadAsync(factory, other, otherDirector.EmployeeId);
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { director, manager, requester, sectionRequester, pmOffice, dg, budget, administration, viewer, otherRequester, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new PortfolioWorld
        {
            Factory = factory,
            Suffix = suffix,
            DepartmentId = department,
            SectionId = section,
            OtherDepartmentId = other,
            Director = director,
            SectionManager = manager,
            Requester = requester,
            SectionRequester = sectionRequester,
            PmOffice = pmOffice,
            DgOffice = dg,
            Budget = budget,
            Administration = administration,
            DepartmentViewer = viewer,
            OtherRequester = otherRequester,
            Outsider = outsider,
            Clients = clients,
        };
    }

    /// <summary>A complete, valid request: every SPEC §1.4 A rule satisfied. With a budget, Year 1–4 add up to it and the cost path is the cost estimation file.</summary>
    public static RequestValues Valid(string name, decimal? budget = null, bool allYes = true) => new()
    {
        NameAr = "برنامج " + name,
        NameEn = name,
        Description = "Digitise the service end to end.",
        Justifications = "Customers wait too long today.",
        StrategicObjectiveCode = Objective,
        SuggestionSourceCode = "idea",
        ProgramTypeCode = "digital",
        RequiresBudget = budget is not null,
        EstimatedBudget = budget,
        Year1Cost = budget is { } b ? b - (3 * Math.Round(b / 10, 2)) : null,
        Year2Cost = budget is { } b2 ? Math.Round(b2 / 10, 2) : null,
        Year3Cost = budget is { } b3 ? Math.Round(b3 / 10, 2) : null,
        Year4Cost = budget is { } b4 ? Math.Round(b4 / 10, 2) : null,
        CostPath = budget is null ? null : CostPaths.CostEstimation,
        ExpectedStartDate = new DateOnly(Year + 1, 1, 1),
        ExpectedEndDate = new DateOnly(Year + 2, 6, 30),
        ProgramNature = ProgramNatures.New,
        ScopeOfWork = "Online forms, payments and tracking.",
        ScreenCoreWork = true,
        ScreenOneTime = true,
        ScreenTimeBound = true,
        ScreenUniqueOutput = true,
        ScreenMinCost = allYes,
        ScreenOperationalCosts = true,
        PriorityScores = new Dictionary<string, int> { ["vision-alignment"] = 5, ["impact"] = 4, ["complexity"] = 3, ["radical-change"] = 4 },
        MainOptionCode = "contracting",
        PracticeService = "Licensing service",
    };

    public async Task<RequestDto> CreateAsync(RequestValues values, TestUser? user = null, string? source = null)
    {
        var response = await C(user ?? Requester).PostAsJsonAsync($"{Base}/requests", new { values, source });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RequestDto>())!;
    }

    public async Task<RequestDto> GetAsync(Guid id, TestUser? user = null) => (await C(user ?? PmOffice).GetFromJsonAsync<RequestDto>($"{Base}/requests/{id}"))!;

    public Task<HttpResponseMessage> SubmitRawAsync(RequestDto request, TestUser? user = null, string? comment = null) =>
        C(user ?? Requester).PostAsJsonAsync($"{Base}/requests/{request.Id}/submit", new { version = request.Version, comment });

    public async Task<RequestDto> SubmitAsync(RequestDto request, TestUser? user = null)
    {
        var response = await SubmitRawAsync(request, user);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RequestDto>())!;
    }

    /// <summary>Creates a request with a budget, uploads its cost estimation file and returns the fresh DTO.</summary>
    public async Task<RequestDto> CreateWithCostFileAsync(RequestValues values, TestUser? user = null)
    {
        var created = await CreateAsync(values, user);
        if (values.RequiresBudget == true)
        {
            await UploadAsync(C(user ?? Requester), created.Id, CostPaths.CostEstimation);
        }

        return await GetAsync(created.Id);
    }

    public static async Task<RequestFileDto> UploadAsync(HttpClient client, Guid requestId, string kind, string name = "file.pdf")
    {
        var response = await UploadRawAsync(client, requestId, kind, name);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RequestFileDto>())!;
    }

    public static Task<HttpResponseMessage> UploadRawAsync(HttpClient client, Guid requestId, string kind, string name = "file.pdf", byte[]? content = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content ?? Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue(name.EndsWith(".docx", StringComparison.Ordinal)
            ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            : "application/pdf");
        form.Add(file, "file", name);
        return client.PostAsync($"{Base}/requests/{requestId}/files/{kind}", form);
    }

    /// <summary>The step in progress (code) and the caller's task, from the shared approval panel.</summary>
    public async Task<(string Step, MyTaskDto? Task)> CurrentAsync(TestUser approver, Guid requestId)
    {
        var response = await C(approver).GetAsync($"/api/v1/workflow/subjects/portfolio.request/{requestId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var panel = (await response.Content.ReadFromJsonAsync<SubjectApprovalsDto>())!;
        var instance = panel.Instances[0];
        var current = instance.Steps.SingleOrDefault(s => s.State == StepStates.Current);
        return (current?.Code ?? string.Empty, instance.MyTask);
    }

    /// <summary>The approver acts on the step in progress, which must be <paramref name="expectedStep"/>.</summary>
    public async Task ActAsync(TestUser approver, Guid requestId, string expectedStep, string action = "approve", string? comment = null)
    {
        var (step, task) = await CurrentAsync(approver, requestId);
        step.ShouldBe(expectedStep);
        task.ShouldNotBeNull($"{approver.UserName} should be able to act on {expectedStep}");
        await ApprovalWorld.ActOkAsync(C(approver), task.TaskId, action, comment);
    }

    /// <summary>The step codes the newest instance went through (done), in order.</summary>
    public async Task<IReadOnlyList<string>> StepsAsync(Guid requestId)
    {
        var panel = (await C(PmOffice).GetFromJsonAsync<SubjectApprovalsDto>($"/api/v1/workflow/subjects/portfolio.request/{requestId}"))!;
        return panel.Instances[0].Steps.Where(s => s.State == StepStates.Done).Select(s => s.Code).ToList();
    }

    /// <summary>A 400 validation problem's field errors.</summary>
    public static async Task<List<FieldErrorLite>> ProblemFieldsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("errors").Deserialize<List<FieldErrorLite>>(JsonSerializerOptions.Web)!;
    }

    public static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}

public sealed record FieldErrorLite(string Field, string Code, string MessageKey, Dictionary<string, JsonElement>? Params);
