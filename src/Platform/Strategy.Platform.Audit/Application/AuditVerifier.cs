using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit.Domain;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Time;

namespace Strategy.Platform.Audit.Application;

public static class VerificationModes
{
    public const string Full = "full";
    public const string Incremental = "incremental";
}

/// <summary>Why a chain check failed (translatable keys under <c>audit.verify.*</c>).</summary>
public static class VerificationMessages
{
    public const string HashMismatch = "audit.verify.hashMismatch";
    public const string LinkBroken = "audit.verify.linkBroken";
    public const string MissingEntry = "audit.verify.missingEntry";
    public const string Truncated = "audit.verify.truncated";
    public const string CheckpointChanged = "audit.verify.checkpointChanged";
}

/// <summary>Result of walking a sequence of entries.</summary>
public sealed record ChainCheck(bool Ok, long Checked, long LastSeq, byte[] LastHash, long? BrokenSeq, string? MessageKey);

/// <summary>
/// Recomputes the chain (ADR 0018): consecutive <c>seq</c>, each <c>prev_hash</c> equal to the previous row's hash, each hash equal to
/// the recomputed one, and the last checkpoint still present. Records the run and, when the chain is intact, moves the checkpoint.
/// </summary>
internal sealed class AuditVerifier(AuditDbContext db, IClock clock, IRequestContext context, IAuditLog log)
{
    private const int BatchSize = 5000;

    /// <summary>Pure check of consecutive entries, starting after <paramref name="startSeq"/> whose hash is <paramref name="startHash"/>.</summary>
    public static ChainCheck Check(IEnumerable<AuditEntry> entries, long startSeq, byte[] startHash)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var expectedSeq = startSeq + 1;
        var prev = startHash;
        long count = 0;
        foreach (var entry in entries)
        {
            if (entry.Seq != expectedSeq)
            {
                return new ChainCheck(false, count, expectedSeq - 1, prev, expectedSeq, VerificationMessages.MissingEntry);
            }

            if (!entry.PrevHash.AsSpan().SequenceEqual(prev))
            {
                return new ChainCheck(false, count, expectedSeq - 1, prev, entry.Seq, VerificationMessages.LinkBroken);
            }

            if (!AuditCanonical.ComputeHash(prev, entry).AsSpan().SequenceEqual(entry.Hash))
            {
                return new ChainCheck(false, count, expectedSeq - 1, prev, entry.Seq, VerificationMessages.HashMismatch);
            }

            prev = entry.Hash;
            expectedSeq++;
            count++;
        }

        return new ChainCheck(true, count, expectedSeq - 1, prev, null, null);
    }

    public async Task<VerificationRun> VerifyAsync(string mode, CancellationToken cancellationToken)
    {
        var run = new VerificationRun
        {
            StartedAt = clock.UtcNow,
            Mode = mode,
            TriggeredBy = context.UserId,
            Channel = context.Channel,
        };

        var checkpoint = await db.Checkpoints.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var maxSeq = await db.Entries.MaxAsync(e => (long?)e.Seq, cancellationToken).ConfigureAwait(false) ?? 0;
        ChainCheck result;
        if (checkpoint is not null && maxSeq < checkpoint.Seq)
        {
            result = new ChainCheck(false, 0, maxSeq, [], maxSeq + 1, VerificationMessages.Truncated);
        }
        else
        {
            long startSeq = 0;
            var startHash = AuditCanonical.Genesis;
            if (mode == VerificationModes.Incremental && checkpoint is not null)
            {
                var anchor = await db.Entries.AsNoTracking().Where(e => e.Seq == checkpoint.Seq).Select(e => e.Hash)
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (anchor is null || !anchor.AsSpan().SequenceEqual(checkpoint.Hash))
                {
                    result = new ChainCheck(false, 0, checkpoint.Seq, [], checkpoint.Seq, VerificationMessages.CheckpointChanged);
                    return await FinishAsync(run, result, 0, cancellationToken).ConfigureAwait(false);
                }

                startSeq = checkpoint.Seq;
                startHash = checkpoint.Hash;
            }

            result = await WalkAsync(startSeq, startHash, cancellationToken).ConfigureAwait(false);
            if (result.Ok && mode == VerificationModes.Full && checkpoint is not null && result.LastSeq >= checkpoint.Seq)
            {
                // The checkpointed row must still carry the hash seen at the last good run.
                var hash = await db.Entries.AsNoTracking().Where(e => e.Seq == checkpoint.Seq).Select(e => e.Hash).FirstAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!hash.AsSpan().SequenceEqual(checkpoint.Hash))
                {
                    result = result with { Ok = false, BrokenSeq = checkpoint.Seq, MessageKey = VerificationMessages.CheckpointChanged };
                }
            }

            return await FinishAsync(run, result, startSeq, cancellationToken).ConfigureAwait(false);
        }

        return await FinishAsync(run, result, 0, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChainCheck> WalkAsync(long startSeq, byte[] startHash, CancellationToken ct)
    {
        long total = 0;
        var seq = startSeq;
        var hash = startHash;
        while (true)
        {
            var batch = await db.Entries.AsNoTracking().Where(e => e.Seq > seq).OrderBy(e => e.Seq).Take(BatchSize).ToListAsync(ct).ConfigureAwait(false);
            var result = Check(batch, seq, hash);
            total += result.Checked;
            if (!result.Ok || batch.Count < BatchSize)
            {
                return result with { Checked = total };
            }

            seq = result.LastSeq;
            hash = result.LastHash;
        }
    }

    private async Task<VerificationRun> FinishAsync(VerificationRun run, ChainCheck result, long fromSeq, CancellationToken ct)
    {
        run.FinishedAt = clock.UtcNow;
        run.FromSeq = fromSeq;
        run.EntriesChecked = result.Checked;
        run.LastSeq = result.LastSeq;
        run.Ok = result.Ok;
        run.BrokenSeq = result.BrokenSeq;
        run.MessageKey = result.MessageKey;
        db.VerificationRuns.Add(run);
        if (result.Ok && result.LastSeq > 0)
        {
            var checkpoint = await db.Checkpoints.FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (checkpoint is null)
            {
                db.Checkpoints.Add(checkpoint = new ChainCheckpoint());
            }

            checkpoint.Seq = result.LastSeq;
            checkpoint.Hash = result.LastHash;
            checkpoint.VerifiedAt = run.FinishedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await log.WriteAsync(new AuditEvent(AuditActions.ChainVerified, result.Ok, AuditComponent.SystemCode, "audit.verification_run", run.Id.ToString("D"),
            new { run.Mode, run.EntriesChecked, run.LastSeq, run.BrokenSeq, run.MessageKey }), ct).ConfigureAwait(false);
        return run;
    }
}
