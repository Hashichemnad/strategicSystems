using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Sample.Application;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Workflow;

/// <summary>
/// A small organisation for approval tests: a department (head: director) with a section (head: section manager); the submitter sits in the
/// section and reports to the section manager. Every person has a signed-in client.
/// </summary>
public sealed class ApprovalWorld
{
    public const string Items = "/api/v1/sample/items";

    public required ApiFactory Factory { get; init; }

    public required Guid DepartmentId { get; init; }

    public required Guid SectionId { get; init; }

    public required TestUser Director { get; init; }

    public required TestUser SectionManager { get; init; }

    public required TestUser Submitter { get; init; }

    public required HttpClient DirectorClient { get; init; }

    public required HttpClient SectionManagerClient { get; init; }

    public required HttpClient SubmitterClient { get; init; }

    public static async Task<ApprovalWorld> CreateAsync(ApiFactory factory, bool withSection = true)
    {
        var suffix = Guid.CreateVersion7().ToString("N")[^8..];
        Guid department, section;
        await using (var scope = TestAccess.SystemScope(factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            department = (await dispatcher.SendAsync(new CreateUnit($"DEP-{suffix}", "إدارة", $"Department {suffix}", "department", null, null, null, null, null))).Value.Id;
            section = (await dispatcher.SendAsync(new CreateUnit($"SEC-{suffix}", "قسم", $"Section {suffix}", "section", department, null, null, null, null))).Value.Id;
        }

        var director = await factory.CreateUserAsync(unitId: department);
        var manager = await factory.CreateUserAsync(unitId: section, managerEmployeeId: director.EmployeeId);
        var submitter = await factory.CreateUserAsync(unitId: withSection ? section : department, managerEmployeeId: withSection ? manager.EmployeeId : director.EmployeeId,
            roles: TestIdentity.SampleEditor);
        await SetHeadAsync(factory, department, director.EmployeeId);
        await SetHeadAsync(factory, section, manager.EmployeeId);
        return new ApprovalWorld
        {
            Factory = factory,
            DepartmentId = department,
            SectionId = section,
            Director = director,
            SectionManager = manager,
            Submitter = submitter,
            DirectorClient = await factory.SignInAsync(director),
            SectionManagerClient = await factory.SignInAsync(manager),
            SubmitterClient = await factory.SignInAsync(submitter),
        };
    }

    public static async Task SetHeadAsync(ApiFactory factory, Guid unitId, Guid? headEmployeeId)
    {
        await using var system = TestAccess.SystemScope(factory);
        var dispatcher = system.ServiceProvider.GetRequiredService<IDispatcher>();
        var unit = (await dispatcher.SendAsync(new GetUnit(unitId))).Value;
        var updated = await dispatcher.SendAsync(new UpdateUnit(unitId, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode, headEmployeeId, null, null, null, unit.Version));
        updated.IsSuccess.ShouldBeTrue(updated.IsFailure ? updated.Error.Code : null);
    }

    public async Task<SampleItemDto> CreateItemAsync(decimal? amount = 1000m, HttpClient? client = null)
    {
        var response = await (client ?? SubmitterClient).PostAsJsonAsync(Items, new { nameAr = "بند للاعتماد", nameEn = "Item to approve", amount });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }

    public async Task<HttpResponseMessage> SubmitAsync(SampleItemDto item, string? comment = null, string? key = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Items}/{item.Id}/submit") { Content = JsonContent.Create(new { version = item.Version, comment }) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await (client ?? SubmitterClient).SendAsync(request);
    }

    public async Task<SampleItemDto> SubmitOkAsync(SampleItemDto item, string? comment = null)
    {
        var response = await SubmitAsync(item, comment);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SampleItemDto>())!;
    }

    public static async Task<SubjectApprovalsDto> PanelAsync(HttpClient client, Guid itemId)
    {
        var response = await client.GetAsync($"/api/v1/workflow/subjects/sample.item/{itemId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SubjectApprovalsDto>())!;
    }

    public static async Task<HttpResponseMessage> ActAsync(
        HttpClient client, Guid taskId, string action, string? comment = null, string? key = null, Guid? onBehalfOf = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/workflow/tasks/{taskId}/act")
        {
            Content = JsonContent.Create(new { action, comment, onBehalfOfUserId = onBehalfOf }),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request);
    }

    public static async Task<ActResult> ActOkAsync(HttpClient client, Guid taskId, string action, string? comment = null)
    {
        var response = await ActAsync(client, taskId, action, comment);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ActResult>())!;
    }

    /// <summary>The pending task of the newest instance, as seen by <paramref name="client"/> (must be able to act).</summary>
    public static async Task<MyTaskDto> MyTaskAsync(HttpClient client, Guid itemId)
    {
        var panel = await PanelAsync(client, itemId);
        panel.Instances[0].MyTask.ShouldNotBeNull("the caller should be able to act on the current step");
        return panel.Instances[0].MyTask!;
    }

    public async Task<SampleItemDto> ItemAsync(Guid id) =>
        (await SubmitterClient.GetFromJsonAsync<SampleItemDto>($"{Items}/{id}"))!;

    public static string CurrentStep(SubjectApprovalsDto panel) =>
        panel.Instances[0].Steps.Single(s => s.State == StepStates.Current).Code;

    /// <summary>Saves a chain for the sample process as a new version (admin API).</summary>
    public static async Task SaveChainAsync(HttpClient admin, params ApprovalStepDefinition[] steps)
    {
        var current = (await admin.GetFromJsonAsync<ProcessDetailDto>($"/api/v1/workflow/processes/{SampleApprovalProcess.Key}"))!;
        var response = await admin.PutAsJsonAsync($"/api/v1/workflow/processes/{SampleApprovalProcess.Key}",
            new SaveProcessInput(steps, "test chain", current.VersionNo));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
