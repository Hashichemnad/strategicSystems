using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Modules.Teams.Application;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Teams;

/// <summary>Captures the HTML sent to Gotenberg and returns a tiny valid PDF (the Files service checks the magic number).</summary>
public sealed class CapturingPdfRenderer : IPdfRenderer
{
    public ConcurrentQueue<string> Html { get; } = new();

    public Task<byte[]> RenderAsync(string html, CancellationToken cancellationToken)
    {
        Html.Enqueue(html);
        return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
    }
}

/// <summary>
/// A team-able linkable type whose records everyone may read (ADR 0043 test double). Its own type code: the real <c>portfolio.project</c> exists
/// since Phase 9 and is tested end to end in the Portfolio tests.
/// </summary>
public sealed class FakeProjectLinkTargets : ILinkTargetSource
{
    public const string Type = "test.project";

    public static readonly LinkableTypeDefinition Definition = new(
        Type, "sample", "مشروع (اختبار)", "Project (test)", [LinkCapabilities.TeamAble], "teams.team.read", "/projects/{id}");

    public string ResourceType => Type;

    public Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, LinkTarget>>(ids.ToDictionary(id => id, id => new LinkTarget(id, "PROJ-T", "مشروع", "Project", "execution", null, null, "info", false)));

    public Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>One host per test class with a travelling clock, a capturing PDF renderer and the fake project type.</summary>
public sealed class TeamsFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public TravellingClock Clock { get; } = new();

    public CapturingPdfRenderer Pdf { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IPdfRenderer>();
            services.AddSingleton<IPdfRenderer>(Pdf);
            services.AddLinkableType(FakeProjectLinkTargets.Definition);
            services.AddLinkTargetSource<FakeProjectLinkTargets>();
        });

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>
/// A small organisation: a top unit (head: General Manager) with a department (head: Director). HR holds the Teams administrator role; a
/// requester, the President (leader), two members, a secretary and an outsider work in the department or elsewhere.
/// </summary>
internal sealed class TeamsWorld
{
    public const string Base = "/api/v1/teams";

    public required ApiFactory Factory { get; init; }

    public required Guid OrganisationId { get; init; }

    public required Guid DepartmentId { get; init; }

    public required TestUser Gm { get; init; }

    public required TestUser Director { get; init; }

    public required TestUser Hr { get; init; }

    public required TestUser Requester { get; init; }

    public required TestUser President { get; init; }

    public required TestUser MemberA { get; init; }

    public required TestUser MemberB { get; init; }

    public required TestUser Secretary { get; init; }

    public required TestUser Outsider { get; init; }

    public required Dictionary<Guid, HttpClient> Clients { get; init; }

    public HttpClient C(TestUser user) => Clients[user.Id];

