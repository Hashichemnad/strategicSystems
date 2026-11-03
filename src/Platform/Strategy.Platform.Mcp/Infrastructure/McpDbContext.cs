using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Mcp.Infrastructure;

/// <summary>Schema <c>mcp</c>: OpenIddict applications (OAuth clients), authorizations, scopes and tokens (ADR 0032).</summary>
public sealed class McpDbContext(DbContextOptions<McpDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "mcp";

    public override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
    }
}
