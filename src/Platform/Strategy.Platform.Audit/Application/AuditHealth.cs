using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Http.Health;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Audit.Application;

/// <summary>
/// Health page (ADR 0151): result of the last audit hash-chain verification (job <c>audit.verify-chain</c> or a manual run). A broken chain is
/// an error; no verification within 48 hours a warning (the job runs daily). The page does not verify itself (that can take minutes).
/// </summary>
internal sealed class AuditHealthContributor(AuditDbContext db, IClock clock) : IAdminHealthContributor
{
    public static readonly TimeSpan Stale = TimeSpan.FromHours(48);

    public string Code => "auditChain";

    public string Group => AdminHealthGroups.Security;

    public int Order => 50;

    public async Task<IReadOnlyList<AdminHealthCheck>> CheckAsync(CancellationToken cancellationToken)
    {
        var last = await db.VerificationRuns.AsNoTracking().OrderByDescending(r => r.FinishedAt)
            .Select(r => new { r.FinishedAt, r.Ok, r.Mode, r.EntriesChecked, r.LastSeq, r.BrokenSeq, r.MessageKey })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (last is null)
        {
            return [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Warning, "health.auditChain.never")];
        }

        var parameters = HealthParams.Of(("at", last.FinishedAt), ("mode", last.Mode), ("checked", last.EntriesChecked), ("lastSeq", last.LastSeq),
            ("brokenSeq", last.BrokenSeq), ("reason", last.MessageKey));
        return !last.Ok ? [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Error, "health.auditChain.broken", parameters)]
            : clock.UtcNow - last.FinishedAt > Stale ? [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Warning, "health.auditChain.stale", parameters)]
            : [new AdminHealthCheck(Code, Group, AdminHealthStatuses.Ok, "health.auditChain.ok", parameters)];
    }
}
