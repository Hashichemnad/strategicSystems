using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Platform.Persistence;

/// <summary>
/// Base for every module's DbContext: one PostgreSQL schema per module, configurations from the module's
/// assembly, <c>xmin</c> concurrency on auditable entities. Naming (snake_case) and the migrations history
/// table are set at registration (<see cref="PersistenceServiceCollectionExtensions.AddModuleDbContext{TContext}"/>).
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>The module's schema, e.g. <c>portfolio</c>. Never shared with another module.</summary>
    public abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Ignore(nameof(Entity.DomainEvents));

            if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                // Npgsql maps a uint row version to the PostgreSQL xmin system column.
                entity.Property(nameof(AuditableEntity.Version)).IsRowVersion();
            }
        }
    }
}