    public static async Task<TeamsWorld> CreateAsync(ApiFactory factory)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        Guid organisation, department;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            organisation = (await dispatcher.SendAsync(new CreateUnit($"ORG-{suffix}", "الجهة", $"Organisation {suffix}", "organisation", null, null, null, null, null))).Value.Id;
            department = (await dispatcher.SendAsync(new CreateUnit($"DEP-{suffix}", "إدارة", $"Department {suffix}", "department", organisation, null, null, null, null))).Value.Id;
        }

        var gm = await factory.CreateUserAsync(unitId: organisation);
        var director = await factory.CreateUserAsync(unitId: department, managerEmployeeId: gm.EmployeeId);
        var hr = await factory.CreateUserAsync(withMfa: true, unitId: department, roles: TeamPermissions.AdministratorRoleKey);
        var requester = await factory.CreateUserAsync(unitId: department, managerEmployeeId: director.EmployeeId);
        var president = await factory.CreateUserAsync(unitId: department, managerEmployeeId: director.EmployeeId);
        var a = await factory.CreateUserAsync(unitId: department, managerEmployeeId: director.EmployeeId);
        var b = await factory.CreateUserAsync(unitId: department, managerEmployeeId: director.EmployeeId);
        var secretary = await factory.CreateUserAsync(unitId: department, managerEmployeeId: director.EmployeeId);
        var outsider = await factory.CreateUserAsync();
        await ApprovalWorld.SetHeadAsync(factory, organisation, gm.EmployeeId);
        await ApprovalWorld.SetHeadAsync(factory, department, director.EmployeeId);
        var clients = new Dictionary<Guid, HttpClient>();
        foreach (var user in new[] { gm, director, hr, requester, president, a, b, secretary, outsider })
        {
            clients[user.Id] = await factory.SignInAsync(user);
        }

        return new TeamsWorld
        {
            Factory = factory,
            OrganisationId = organisation,
            DepartmentId = department,
            Gm = gm,
            Director = director,
            Hr = hr,
            Requester = requester,
            President = president,
            MemberA = a,
            MemberB = b,
            Secretary = secretary,
            Outsider = outsider,
            Clients = clients,
        };
    }

    /// <summary>Signs everyone in again (sessions follow the travelling clock).</summary>
    /// <summary>Signs everyone but HR (MFA) in again: sessions follow the travelling clock.</summary>
    public async Task ReSignInAsync()
    {
        foreach (var user in new[] { Gm, Director, Requester, President, MemberA, MemberB, Secretary, Outsider })
        {
            Clients[user.Id] = await Factory.SignInAsync(user);
        }
    }

    /// <summary>A user (no MFA) whose role reads every team and its evaluations and may open / cancel cycles: HR's view after a clock jump.</summary>
    public async Task<HttpClient> HrViewAsync()
    {
        var role = await Factory.CreateRoleAsync(TeamPermissions.SystemCode, (TeamPermissions.Read, DataScope.All), (TeamPermissions.EvaluationOpen, DataScope.All),
            (TeamPermissions.Results, DataScope.All));
        return await Factory.SignInAsync(await Factory.CreateUserAsync(unitId: DepartmentId, roles: role));
    }

    public object Request(string name, string type = "standing-committee", bool permanent = true, DateOnly? start = null, DateOnly? end = null, string memoMode = "generate",
        string? linkedType = null, Guid? linkedId = null, bool withExternal = true) => new
        {
            typeCode = type,
            nameAr = "لجنة " + name,
            nameEn = name,
            permanent,
            subject = "Governance of " + name,
            objectives = "Improve the process",
            mandate = "Review and recommend",
            owningUnitId = DepartmentId,
            sponsorEmployeeId = Director.EmployeeId,
            memoMode,
            startDate = start ?? DateOnly.FromDateTime(DateTime.UtcNow),
            endDate = end,
            workload = "high",
            linkedType,
            linkedId,
            members = new List<object?>
        {
            new { employeeId = President.EmployeeId, roleCode = "president", responsibilities = "Chairs the committee" },
            new { employeeId = MemberA.EmployeeId, roleCode = "member", responsibilities = "Analysis", requiredSkills = "finance budgeting" },
            new { employeeId = Secretary.EmployeeId, roleCode = "secretary", responsibilities = "Minutes" },
            withExternal ? new { externalName = "Dr. Lina Haddad", externalOrganisation = "University", externalEmail = "lina@uni.example", roleCode = "member" } : null,
        }.Where(m => m is not null).ToList(),
        };

    public async Task<TeamDto> CreateAsync(object request, HttpClient? client = null)
    {
        var response = await (client ?? C(Requester)).PostAsJsonAsync(Base, request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TeamDto>())!;
    }

    public async Task<TeamDto> GetAsync(Guid id, TestUser? user = null) => (await C(user ?? Hr).GetFromJsonAsync<TeamDto>($"{Base}/{id}"))!;

    public async Task<TeamDto> SubmitAsync(TeamDto team, HttpClient? client = null)
    {
        var response = await (client ?? C(Requester)).PostAsJsonAsync($"{Base}/{team.Id}/submit", new { version = team.Version });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TeamDto>())!;
    }

    public static async Task<MyTaskDto> MyTaskAsync(HttpClient client, Guid teamId)
    {
        var response = await client.GetAsync($"/api/v1/workflow/subjects/teams.team/{teamId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var panel = (await response.Content.ReadFromJsonAsync<SubjectApprovalsDto>())!;
        var mine = panel.Instances.Select(i => i.MyTask).FirstOrDefault(t => t is not null);
        mine.ShouldNotBeNull("the caller should be able to act on the current step");
        return mine;
    }

    /// <summary>Request → submit → Director → General Manager: an Active team.</summary>
    public async Task<TeamDto> FormAsync(object request)
    {
        var team = await SubmitAsync(await CreateAsync(request));
        team.Status.ShouldBe("pending_approval");
        await ApprovalWorld.ActOkAsync(C(Director), (await MyTaskAsync(C(Director), team.Id)).TaskId, "approve");
        await ApprovalWorld.ActOkAsync(C(Gm), (await MyTaskAsync(C(Gm), team.Id)).TaskId, "approve");
        var formed = await GetAsync(team.Id);
        formed.Status.ShouldBe("active");
        return formed;
    }

    public static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<FileDtoLite> UploadAsync(HttpClient client, string ownerType, Guid ownerId, string name = "evidence.pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        var response = await client.PostAsync($"/api/v1/files/owners/{ownerType}/{ownerId}", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FileDtoLite>())!;
    }

    public static async Task WaitAsync(Func<Task<bool>> condition, string what)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new ShouldAssertException($"Timed out waiting for {what}.");
    }
}

public sealed record FileDtoLite(Guid Id, string Purpose, string OwnerType, string OwnerId, string Name, string ContentType);
