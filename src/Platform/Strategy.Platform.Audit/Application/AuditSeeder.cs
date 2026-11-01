using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Platform.Audit.Application;

/// <summary>
/// Carries records staged by migrations (Phase 1 sign-in events) into the chain, oldest first, and empties the staging table in the
/// same transaction. Runs first, so carried-over history precedes everything written by later seeders.
/// </summary>
internal sealed class AuditSeeder : IDatabaseSeeder
{
    public int Order => 1;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AuditDbContext>();
        var sink = services.GetRequiredService<IAuditSink>();
        while (true)
        {
            var batch = await db.LegacyImports.AsNoTracking().OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Take(1000).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var records = batch.Select(x => new AuditRecord(AuditKinds.Event, x.Action, x.SystemCode, x.EntityType, x.RecordId,
                Success: x.Success, Data: x.Data, OccurredAt: x.OccurredAt)
            {
                ActorId = x.UserId,
                ActorName = x.UserName,
                Channel = x.Channel,
                CorrelationId = x.CorrelationId,
                IpAddress = x.IpAddress,
                UserAgent = x.UserAgent,
            }).ToList();
            await sink.AppendAsync(db.Database.GetDbConnection(), transaction.GetDbTransaction(), records, cancellationToken).ConfigureAwait(false);
            var ids = batch.Select(x => x.Id).ToArray();
            await db.LegacyImports.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
