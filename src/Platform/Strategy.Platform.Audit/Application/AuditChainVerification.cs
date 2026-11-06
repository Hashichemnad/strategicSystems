namespace Strategy.Platform.Audit.Application;

/// <summary>Outcome of a full walk of the audit hash chain (host command <c>--verify-audit-chain</c>, restore verification).</summary>
public sealed record AuditChainVerificationResult(bool Ok, long EntriesChecked, long LastSeq, long? BrokenSeq, string? MessageKey);

/// <summary>
/// Full verification of the audit hash chain for operations tools (restore check, ADR 0157). The run is recorded like a run started from
/// the verification screen; a broken chain alerts the holders of <c>audit.chain.verify</c>.
/// </summary>
public interface IAuditChainVerification
{
    Task<AuditChainVerificationResult> VerifyFullAsync(CancellationToken cancellationToken = default);
}

internal sealed class AuditChainVerification(AuditVerifier verifier, ChainAlert alert) : IAuditChainVerification
{
    public async Task<AuditChainVerificationResult> VerifyFullAsync(CancellationToken cancellationToken = default)
    {
        var run = await verifier.VerifyAsync(VerificationModes.Full, cancellationToken).ConfigureAwait(false);
        await alert.RaiseIfBrokenAsync(run, cancellationToken).ConfigureAwait(false);
        return new AuditChainVerificationResult(run.Ok, run.EntriesChecked, run.LastSeq, run.BrokenSeq, run.MessageKey);
    }
}
