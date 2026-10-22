namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>One host + fresh database shared by the tests of a class (tests use unique data, never global counts).</summary>
public sealed class ApiFixture(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HttpClient? _userClient;
    private HttpClient? _adminClient;

    public ApiFactory Factory { get; private set; } = null!;

    /// <summary>One signed-in regular user per test class (tests that change the user's own state create their own).</summary>
    public Task<HttpClient> UserClientAsync() => CachedAsync(() => _userClient, c => _userClient = c, admin: false);

    /// <summary>One signed-in system administrator (with MFA) per test class.</summary>
    public Task<HttpClient> AdminClientAsync() => CachedAsync(() => _adminClient, c => _adminClient = c, admin: true);

    private async Task<HttpClient> CachedAsync(Func<HttpClient?> get, Action<HttpClient> set, bool admin)
    {
        await _gate.WaitAsync();
        try
        {
            if (get() is { } existing)
            {
                return existing;
            }

            var client = await Factory.CreateSignedInClientAsync(admin);
            set(client);
            return client;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask InitializeAsync() => Factory = await ApiFactory.CreateAsync(postgres);

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return Factory.DisposeAsync();
    }
}
