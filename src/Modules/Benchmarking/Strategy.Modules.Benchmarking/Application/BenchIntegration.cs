using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;

namespace Strategy.Modules.Benchmarking.Application;

/// <summary>
/// CAP reports remark changes of the study's source (ADR 0128): when every linked action is completed with evidence the coordinator and the office are
/// told once that the study is ready for closure. Idempotent (stamp).
/// </summary>
internal sealed class CapRemarkStatusChangedHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<CapRemarkStatusChanged>
{
    public async Task HandleAsync(CapRemarkStatusChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SourceLinkedType != Study.EntityType || integrationEvent.SourceLinkedId is not { } studyId)
        {
            return;
        }

        await using var scope = Jobs.BenchJobScope.Create(scopes);
        await ReadyCheck.RunAsync(scope.ServiceProvider, studyId, cancellationToken).ConfigureAwait(false);
    }
}

internal static class ReadyCheck
{
    /// <summary>Notifies once when the study's actions are all completed with evidence; true when notified now.</summary>
    public static async Task<bool> RunAsync(IServiceProvider services, Guid studyId, CancellationToken ct)
    {
        var db = services.GetRequiredService<BenchDbContext>();
        var study = await db.Studies.FirstOrDefaultAsync(s => s.Id == studyId, ct).ConfigureAwait(false);
        if (study is null || !study.IsActive || !study.AnalysisApproved || study.ReadyNotifiedAt is not null
            || !await BenchClosure.ReadyAsync(services, study.Id, ct).ConfigureAwait(false))
        {
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        study.MarkReadyNotified(services.GetRequiredService<IClock>().UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await services.GetRequiredService<BenchNotifier>().SendAsync(BenchNotifications.ReadyForClosure, [study.ResponsibleEmployeeId], BenchNotifier.Values(study),
            BenchNotifier.Link(study, "actions"), db, ct, [BenchPermissions.OfficeRoleKey]).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }
}

/// <summary>Studies are task-able, team-able (benchmarking team, SPEC §8.3) and CAP remark sources: badge = number, title, stage.</summary>
internal sealed class StudyLinkTargets(BenchReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        Study.EntityType, BenchPermissions.SystemCode, "دراسة مقارنة معيارية", "Benchmarking study",
        [LinkCapabilities.TaskAble, LinkCapabilities.TeamAble, LinkCapabilities.RemarkSource], BenchPermissions.Read, "/bench/studies/{id}", BenchPermissions.Edit);

    public string ResourceType => Study.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false)).Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(s => s.Id, s => new LinkTarget(s.Id, s.Number, s.TitleAr, s.TitleEn, s.Status == StudyStatuses.Active ? s.Stage : s.Status, null, null,
            s.Status switch
            {
                StudyStatuses.Completed => "success",
                StudyStatuses.Cancelled => "neutral",
                _ => "info",
            }, s.Status != StudyStatuses.Active));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.StudyAsync(id, user, cancellationToken).ConfigureAwait(false) is { } study && await reader.CanAsync(user, permission, study, cancellationToken).ConfigureAwait(false);
}

/// <summary>Study documents (questionnaire, visit report, analysis): read with the study, written by whoever may fill its forms.</summary>
internal sealed class StudyFiles(BenchReader reader, BenchViews views) : IFileOwnerAccess
{
    public string OwnerType => Study.EntityType;

    public string AttachmentPurpose => BenchFilePurposes.StudyDocument.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.StudyAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id) || await reader.StudyAsync(id, user, cancellationToken).ConfigureAwait(false) is not { } study || !study.IsActive)
        {
            return false;
        }

        var can = await views.PermissionsAsync(study, user, cancellationToken).ConfigureAwait(false);
        return can.Office || can.EditVisits || can.EditAnalysis || can.EditSearch || can.Outreach;
    }
}

/// <summary>Global search over studies (number, titles), scope-filtered in the query.</summary>
internal sealed class StudySearchSource(BenchReader reader) : IRecordSearchSource
{
    public string ResourceType => Study.EntityType;

    public string SystemCode => BenchPermissions.SystemCode;

    public string ReadPermission => BenchPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var rows = await (await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(s => EF.Functions.ILike(s.Number, pattern, "\\") || EF.Functions.ILike(s.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(s.TitleEn ?? string.Empty, pattern, "\\"))
            .OrderBy(s => s.Number).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(s => new RecordSearchHit(Study.EntityType, s.Id, BenchPermissions.SystemCode, s.Number, s.TitleAr, s.TitleEn, s.Stage, BenchNotifier.Link(s), 1))
            .ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var hits = await (await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(s => s.Id != excludeId)
            .Select(s => new
            {
                Study = s,
                Score = Math.Max(EF.Functions.TrigramsSimilarity(s.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(s.TitleAr ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return hits.Select(h => new RecordSearchHit(Study.EntityType, h.Study.Id, BenchPermissions.SystemCode, h.Study.Number, h.Study.TitleAr, h.Study.TitleEn, h.Study.Stage,
            BenchNotifier.Link(h.Study), h.Score)).ToList();
    }
}
