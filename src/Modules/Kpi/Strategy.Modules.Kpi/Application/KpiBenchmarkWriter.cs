using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Benchmark fields of a card from an approved benchmarking analysis (ADR 0128), through EF (audited).</summary>
internal sealed class KpiBenchmarkWriter(KpiDbContext db) : IKpiBenchmarkWriter
{
    public async Task<bool> UpdateBenchmarkAsync(Guid cardId, string? description, decimal? result, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        if (transactionContext is not null)
        {
            await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        }

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return false;
        }

        card.SetBenchmark(description, result);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
