using Microsoft.EntityFrameworkCore;
using Strategy.Modules.DocumentRegistry.Domain;
using Strategy.Modules.DocumentRegistry.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Documents;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.DocumentRegistry.Application;

public sealed record DocUnitDto(Guid Id, string Code, string? NameAr, string? NameEn);

public sealed record DocPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record DocumentListItemDto(
    Guid Id,
    string? DocumentNo,
    string? NameAr,
    string? NameEn,
    string? Level,
    string? DocType,
    string? Sensitivity,
    DocUnitDto? OwnerUnit,
    string Status,
    string? CurrentVersion,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    bool ExpiringSoon,
    bool ShareToAll,
    bool UnderRevision,
    DateTimeOffset? PublishedAt);

public sealed record FacetDto(string Code, int Count);

public sealed record UnitFacetDto(Guid Id, string? NameAr, string? NameEn, int Count);

public sealed record LibraryDto(PagedResult<DocumentListItemDto> Page, IReadOnlyList<FacetDto> Levels, IReadOnlyList<FacetDto> Types, IReadOnlyList<UnitFacetDto> Units);

public sealed record VersionDto(
    Guid Id,
    string? Label,
    string Status,
    string? ChangeSummary,
    Guid RequestId,
    string? RequestNo,
    DocPersonDto? RequestedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? PublishedAt,
    Guid? FinalFileId,
    string? FinalFileName,
    bool HasText,
    string? SummaryAr,
    string? SummaryEn);

public sealed record ValidityEventDto(string Kind, int? Days, DateOnly? OldExpiry, DateOnly? NewExpiry, string? Comment, DateTimeOffset At);

public sealed record DocumentCapabilities(bool ConfirmValid, bool NewVersion, bool Withdraw, bool EditExpiry, bool Acknowledge, bool Download, bool Manage);

public sealed record AckDto(bool Required, bool Mine, int Count);

public sealed record DocumentDto(
    Guid Id,
    string? DocumentNo,
    string? NameAr,
    string? NameEn,
    string? Description,
    string? Level,
    string? DocType,
    string? Sensitivity,
    DocUnitDto? OwnerUnit,
    DocPersonDto? Owner,
    string Status,
    string? CurrentVersion,
    Guid? CurrentVersionId,
    DateOnly? EffectiveDate,
    DateOnly? ExpiryDate,
    int? ValidityMonths,
    bool ShareToAll,
    bool UnderRevision,
    string? WithdrawnReason,
    IReadOnlyList<Platform.Links.LinkView> Linked,
    IReadOnlyList<VersionDto> Versions,
    IReadOnlyList<ValidityEventDto> Validity,
    AckDto Acknowledgements,
    DocumentCapabilities Can,
    uint Version);

public sealed record CompareDto(Guid FromId, string? FromLabel, Guid ToId, string? ToLabel, IReadOnlyList<DiffPart> Parts, int Added, int Removed);

public sealed record ConfirmInput(string? Comment);

public sealed record ExpiryInput(DateOnly? ExpiryDate, string? Comment, uint Version);

public sealed record DownloadFile(byte[] Content, string ContentType, string FileName, bool Stamped);

public sealed record DashboardDto(
    int Published, int Expired, int ExpiringSoon, int PendingReview, int AwaitingPublication, IReadOnlyList<FacetDto> ByLevel, IReadOnlyList<FacetDto> ByType,
    IReadOnlyList<UnitFacetDto> ByUnit, IReadOnlyList<DocumentListItemDto> Expiring, IReadOnlyList<DocumentListItemDto> RecentlyPublished);

public sealed record DocRegSettingsInput(DocRegSettings Settings, uint Version);

