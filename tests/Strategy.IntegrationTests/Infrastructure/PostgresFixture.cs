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
    private string _adminConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("STRATEGY_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            return;
        }

        _container = new PostgreSqlBuilder(Image).Build();
        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();
    }

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "strategy_test_" + Guid.CreateVersion7().ToString("N");
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
        }
    }
}
