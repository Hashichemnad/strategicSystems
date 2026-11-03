using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Modules.Tasks.Application;
using Strategy.Platform.Kernel.Paging;

namespace Strategy.IntegrationTests.Tasks;

/// <summary>
/// A small organisation for task tests: a manager heading a unit with three direct reports, an outsider in another unit, and an administrator.
/// </summary>
internal sealed class TasksWorld
{
    public const string Base = "/api/v1/tasks";

    private TasksWorld()
    {
    }

    public required ApiFactory Factory { get; init; }

    public required TestUser Manager { get; init; }

    public required TestUser A { get; init; }

    public required TestUser B { get; init; }

    public required TestUser C { get; init; }

    public required TestUser Outsider { get; init; }

    public required HttpClient ManagerClient { get; init; }

    public required HttpClient AClient { get; init; }

    public required HttpClient BClient { get; init; }

    public required HttpClient CClient { get; init; }

    public required HttpClient OutsiderClient { get; init; }

    public static async Task<TasksWorld> CreateAsync(ApiFactory factory)
    {
        var manager = await factory.CreateUserAsync();
        var a = await factory.CreateUserAsync(unitId: manager.UnitId, managerEmployeeId: manager.EmployeeId);
        var b = await factory.CreateUserAsync(unitId: manager.UnitId, managerEmployeeId: manager.EmployeeId);
        var c = await factory.CreateUserAsync(unitId: manager.UnitId, managerEmployeeId: manager.EmployeeId);
        var outsider = await factory.CreateUserAsync();
        return new TasksWorld
        {
            Factory = factory,
            Manager = manager,
            A = a,
            B = b,
            C = c,
            Outsider = outsider,
            ManagerClient = await factory.SignInAsync(manager),
            AClient = await factory.SignInAsync(a),
            BClient = await factory.SignInAsync(b),
            CClient = await factory.SignInAsync(c),
            OutsiderClient = await factory.SignInAsync(outsider),
        };
    }

    public static object NewTask(string title, params Guid?[] assignees) => new
    {
        titleEn = title,
        titleAr = "مهمة " + title,
        typeCode = "manager-assignment",
        sourceCode = "manager-assignment",
        priority = "high",
        assigneeEmployeeIds = assignees,
    };

    public static async Task<CreatedTasksDto> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Base, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedTasksDto>())!;
    }

    public static async Task<TaskDto> GetAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<TaskDto>($"{Base}/{id}"))!;

    public static Task<HttpResponseMessage> ActAsync(HttpClient client, TaskDto task, string action, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["version"] = task.Version };
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties())
            {
                body[property.Name] = property.GetValue(extra);
            }
        }

        return client.PostAsJsonAsync($"{Base}/{task.Id}/{action}", body);
    }

    public static async Task<TaskDto> ActOkAsync(HttpClient client, Guid id, string action, object? extra = null)
    {
        var task = await GetAsync(client, id);
        var response = await ActAsync(client, task, action, extra);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{action}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    public static async Task<PagedResult<TaskListItemDto>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<TaskListItemDto>>($"{Base}?{query}"))!;
}
