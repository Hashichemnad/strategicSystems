using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Teams;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Jobs;
using Strategy.Platform.Access;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Scheduling;

namespace Strategy.IntegrationTests.Kpi;

/// <summary>Records the KPI integration events the outbox delivers (Risk and CAP subscribe in later phases).</summary>
public sealed class RecordingKpiEvents : IIntegrationEventHandler<KpiResultApproved>
{
    public static ConcurrentQueue<KpiResultApproved> Approved { get; } = new();

    public static ConcurrentQueue<KpiTargetMissed> Missed { get; } = new();

    public Task HandleAsync(KpiResultApproved integrationEvent, CancellationToken cancellationToken)
    {
        Approved.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>A subscription is one handler type per event (the outbox message type is the handler's name).</summary>
public sealed class RecordingKpiMissedEvents : IIntegrationEventHandler<KpiTargetMissed>
{
    public Task HandleAsync(KpiTargetMissed integrationEvent, CancellationToken cancellationToken)
    {
        RecordingKpiEvents.Missed.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Stands in for Portfolio (Phase 9): project KPIs that link to cards (ADR 0065 test double).</summary>
public sealed class FakeProjectKpiLinks : IKpiLinkedRecordSource
{
    public static ConcurrentDictionary<Guid, Guid[]> Links { get; } = new();

    public string ResourceType => FakeProjectLinkTargets.Type;

    public Task<IReadOnlyList<Guid>> GetLinkedRecordIdsAsync(Guid cardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Links.TryGetValue(cardId, out var ids) ? ids : []);
}

/// <summary>One host per test class with a travelling clock, a capturing PDF renderer, the recording event handler and a fake project link source.</summary>
public sealed class KpiFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer>(new CapturingPdfRenderer());
            services.AddIntegrationEventHandler<KpiResultApproved, RecordingKpiEvents>();
            services.AddIntegrationEventHandler<KpiTargetMissed, RecordingKpiMissedEvents>();
            services.AddLinkableType(FakeProjectLinkTargets.Definition);
            services.AddLinkTargetSource<FakeProjectLinkTargets>();
            services.AddSingleton<IKpiLinkedRecordSource, FakeProjectKpiLinks>();
        });
        await Factory.GrantSystemAccessAsync(KpiPermissions.SystemCode, AccessGrants.Everyone, null);
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }

    public async Task<(int Opened, int Reminded, int Overdue, int Escalated)> RunWindowsAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        return await scope.ServiceProvider.GetRequiredService<WindowRunner>().RunAsync(CancellationToken.None);
    }
}

/// <summary>
/// An organisation with two departments. The PMO holds the Strategy Office role (MFA); owner A and B are responsible for KPIs in department A and B;
/// the manager heads department A and holds the department-manager role; the outsider only has the default owner role.
/// </summary>
internal sealed class KpiWorld
{
    public const string Base = "/api/v1/kpi";

    public required ApiFactory Factory { get; init; }

    public required Guid OrganisationId { get; init; }

    public required Guid DepartmentA { get; init; }

    public required Guid DepartmentB { get; init; }

    public required TestUser Pmo { get; init; }

    public required TestUser OwnerA { get; init; }

    public required TestUser OwnerB { get; init; }

    public required TestUser ManagerA { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static async Task<KpiWorld> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        Guid organisation, a, b;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"KO-{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            a = (await dispatcher.SendAsync(new CreateUnit($"KA-{suffix}", "إدارة أ", $"Department A {suffix}", "department", organisation, null, null, null, null))).Value.Id;
            b = (await dispatcher.SendAsync(new CreateUnit($"KB-{suffix}", "إدارة ب", $"Department B {suffix}", "department", organisation, null, null, null, null))).Value.Id;
        }

        var pmo = await factory.CreateUserAsync(withMfa: true, unitId: organisation, roles: KpiPermissions.StrategyOfficeRoleKey);
        var manager = await factory.CreateUserAsync(unitId: a, roles: "kpi.department-manager");
        var ownerA = await factory.CreateUserAsync(unitId: a, managerEmployeeId: manager.EmployeeId);
        var ownerB = await factory.CreateUserAsync(unitId: b);
        var outsider = await factory.CreateUserAsync();
        await ApprovalWorld.SetHeadAsync(factory, a, manager.EmployeeId);
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { pmo, manager, ownerA, ownerB, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new KpiWorld
        {
            Factory = factory,
            OrganisationId = organisation,
            DepartmentA = a,
            DepartmentB = b,
            Pmo = pmo,
            OwnerA = ownerA,
            OwnerB = ownerB,
            ManagerA = manager,
            Outsider = outsider,
            Clients = clients,
        };
    }

    /// <summary>Signs everyone but the PMO (MFA) in again: sessions follow the travelling clock.</summary>
    public async Task ReSignInAsync()
    {
        foreach (var user in new[] { ManagerA, OwnerA, OwnerB, Outsider })
        {
            Clients[user.Id] = await Factory.SignInAsync(user);
        }
    }

    public static object Card(
        string name, TestUser owner, Guid unit, string period = "quarterly", string direction = "increase", string method = "cumulative", string unitCode = "number",
        Guid? objectiveId = null, decimal? weight = null, Guid? parentCardId = null, string targetType = "single", string? formula = null, object[]? elements = null,
        string nature = "quantitative", object[]? scale = null, string[]? gem = null, bool globalStar = false) => new
        {
            nameAr = "مؤشر " + name,
            nameEn = name,
            measuringEquation = "count of " + name,
            nature,
            measuringMethodCode = method,
            unitCode,
            measuringPeriod = period,
            direction,
            targetType,
            objectiveId,
            departmentUnitId = unit,
            responsibleUnitId = unit,
            responsibleEmployeeId = owner.EmployeeId,
            level = "department",
            weight,
            parentCardId,
            formula,
            dataElements = elements ?? [],
            scale = scale ?? [],
            gemCriteriaCodes = gem ?? [],
            isGlobalStar = globalStar,
            typeCode = "strategic",
        };

