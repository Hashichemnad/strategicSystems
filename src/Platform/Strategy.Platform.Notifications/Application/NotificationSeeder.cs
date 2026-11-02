using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Notifications.Domain;
using Strategy.Platform.Notifications.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Notifications.Application;

/// <summary>Inserts missing templates; keeps names in step with the code and the text too while it is not customised.</summary>
internal sealed class NotificationSeeder : IDatabaseSeeder
{
    public int Order => 30;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<NotifyDbContext>();
        var existing = await db.Templates.ToDictionaryAsync(t => t.Key, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        foreach (var definition in services.GetServices<NotificationTemplateDefinition>().DistinctBy(d => d.Key))
        {
            if (existing.TryGetValue(definition.Key, out var template))
            {
                template.Sync(definition);
            }
            else
            {
                db.Templates.Add(NotificationTemplate.Create(definition));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
