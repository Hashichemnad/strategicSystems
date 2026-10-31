namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>One host + fresh database shared by the tests of a class (tests use unique data, never global counts).</summary>
public sealed class ApiFixture(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HttpClient? _userClient;
    private HttpClient? _adminClient;

    public ApiFactory Factory { get; private set; } = null!;

    public PostgresFixture Postgres => postgres;

    /// <summary>One signed-in regular user per test class (tests that change the user's own state create their own).</summary>
    public Task<HttpClient> UserClientAsync() => CachedAsync(() => _userClient, c => _userClient = c, admin: false);

    /// <summary>One signed-in system administrator (with MFA) per test class.</summary>
    public Task<HttpClient> AdminClientAsync() => CachedAsync(() => _adminClient, c => _adminClient = c, admin: true);

    private readonly Dictionary<string, HttpClient> _permissionClients = new(StringComparer.Ordinal);

    /// <summary>A signed-in user whose only grant (besides the defaults for everyone) is a role holding exactly <paramref name="permission"/>.</summary>
    public async Task<HttpClient> ClientWithOnlyAsync(string permission)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_permissionClients.TryGetValue(permission, out var client))
            {
                var system = permission[..permission.IndexOf('.', StringComparison.Ordinal)];
                var role = await Factory.CreateRoleAsync(system, (permission, Strategy.Platform.Access.DataScope.All));
                client = await Factory.SignInAsync(await Factory.CreateUserAsync(roles: role));
                _permissionClients[permission] = client;
            }

            return client;
        }
        finally
        {
            _gate.Release();
        }
    }

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