    public async Task<KpiCardDto> CreateCardAsync(object values)
    {
        var response = await C(Pmo).PostAsJsonAsync($"{Base}/cards", new { values });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<KpiCardDto>())!;
    }

    public static object Item(KpiCardDto card, decimal? target = null, decimal? min = null, decimal? max = null) => new
    {
        cardId = card.Id,
        include = true,
        measuringPeriod = card.Values.MeasuringPeriod,
        unitCode = card.Values.UnitCode,
        direction = card.Values.Direction,
        targetType = card.Values.TargetType,
        target,
        rangeMin = min,
        rangeMax = max,
        isAccumulated = false,
    };

    /// <summary>Starts the year for the given items, optionally moves every window to "open since yesterday, due in N days", confirms the plans.</summary>
    public async Task<List<KpiPlanListItemDto>> StartYearAsync(int year, object[] items, bool openNow = true, bool confirm = true, int dueInDays = 10)
    {
        var started = await OkAsync<KpiYearStartedDto>(C(Pmo).PostAsJsonAsync($"{Base}/years/{year}/start", new { items }));
        started.PlansCreated.ShouldBe(items.Length);
        if (openNow)
        {
            var today = DateOnly.FromDateTime(Factory.Services.GetRequiredService<IClock>().UtcNow.UtcDateTime);
            var windows = new[] { "q1", "q2", "q3", "q4", "h1", "h2", "fy", "ty" }
                .Select(p => new { periodCode = p, opensOn = today.AddDays(-1), dueOn = today.AddDays(dueInDays) }).ToArray();
            await OkAsync<List<KpiWindowDto>>(C(Pmo).PutAsJsonAsync($"{Base}/years/{year}/windows", new { windows }));
        }

        var plans = (await C(Pmo).GetFromJsonAsync<List<KpiPlanListItemDto>>($"{Base}/plans?year={year}"))!;
        if (confirm)
        {
            var ids = plans.Where(p => items.Any(i => (Guid)i.GetType().GetProperty("cardId")!.GetValue(i)! == p.CardId)).Select(p => p.Id).ToList();
            (await OkAsync<int>(C(Pmo).PostAsJsonAsync($"{Base}/plans/confirm", new { planIds = ids }))).ShouldBe(ids.Count);
        }

        return (await C(Pmo).GetFromJsonAsync<List<KpiPlanListItemDto>>($"{Base}/plans?year={year}"))!;
    }

    public async Task<KpiPlanDto> PlanAsync(Guid cardId, int year)
    {
        var plans = (await C(Pmo).GetFromJsonAsync<List<KpiPlanListItemDto>>($"{Base}/plans?year={year}"))!;
        return (await C(Pmo).GetFromJsonAsync<KpiPlanDto>($"{Base}/plans/{plans.Single(p => p.CardId == cardId).Id}"))!;
    }

    public async Task<KpiResultDto> ResultAsync(Guid id, TestUser? user = null) => (await C(user ?? Pmo).GetFromJsonAsync<KpiResultDto>($"{Base}/results/{id}"))!;

    public static object Entry(decimal? achieve, string? causality = null, string? actions = null, bool na = false, string? naReason = null,
        IReadOnlyDictionary<string, decimal?>? elements = null, decimal? scale = null) =>
        new { achieve, elementValues = elements, scaleValue = scale, causality, correctiveActions = actions, notes = "notes", notApplicable = na, naReason };

    /// <summary>Owner uploads evidence and submits.</summary>
    public async Task<KpiResultDto> SubmitAsync(TestUser owner, Guid resultId, object entry, bool evidence = true)
    {
        if (evidence)
        {
            await TeamsWorld.UploadAsync(C(owner), "kpi.result", resultId);
        }

        var current = await ResultAsync(resultId, owner);
        return await OkAsync<KpiResultDto>(C(owner).PostAsJsonAsync($"{Base}/results/{resultId}/submit", new { entry, version = current.Version }));
    }

    public static readonly IReadOnlyDictionary<string, bool> FullChecklist = new Dictionary<string, bool>
    {
        ["evidence-readable"] = true, ["evidence-supports"] = true, ["equation-followed"] = true, ["correct-period"] = true, ["justification-adequate"] = true,
    };

    public async Task<KpiResultDto> DecideAsync(Guid resultId, string action = "approve", string? comment = null, IReadOnlyDictionary<string, bool>? checklist = null) =>
        await OkAsync<KpiResultDto>(C(Pmo).PostAsJsonAsync($"{Base}/results/{resultId}/decide", new { action, comment, checklist = checklist ?? FullChecklist }));

    public static async Task<Strategy.Platform.Workflow.Application.MyTaskDto> MyTaskAsync(HttpClient client, string resourceType, Guid id)
    {
        var response = await client.GetAsync($"/api/v1/workflow/subjects/{resourceType}/{id}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var panel = (await response.Content.ReadFromJsonAsync<Strategy.Platform.Workflow.Application.SubjectApprovalsDto>())!;
        var mine = panel.Instances.Select(i => i.MyTask).FirstOrDefault(t => t is not null);
        mine.ShouldNotBeNull("the caller should be able to act on the current step");
        return mine;
    }

    public static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static ClaimsPrincipal System => StrategyClaims.CreateSystemPrincipal();
}
