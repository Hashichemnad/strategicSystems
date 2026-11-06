using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Strategy.IntegrationTests.Infrastructure.PostgresFixture))]

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL server per test run (Testcontainers), one fresh database per <see cref="ApiFactory"/>,
/// so every factory starts from an empty database and runs the migrations itself.
/// Set <c>STRATEGY_TEST_POSTGRES</c> to an admin connection string to reuse an existing server instead of Docker.
/// Behind a Docker Hub rate limit, set <c>TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX</c> (e.g. <c>mirror.gcr.io/</c>).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:16-alpine";

    private PostgreSqlContainer? _container;
    private readonly System.Collections.Concurrent.ConcurrentBag<string> _created = [];
    private string _adminConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("STRATEGY_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            return;
        }

        // Test classes run in parallel, each with its own host and connection pool: room for all of them (the default of 100 runs out).
        _container = new PostgreSqlBuilder(Image).WithCommand("-c", "max_connections=400").Build();
        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();
    }

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "strategy_test_" + Guid.CreateVersion7().ToString("N");
        _created.Add(name);
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        // A reused server (STRATEGY_TEST_POSTGRES) keeps what it is given: drop this run's databases, or every run adds a few gigabytes. Every host
        // of the run has stopped by now (assembly fixture); FORCE closes connections a pool may still hold.
        if (_adminConnectionString.Length == 0 || _created.IsEmpty)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        foreach (var name in _created)
        {
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (PostgresException)
            {
                // Best effort: a database that cannot be dropped now is left for the server's administrator.
            }
        }
    }
}
