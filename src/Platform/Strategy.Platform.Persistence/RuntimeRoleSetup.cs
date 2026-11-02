using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Strategy.Platform.Persistence;

/// <summary>Outcome of <see cref="RuntimeRoleSetup.EnsureRoleAsync"/>.</summary>
public enum RuntimeRoleOutcome
{
    Created,
    Updated,
    NotConfigured,
    SameAsOwner,
}

/// <summary>
/// Upgrade step for installations that still run as the owner role (ADR 0018, <c>--setup-db-roles</c>): creates the runtime role named in
/// <c>ConnectionStrings:Strategy</c> (or updates its password), lets it connect, and leaves the schema grants to the migrator's
/// <see cref="RuntimeRoleGrants"/> seeder, so the role gets read / write on module schemas and only SELECT + INSERT on the audit trail.
/// Connects as the owner (<c>ConnectionStrings:StrategyMigrations</c>), which needs the CREATEROLE privilege.
/// </summary>
public sealed class RuntimeRoleSetup(IConfiguration configuration, NpgsqlDataSource ownerDataSource)
{
    public async Task<RuntimeRoleOutcome> EnsureRoleAsync(CancellationToken cancellationToken = default)
    {
        var runtime = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        var owner = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.MigrationsConnectionStringName);
        if (string.IsNullOrWhiteSpace(runtime) || string.IsNullOrWhiteSpace(owner))
        {
            return RuntimeRoleOutcome.NotConfigured;
        }

        var runtimeBuilder = new NpgsqlConnectionStringBuilder(runtime);
        var ownerBuilder = new NpgsqlConnectionStringBuilder(owner);
        if (string.IsNullOrEmpty(runtimeBuilder.Username) || string.IsNullOrEmpty(runtimeBuilder.Password))
        {
            return RuntimeRoleOutcome.NotConfigured;
        }

        if (string.Equals(runtimeBuilder.Username, ownerBuilder.Username, StringComparison.Ordinal))
        {
            return RuntimeRoleOutcome.SameAsOwner;
        }

        await using var connection = await ownerDataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool exists;
        await using (var check = new NpgsqlCommand("select exists (select 1 from pg_roles where rolname = @role)", connection, transaction))
        {
            check.Parameters.AddWithValue("role", runtimeBuilder.Username);
            exists = (bool)(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        // format(%I, %L) quotes the name and password on the server; nothing is concatenated into SQL here.
        var statement = exists
            ? "select format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', @role, @password)"
            : "select format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', @role, @password)";
        await ExecuteFormattedAsync(connection, transaction, statement, runtimeBuilder.Username, runtimeBuilder.Password, cancellationToken)
            .ConfigureAwait(false);
        await ExecuteFormattedAsync(connection, transaction, "select format('GRANT CONNECT, TEMPORARY ON DATABASE %I TO %I', current_database(), @role)",
            runtimeBuilder.Username, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return exists ? RuntimeRoleOutcome.Updated : RuntimeRoleOutcome.Created;
    }

    private static async Task ExecuteFormattedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string formatQuery, string role, string? password, CancellationToken ct)
    {
        string sql;
        await using (var format = new NpgsqlCommand(formatQuery, connection, transaction))
        {
            format.Parameters.AddWithValue("role", role);
            if (password is not null)
            {
                format.Parameters.AddWithValue("password", password);
            }

            sql = (string)(await format.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
