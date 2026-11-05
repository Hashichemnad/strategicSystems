using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Kpi.Application;

/// <summary>Machine drafts from other modules (ADR 0135): the same draft path as MCP (<c>DraftFromMcp</c>), as the system, channel job.</summary>
internal sealed class KpiResultFeed(IServiceScopeFactory scopes) : IKpiResultFeed
{
    public async Task<string> ProposeAsync(Guid cardId, DateOnly asOf, decimal achieve, string? note, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        var db = services.GetRequiredService<KpiDbContext>();
        var result = await db.Results.AsNoTracking()
            .Where(r => r.CardId == cardId && r.PeriodEnd <= asOf && (r.Status == ResultStatuses.Open || r.Status == ResultStatuses.Returned))
            .OrderByDescending(r => r.PeriodEnd).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (result is null || !ResultStatuses.IsOpenForUpdate(result.Status))
        {
            return KpiFeedOutcomes.NoOpenResult;
        }

        if (result.Achieve == achieve && result.DraftFromMcp)
        {
            return KpiFeedOutcomes.Unchanged;
        }

        var saved = await services.GetRequiredService<IDispatcher>().SendAsync(
            new SaveKpiResultDraft(result.Id, new KpiEntryInput(achieve, null, null, null, null, note, false, null), result.Version, FromMcp: true), cancellationToken)
            .ConfigureAwait(false);
        return saved.IsSuccess ? KpiFeedOutcomes.Drafted : KpiFeedOutcomes.Failed;
    }
}