/// <summary>Library, document page, versions, compare, downloads, validity actions, acknowledgements and dashboard — all through <see cref="DocumentVisibility"/>.</summary>
internal sealed class DocumentService(
    DocRegDbContext db,
    DocumentVisibility visibility,
    DocRegCalendar calendar,
    ISettingsStore settings,
    ISettingsEditor editor,
    IFileService files,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    Watermark watermark,
    Platform.Links.ILinkStore linkStore,
    Platform.Links.ILinkRegistry links,
    IRequestContext context,
    IClock clock)
{
    public const int SoonDays = 90;

    public async Task<LibraryDto> LibraryAsync(PageRequest page, string? level, string? type, Guid? unitId, string? sensitivity, string? status, int? expiringWithin,
        string? search, CancellationToken ct)
    {
        var visible = await visibility.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var active = visible.Where(d => d.Status == DocumentStatuses.Published || d.Status == DocumentStatuses.Expired);
        var query = active;
        if (status is not null)
        {
            query = query.Where(d => d.Status == status);
        }

        if (level is not null)
        {
            query = query.Where(d => d.Level == level);
        }

        if (type is not null)
        {
            query = query.Where(d => d.DocType == type);
        }

        if (unitId is not null)
        {
            query = query.Where(d => d.OwnerUnitId == unitId);
        }

        if (sensitivity is not null)
        {
            query = query.Where(d => d.Sensitivity == sensitivity);
        }

        var today = await calendar.TodayAsync(ct).ConfigureAwait(false);
        if (expiringWithin is > 0)
        {
            var until = today.AddDays(expiringWithin.Value);
            query = query.Where(d => d.ExpiryDate != null && d.ExpiryDate <= until);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = "%" + SearchText.Normalise(search) + "%";
            query = query.Where(d => (d.SearchText != null && EF.Functions.ILike(d.SearchText, term)) || (d.FileSearchText != null && EF.Functions.ILike(d.FileSearchText, term)));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderBy(d => d.NameEn ?? d.NameAr).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        var levels = await active.GroupBy(d => d.Level).Select(g => new FacetDto(g.Key ?? string.Empty, g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var types = await active.GroupBy(d => d.DocType).Select(g => new FacetDto(g.Key ?? string.Empty, g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var units = await active.Where(d => d.OwnerUnitId != null).GroupBy(d => d.OwnerUnitId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);
        var unitFacets = new List<UnitFacetDto>();
        foreach (var u in units)
        {
            var unit = await org.GetUnitAsync(u.Key, ct).ConfigureAwait(false);
            unitFacets.Add(new UnitFacetDto(u.Key, unit?.NameAr, unit?.NameEn, u.Count));
        }

        return new LibraryDto(new PagedResult<DocumentListItemDto>(await ItemsAsync(rows, today, ct).ConfigureAwait(false), page.Page, page.PageSize, total), levels, types,
            unitFacets);
    }

    public async Task<IReadOnlyList<DocumentListItemDto>> ItemsAsync(IReadOnlyList<Document> rows, DateOnly today, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var revising = await db.Versions.AsNoTracking().Where(v => ids.Contains(v.DocumentId) && !v.IsNewDocument
                && (v.Status == VersionStatuses.Submitted || v.Status == VersionStatuses.UnderReview || v.Status == VersionStatuses.Returned || v.Status == VersionStatuses.Approved))
            .Select(v => v.DocumentId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var list = new List<DocumentListItemDto>();
        foreach (var d in rows)
        {
            list.Add(new DocumentListItemDto(d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Level, d.DocType, d.Sensitivity, await UnitAsync(d.OwnerUnitId, ct).ConfigureAwait(false),
                d.Status, d.CurrentVersionLabel, d.EffectiveDate, d.ExpiryDate, d.ExpiryDate is { } e && e.DayNumber - today.DayNumber <= SoonDays && d.Status == DocumentStatuses.Published,
                d.ShareToAll, revising.Contains(d.Id), d.PublishedAt));
        }

        return list;
    }

    public async Task<Result<DocumentDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var document = await visibility.FindAsync(id, context.User, ct).ConfigureAwait(false);
        return document is null ? Error.NotFound() : await ToDtoAsync(document, ct).ConfigureAwait(false);
    }

    public async Task<DocumentDto> ToDtoAsync(Document d, CancellationToken ct)
    {
        var user = context.User;
        var clearance = await visibility.ClearanceAsync(user, ct).ConfigureAwait(false);
        var versions = await visibility.VersionsAsync(d, user, ct).ConfigureAwait(false);
        var people = await employees.GetManyAsync(versions.Where(v => v.RequestedByEmployeeId is not null).Select(v => v.RequestedByEmployeeId!.Value)
            .Append(d.OwnerEmployeeId ?? Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList(), ct).ConfigureAwait(false);
        DocPersonDto? Person(Guid? e) => e is { } k && people.TryGetValue(k, out var p) ? new DocPersonDto(p.Id, p.NameAr ?? p.NameEn, p.NameEn ?? p.NameAr) : null;
        var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
        var ownerSide = await visibility.IsOwnerSideAsync(d, user, ct).ConfigureAwait(false);
        var active = DocumentStatuses.IsActive(d.Status);
        var underRevision = versions.Any(v => !v.IsNewDocument && VersionStatuses.InProgress(v.Status));
        var ackCount = d.CurrentVersionId is { } current ? await db.Acknowledgements.CountAsync(a => a.VersionId == current, ct).ConfigureAwait(false) : 0;
        var mine = d.CurrentVersionId is { } cv && clearance.EmployeeId is { } me
            && await db.Acknowledgements.AnyAsync(a => a.VersionId == cv && a.EmployeeId == me, ct).ConfigureAwait(false);
        var validity = await db.ValidityEvents.AsNoTracking().Where(v => v.DocumentId == d.Id).OrderByDescending(v => v.At).Take(50)
            .Select(v => new ValidityEventDto(v.Kind, v.Days, v.OldExpiry, v.NewExpiry, v.Comment, v.At)).ToListAsync(ct).ConfigureAwait(false);
        var can = new DocumentCapabilities(
            ConfirmValid: active && ownerSide && d.ExpiryDate is not null,
            NewVersion: active && ownerSide && !underRevision,
            Withdraw: active && ownerSide,
            EditExpiry: active && clearance.Manage,
            Acknowledge: active && d.AckRequired && !mine && clearance.EmployeeId is not null,
            Download: d.CurrentVersionId is not null,
            Manage: clearance.Manage);
        return new DocumentDto(d.Id, d.DocumentNo, d.NameAr, d.NameEn, d.Description, d.Level, d.DocType, d.Sensitivity, await UnitAsync(d.OwnerUnitId, ct).ConfigureAwait(false),
            Person(d.OwnerEmployeeId), d.Status, d.CurrentVersionLabel, d.CurrentVersionId, d.EffectiveDate, d.ExpiryDate, configuration.RuleFor(d.DocType).ValidityMonths,
            d.ShareToAll, underRevision, d.WithdrawnReason, await LinkedAsync(d.Id, ct).ConfigureAwait(false),
            versions.Select(v => new VersionDto(v.Id, v.Label, v.Status, v.ChangeSummary, v.RequestId, v.RequestNo, Person(v.RequestedByEmployeeId), v.ApprovedAt, v.PublishedAt,
                v.FinalFileId, v.FinalFileName, v.TextExtract is not null || v.DraftFileId is not null, v.SummaryAr, v.SummaryEn)).ToList(),
            validity, new AckDto(d.AckRequired, mine, ackCount), can, d.Version);
    }

    /// <summary>Word-level comparison of two versions the user may see (draft text extracted on the fly for in-review versions).</summary>
    public async Task<Result<CompareDto>> CompareAsync(Guid id, Guid from, Guid to, CancellationToken ct)
    {
        var document = await visibility.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound();
        }

        var versions = await visibility.VersionsAsync(document, context.User, ct).ConfigureAwait(false);
        if (versions.FirstOrDefault(v => v.Id == from) is not { } a || versions.FirstOrDefault(v => v.Id == to) is not { } b)
        {
            return Error.NotFound();
        }

        var parts = TextDiff.Compare(await TextOfAsync(a, ct).ConfigureAwait(false), await TextOfAsync(b, ct).ConfigureAwait(false));
        var (added, removed) = TextDiff.Count(parts);
        return new CompareDto(a.Id, a.Label, b.Id, b.Label, parts, added, removed);
    }

    public async Task<string?> TextOfAsync(DocumentVersion version, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(version.TextExtract))
        {
            return version.TextExtract;
        }

        // A scan without a text layer: fall back to the draft's text.
        foreach (var fileId in new[] { version.FinalFileId, version.DraftFileId }.Where(f => f is not null).Select(f => f!.Value))
        {
            var opened = await files.OpenAsync(fileId, auditDownload: false, cancellationToken: ct).ConfigureAwait(false);
            if (opened.IsFailure)
            {
                continue;
            }

            await using var stream = opened.Value.Stream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            var text = await DocumentTextExtractor.ExtractAsync(buffer.ToArray(), opened.Value.FileName, null, ct).ConfigureAwait(false);
            if (text.IsSuccess && !string.IsNullOrWhiteSpace(text.Value.Text))
            {
                return text.Value.Text;
            }
        }

        return null;
    }

    /// <summary>The published file (current or a chosen published version), stamped "uncontrolled when printed" (ADR 0085). Audited by the Files service.</summary>
    public async Task<Result<DownloadFile>> DownloadAsync(Guid id, Guid? versionId, CancellationToken ct)
    {
        var document = await visibility.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound();
        }

        var versions = await visibility.VersionsAsync(document, context.User, ct).ConfigureAwait(false);
        var version = versions.FirstOrDefault(v => v.Id == (versionId ?? document.CurrentVersionId));
        if (version?.FinalFileId is not { } fileId)
        {
            return Error.NotFound();
        }

        var opened = await files.OpenAsync(fileId, cancellationToken: ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        byte[] bytes;
        await using (opened.Value.Stream)
        {
            using var buffer = new MemoryStream();
            await opened.Value.Stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }

        var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
        if (configuration.Watermark && opened.Value.ContentType == "application/pdf")
        {
            var stamped = await watermark.StampAsync(bytes, configuration.WatermarkAr, configuration.WatermarkEn,
                $"{document.DocumentNo} v{version.Label} · {clock.UtcNow:yyyy-MM-dd}", ct).ConfigureAwait(false);
            if (stamped is not null)
            {
                return new DownloadFile(stamped, "application/pdf", opened.Value.FileName, true);
            }
        }

        return new DownloadFile(bytes, opened.Value.ContentType, opened.Value.FileName, false);
    }

    /// <summary>Owner confirms the document is still valid: expiry extended by the type's period, logged (ADR 0084).</summary>
    public async Task<Result<DocumentDto>> ConfirmValidAsync(Guid id, ConfirmInput input, CancellationToken ct)
    {
        var document = await visibility.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound();
        }

        if (!await visibility.IsOwnerSideAsync(document, context.User, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!DocumentStatuses.IsActive(document.Status) || document.ExpiryDate is null)
        {
            return Error.Rule("docreg.validity.notApplicable", "docreg.errors.noExpiry");
        }

        var configuration = await settings.GetAsync<DocRegSettings>(ct).ConfigureAwait(false);
        var today = await calendar.TodayAsync(ct).ConfigureAwait(false);
        var old = document.ExpiryDate;
        var extended = ValidityCalculator.Extend(old, today, configuration.RuleFor(document.DocType).ValidityMonths);
        document.SetExpiry(extended);
        db.ValidityEvents.Add(ValidityEvent.Create(document.Id, ValidityEventKinds.Confirmed, null, old, extended, context.UserId, input.Comment?.Trim(), clock.UtcNow));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ToDtoAsync(document, ct).ConfigureAwait(false);
    }

    public async Task<Result<DocumentDto>> SetExpiryAsync(Guid id, ExpiryInput input, CancellationToken ct)
    {
        var document = await visibility.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound();
        }

        if (!(await visibility.ClearanceAsync(context.User, ct).ConfigureAwait(false)).Manage)
        {
            return Error.Forbidden();
        }

        if (input.ExpiryDate is { } e && document.EffectiveDate is { } effective && e <= effective)
        {
            return Error.Validation([new FieldError("expiryDate", "docreg.errors.expiryBeforeEffective", "docreg.errors.expiryBeforeEffective")]);
        }

        db.Entry(document).Property(x => x.Version).OriginalValue = input.Version;
        var old = document.ExpiryDate;
        document.SetExpiry(input.ExpiryDate);
        db.ValidityEvents.Add(ValidityEvent.Create(document.Id, ValidityEventKinds.ExpiryChanged, null, old, input.ExpiryDate, context.UserId, input.Comment?.Trim(), clock.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ToDtoAsync(document, ct).ConfigureAwait(false);
    }

    public async Task<Result<DocumentDto>> AcknowledgeAsync(Guid id, CancellationToken ct)
    {
        var document = await visibility.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (document is null)
        {
            return Error.NotFound();
        }

        if (context.User.EmployeeIdOf() is not { } employee || document.CurrentVersionId is not { } version || !document.AckRequired)
        {
            return Error.Rule("docreg.ack.notRequired", "docreg.errors.ackNotRequired");
        }

        if (!await db.Acknowledgements.AnyAsync(a => a.VersionId == version && a.EmployeeId == employee, ct).ConfigureAwait(false))
        {
            db.Acknowledgements.Add(Acknowledgement.Create(document.Id, version, employee, clock.UtcNow));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return await ToDtoAsync(document, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The dashboard. <paramref name="from"/> / <paramref name="to"/> (inclusive month range, design note reporting.md §3.3): documents published in
    /// the range, and "expiring" = published documents whose expiry date falls in the range (instead of the next <see cref="SoonDays"/> days).
    /// </summary>
    public async Task<DashboardDto> DashboardAsync(CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var visible = await visibility.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var today = await calendar.TodayAsync(ct).ConfigureAwait(false);
        var soon = today.AddDays(SoonDays);
        var start = from is { } f ? new DateOnly(f.Year, f.Month, 1) : (DateOnly?)null;
        var end = to is { } t ? new DateOnly(t.Year, t.Month, 1).AddMonths(1) : (DateOnly?)null;
        var all = visible.Where(d => d.Status == DocumentStatuses.Published || d.Status == DocumentStatuses.Expired);
        var active = all;
        var expiringSource = all.Where(d => d.Status == DocumentStatuses.Published && d.ExpiryDate != null);
        if (start is { } s)
        {
            var at = new DateTimeOffset(s.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            active = active.Where(d => d.PublishedAt >= at);
            expiringSource = expiringSource.Where(d => d.ExpiryDate >= s);
        }

        if (end is { } e)
        {
            var at = new DateTimeOffset(e.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            active = active.Where(d => d.PublishedAt < at);
            expiringSource = expiringSource.Where(d => d.ExpiryDate < e);
        }
        else if (start is null)
        {
            expiringSource = expiringSource.Where(d => d.ExpiryDate <= soon);
        }

        var published = await active.CountAsync(d => d.Status == DocumentStatuses.Published, ct).ConfigureAwait(false);
        var expired = await active.CountAsync(d => d.Status == DocumentStatuses.Expired, ct).ConfigureAwait(false);
        var expiring = await expiringSource.OrderBy(d => d.ExpiryDate).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var ids = visible.Select(d => d.Id);
        var pending = await db.Versions.CountAsync(v => ids.Contains(v.DocumentId) && (v.Status == VersionStatuses.UnderReview || v.Status == VersionStatuses.Submitted), ct)
            .ConfigureAwait(false);
        var awaiting = await db.Versions.CountAsync(v => ids.Contains(v.DocumentId) && v.Status == VersionStatuses.Approved, ct).ConfigureAwait(false);
        var recent = await active.Where(d => d.PublishedAt != null).OrderByDescending(d => d.PublishedAt).Take(10).ToListAsync(ct).ConfigureAwait(false);
        var levels = await active.GroupBy(d => d.Level).Select(g => new FacetDto(g.Key ?? string.Empty, g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var types = await active.GroupBy(d => d.DocType).Select(g => new FacetDto(g.Key ?? string.Empty, g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var units = await active.Where(d => d.OwnerUnitId != null).GroupBy(d => d.OwnerUnitId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct)
            .ConfigureAwait(false);
        var unitFacets = new List<UnitFacetDto>();
        foreach (var u in units)
        {
            var unit = await org.GetUnitAsync(u.Key, ct).ConfigureAwait(false);
            unitFacets.Add(new UnitFacetDto(u.Key, unit?.NameAr, unit?.NameEn, u.Count));
        }

        return new DashboardDto(published, expired, expiring.Count, pending, awaiting, levels, types, unitFacets, await ItemsAsync(expiring, today, ct).ConfigureAwait(false),
            await ItemsAsync(recent, today, ct).ConfigureAwait(false));
    }

    public Task<Platform.Settings.Application.SettingsDocument<DocRegSettings>> SettingsAsync(CancellationToken ct) => editor.GetDocumentAsync<DocRegSettings>(ct);

    public Task<Result<Platform.Settings.Application.SettingsDocument<DocRegSettings>>> SaveSettingsAsync(DocRegSettingsInput input, CancellationToken ct) =>
        editor.SaveAsync(input.Settings, input.Version, DocRegSettings.Validate, ct);

    /// <summary>Records linked to the document (IP record created from it, tasks, criteria…), as badges the user may see.</summary>
    private async Task<IReadOnlyList<Platform.Links.LinkView>> LinkedAsync(Guid id, CancellationToken ct)
    {
        var stored = await linkStore.ListToAsync(new Platform.Links.LinkRef(Document.EntityType, id), cancellationToken: ct).ConfigureAwait(false);
        if (stored.Count == 0)
        {
            return [];
        }

        var views = await links.ResolveAsync(stored.Select(s => new Platform.Links.LinkRef(s.FromType, s.FromId)).Distinct().ToList(), context.User, ct).ConfigureAwait(false);
        return views.Where(v => v.Visibility == Platform.Links.LinkVisibility.Visible).ToList();
    }

    private async Task<DocUnitDto?> UnitAsync(Guid? id, CancellationToken ct) =>
        id is { } u && await org.GetUnitAsync(u, ct).ConfigureAwait(false) is { } unit ? new DocUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr) : null;
}
