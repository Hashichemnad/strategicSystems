using Npgsql;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.Platform;

public class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_apply_from_an_empty_database_into_module_schemas()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select table_schema || '.' || table_name from information_schema.tables where table_schema = 'sample' order by 1",
            connection);
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.ShouldBe(["sample.__ef_migrations_history", "sample.sample_items"]);
        await using var publicTables = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema = 'public'", connection);
        ((long)(await publicTables.ExecuteScalarAsync())!).ShouldBe(0);
    }
}
