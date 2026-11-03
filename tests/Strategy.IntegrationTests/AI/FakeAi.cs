using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Providers;

namespace Strategy.IntegrationTests.AI;

/// <summary>
/// A programmable model: no provider is ever called in CI. Tests set <see cref="Responder"/> and read <see cref="Calls"/> (what was sent).
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Lock _gate = new();

    public Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>? Responder { get; set; }

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public static ChatResponse Text(string text, long input = 100, long output = 20) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output } };

    public static ChatResponse Json(object value) => Text(JsonSerializer.Serialize(value));

    public void Reset(Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>? responder = null)
    {
        lock (_gate)
        {
            Calls.Clear();
            Responder = responder;
        }
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        lock (_gate)
        {
            Calls.Add((list, options));
        }

        return Task.FromResult(Responder?.Invoke(list, options) ?? Text("OK"));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Provider type <c>fake</c>, registered only in tests.</summary>
public sealed class FakeAdapter(FakeChatClient client) : IAiProviderAdapter
{
    public const string TypeCode = "fake";

    public string Type => TypeCode;

    public bool RequiresEndpoint => false;

    public bool RequiresApiKey => false;

    public ProviderConnection? LastConnection { get; private set; }

    public IChatClient Create(ProviderConnection connection)
    {
        LastConnection = connection;
        return client;
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(ProviderConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["fake-small", "fake-large"]);
}

/// <summary>A host with the fake provider, shared by the tests of a class (tests run one after another within a class).</summary>
public sealed class AiFixture(PostgresFixture postgres) : IAsyncLifetime
{
    private HttpClient? _admin;

    public ApiFactory Factory { get; private set; } = null!;

    public FakeChatClient Model { get; } = new();

    public FakeAdapter Adapter => (FakeAdapter)Factory.Services.GetServices<IAiProviderAdapter>().Single(a => a.Type == FakeAdapter.TypeCode);

    public async ValueTask InitializeAsync() =>
        Factory = await ApiFactory.CreateAsync(postgres, configureServices: services =>
        {
            services.AddSingleton(Model);
            services.AddSingleton<IAiProviderAdapter, FakeAdapter>();
        });

    public async Task<HttpClient> AdminAsync() => _admin ??= await Factory.CreateSignedInClientAsync(admin: true);

    /// <summary>Creates a fake provider and switches AI on with it as the default; <paramref name="change"/> adjusts the settings.</summary>
    public async Task<Guid> EnableAsync(Func<AiSettings, AiSettings>? change = null)
    {
        var admin = await AdminAsync();
        var created = await admin.PostAsJsonAsync("/api/v1/ai/providers", new { name = "Fake " + Guid.CreateVersion7(), type = FakeAdapter.TypeCode, defaultModel = "fake-small" });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var provider = (await created.Content.ReadFromJsonAsync<AiProviderDto>())!;
        await SaveSettingsAsync(s => (change ?? (x => x))(new AiSettings(Enabled: true, DefaultProviderId: provider.Id)));
        return provider.Id;
    }

    public async Task SaveSettingsAsync(Func<AiSettings, AiSettings> change)
    {
        var admin = await AdminAsync();
        var current = (await admin.GetFromJsonAsync<AiSettingsDto>("/api/v1/ai/settings"))!;
        var saved = await admin.PutAsJsonAsync("/api/v1/ai/settings", new AiSettingsInput(change(current.Settings), current.Version));
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}
