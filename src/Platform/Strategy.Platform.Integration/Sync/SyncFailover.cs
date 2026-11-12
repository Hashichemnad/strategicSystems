using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Sync;

public sealed record MakePrimaryInput(string? Reason, string? Method, string? Code);

public sealed record ReturnToStandbyInput(bool Acknowledged);

public sealed record ReEnteredInput(bool ReEntered);

public sealed record ResolveConflictInput(string? Choice);

public sealed record StepUpMethodsDto(IReadOnlyList<string> Methods);

public sealed record OutageChangeDto(
    DateTimeOffset At, string Action, string? EntityType, string? Field, string? OldValue, string? NewValue, string? UserName, string Channel, string Kind);

public sealed record OutageRecordReportDto(
    string EntityType, Guid RecordId, string? TypeNameAr, string? TypeNameEn, string? Number, string? TitleAr, string? TitleEn, string? Link, string? SalesforceId,
    bool ResubmitRequired, bool ChangedLocally, bool ReEntered, bool Conflict, string? Resolution, IReadOnlyList<OutageChangeDto> Changes);

public sealed record OutageSystemReportDto(string SystemCode, IReadOnlyList<OutageRecordReportDto> Records);

public sealed record OutageReportDto(SyncOutageDto Outage, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<OutageSystemReportDto> Systems, int Changes, bool Truncated);

public sealed record OutageConflictDto(
    Guid RecordId, string EntityType, string? Number, string? TitleAr, string? TitleEn, string? Link, string? SalesforceId, bool Conflict, string Resolution,
    DateTimeOffset? SourceModifiedAt, IReadOnlyDictionary<string, string?>? Pending);

/// <summary>
/// Failover (SPEC-ADDENDUM §G5 / G6, ADR 0207): make this system primary (MFA re-prompt, reason), outage period, "Resubmit required",
/// outage changes report from the audit trail (Excel / PDF, re-entered marks), return to standby with per-record conflict choices, close.
/// </summary>
public sealed class SyncFailover(
    IntegrationDbContext db,
    SyncAdministration administration,
    SyncEngine engine,
    SyncStandbyGuard guard,
    IStepUpVerifier stepUp,
    IAuditQuery auditQuery,
    IAuditLog audit,
    IReferencedRecordResolver resolver,
    IPdfRenderer pdf,
    ISettingsStore settings,
    IRequestContext context,
    IClock clock)
{
    public const int MaxReportEntries = 50_000;
    private const string StepUpPurpose = "integration.sync.makePrimary";

    private static Error Transition(string mode) =>
        Error.Rule("integration.sync.modeTransition", "integration.sync.errors.modeTransition", new Dictionary<string, object?> { ["mode"] = mode });

    public async Task<StepUpMethodsDto> StepUpMethodsAsync(CancellationToken ct) => new(await stepUp.MethodsAsync(context.User, ct).ConfigureAwait(false));

    public Task<Result> SendStepUpCodeAsync(CancellationToken ct) => stepUp.SendEmailCodeAsync(context.User, ct);

    public async Task<Result<SyncStatusDto>> MakePrimaryAsync(Guid connectionId, MakePrimaryInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        if (SyncModeLifecycle.Next(connection.Mode, SyncModeActions.MakePrimary) is not { } next)
        {
            return Transition(connection.Mode);
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length is < 5 or > 2000)
        {
            return Error.Validation([new FieldError("reason", "reasonRequired", "integration.sync.errors.reasonRequired")]);
        }

        var verified = await stepUp.VerifyAsync(context.User, input.Method ?? string.Empty, input.Code, StepUpPurpose, ct).ConfigureAwait(false);
        if (verified.IsFailure)
        {
            await audit.WriteAsync(new AuditEvent("integration.sync.makePrimary", false, IntegrationPermissions.SystemCode, SyncConnection.EntityType, connectionId.ToString(),
                new { reason = "mfa" }), ct).ConfigureAwait(false);
            return verified.Error!;
        }

        var now = clock.UtcNow;
        var outage = await db.Outages.Where(o => o.ConnectionId == connectionId && o.Status == OutageStatuses.Reconciling).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (outage is null)
        {
            outage = new SyncOutage { ConnectionId = connectionId, StartedAt = now, StartedBy = context.UserId, Reason = reason };
            db.Outages.Add(outage);
        }
        else
        {
            outage.Status = OutageStatuses.Open; // Salesforce failed again before the outage was closed
            outage.ReturnedAt = null;
            outage.Reason = outage.Reason + " | " + reason;
        }

        connection.Mode = next;
        connection.NextRunAt = null;
        connection.RunRequestedAt = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Records whose Salesforce approval was pending at the switch: "Resubmit required" (approval history kept).
        var pending = await db.SourceHistory.AsNoTracking()
            .Where(h => h.SourceSystem == connection.SourceSystem && h.Kind == SourceHistoryKinds.Approval && h.ExternalId.StartsWith("pi:") && h.ProcessStatus == "Pending")
            .Select(h => new { h.EntityType, h.RecordId, h.RecordExternalId }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        foreach (var p in pending)
        {
            var row = await RecordAsync(outage.Id, p.EntityType, p.RecordId, p.RecordExternalId, ct).ConfigureAwait(false);
            row.ResubmitRequired = true;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        guard.Invalidate();
        await audit.WriteAsync(new AuditEvent("integration.sync.makePrimary", true, IntegrationPermissions.SystemCode, SyncConnection.EntityType, connectionId.ToString(),
            new { outage = outage.Id, resubmitRequired = pending.Count }, reason), ct).ConfigureAwait(false);
        return await administration.StatusAsync(connectionId, ct).ConfigureAwait(false);
    }

    public async Task<Result<SyncStatusDto>> ReturnToStandbyAsync(Guid connectionId, ReturnToStandbyInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        if (SyncModeLifecycle.Next(connection.Mode, SyncModeActions.ReturnToStandby) is not { } next)
        {
            return Transition(connection.Mode);
        }

        var outage = await db.Outages.FirstAsync(o => o.ConnectionId == connectionId && o.Status == OutageStatuses.Open, ct).ConfigureAwait(false);
        if (outage.ReportAcknowledgedAt is null && !input.Acknowledged)
        {
            return Error.Rule("integration.sync.reportNotAcknowledged", "integration.sync.errors.reportNotAcknowledged");
        }

        var now = clock.UtcNow;
        if (outage.ReportAcknowledgedAt is null)
        {
            outage.ReportAcknowledgedAt = now;
            outage.ReportAcknowledgedBy = context.UserId;
        }

        // Synced records changed here during the outage: a later Salesforce version waits for the admin's choice.
        var entries = await ReadEntriesAsync(outage.StartedAt, now, connection.SourceSystem, ct).ConfigureAwait(false);
        var touched = entries.Entries.Select(e => KeyOf(e)).Where(k => k is not null).Select(k => k!.Value).Distinct().ToList();
        var ids = touched.Select(t => t.Id).ToList();
        var synced = await db.References.AsNoTracking()
            .Where(r => r.SourceSystem == connection.SourceSystem && r.IsOrigin && ids.Contains(r.RecordId))
            .Select(r => new { r.EntityTypeCode, r.RecordId, r.ExternalId }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var s in synced.DistinctBy(s => s.RecordId))
        {
            var row = await RecordAsync(outage.Id, s.EntityTypeCode, s.RecordId, s.ExternalId, ct).ConfigureAwait(false);
            row.ChangedLocally = true;
        }

        outage.Status = OutageStatuses.Reconciling;
        outage.ReturnedAt = now;
        outage.ReturnedBy = context.UserId;
        connection.Mode = next;
        connection.NextRunAt = now; // resume at once
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        guard.Invalidate();
        await audit.WriteAsync(new AuditEvent("integration.sync.returnToStandby", true, IntegrationPermissions.SystemCode, SyncConnection.EntityType, connectionId.ToString(),
            new { outage = outage.Id, changedLocally = synced.Count }), ct).ConfigureAwait(false);
        return await administration.StatusAsync(connectionId, ct).ConfigureAwait(false);
    }

    public async Task<Result<SyncStatusDto>> CloseOutageAsync(Guid connectionId, CancellationToken ct)
    {
        var connection = await db.SyncConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct).ConfigureAwait(false);
        if (connection is null)
        {
            return Error.NotFound();
        }

        if (SyncModeLifecycle.Next(connection.Mode, SyncModeActions.CloseOutage) is not { } next)
        {
            return Transition(connection.Mode);
        }

        var outage = await db.Outages.FirstAsync(o => o.ConnectionId == connectionId && o.Status == OutageStatuses.Reconciling, ct).ConfigureAwait(false);
        var pending = await db.OutageRecords.CountAsync(r => r.OutageId == outage.Id && r.Conflict && r.Resolution == ConflictResolutions.Pending, ct).ConfigureAwait(false);
        if (pending > 0)
        {
            return Error.Rule("integration.sync.conflictsPending", "integration.sync.errors.conflictsPending", new Dictionary<string, object?> { ["count"] = pending });
        }

        outage.Status = OutageStatuses.Closed;
        outage.ClosedAt = clock.UtcNow;
        outage.ClosedBy = context.UserId;
        connection.Mode = next;
        connection.NextRunAt = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        guard.Invalidate();
        await audit.WriteAsync(new AuditEvent("integration.sync.closeOutage", true, IntegrationPermissions.SystemCode, SyncOutage.EntityType, outage.Id.ToString()), ct)
            .ConfigureAwait(false);
        return await administration.StatusAsync(connectionId, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<SyncOutageDto>> OutagesAsync(Guid connectionId, PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        var query = db.Outages.AsNoTracking().Where(o => o.ConnectionId == connectionId);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(o => o.StartedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        var list = new List<SyncOutageDto>();
        foreach (var row in rows)
        {
            list.Add(await administration.OutageDtoAsync(row, ct).ConfigureAwait(false));
        }

        return new PagedResult<SyncOutageDto>(list, page.Page, page.PageSize, total);
    }

    // ---- Outage changes report (§G6) ----

    public async Task<Result<OutageReportDto>> ReportAsync(Guid outageId, CancellationToken ct)
    {
        var outage = await db.Outages.AsNoTracking().FirstOrDefaultAsync(o => o.Id == outageId, ct).ConfigureAwait(false);
        if (outage is null)
        {
            return Error.NotFound();
        }

        var connection = await db.SyncConnections.AsNoTracking().FirstAsync(c => c.Id == outage.ConnectionId, ct).ConfigureAwait(false);
        var to = outage.ReturnedAt ?? outage.ClosedAt ?? clock.UtcNow;
        var (entries, truncated) = await ReadEntriesAsync(outage.StartedAt, to, connection.SourceSystem, ct).ConfigureAwait(false);
        var grouped = entries.Select(e => (Key: KeyOf(e), Entry: e)).Where(x => x.Key is not null).GroupBy(x => x.Key!.Value).ToList();
        var ids = grouped.Select(g => g.Key.Id).ToList();
        var references = await db.References.AsNoTracking().Where(r => r.SourceSystem == connection.SourceSystem && ids.Contains(r.RecordId))
            .ToListAsync(ct).ConfigureAwait(false);
        var flags = await db.OutageRecords.AsNoTracking().Where(r => r.OutageId == outageId).ToListAsync(ct).ConfigureAwait(false);
        var resolved = new Dictionary<Guid, ResolvedRecord>();
        foreach (var byType in grouped.GroupBy(g => g.Key.Type))
        {
            foreach (var (id, record) in await resolver.ResolveAsync(byType.Key, [.. byType.Select(g => g.Key.Id)], context.User, ct).ConfigureAwait(false))
            {
                resolved[id] = record;
            }
        }

        var records = grouped.Select(g =>
        {
            var r = resolved.GetValueOrDefault(g.Key.Id);
            var reference = references.FirstOrDefault(x => x.RecordId == g.Key.Id && x.IsOrigin) ?? references.FirstOrDefault(x => x.RecordId == g.Key.Id);
            var flag = flags.FirstOrDefault(f => f.RecordId == g.Key.Id);
            var changes = g.Select(x => x.Entry).OrderBy(e => e.Seq).Select(e => new OutageChangeDto(e.OccurredAt, e.Action, e.EntityType, e.Field, e.OldValue, e.NewValue,
                e.UserName ?? e.OnBehalfOfName, e.Channel, e.Kind)).ToList();
            return (System: r?.SystemCode ?? g.First().Entry.SystemCode ?? g.Key.Type.Split('.')[0], Dto: new OutageRecordReportDto(g.Key.Type, g.Key.Id, r?.TypeNameAr, r?.TypeNameEn,
                r?.Number, r?.TitleAr, r?.TitleEn, r?.Link, reference?.ExternalId, flag?.ResubmitRequired ?? false, flag?.ChangedLocally ?? false, flag?.ReEntered ?? false,
                flag?.Conflict ?? false, flag?.Resolution, changes));
        }).ToList();
        var systems = records.GroupBy(r => r.System).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new OutageSystemReportDto(g.Key, [.. g.Select(x => x.Dto).OrderBy(x => x.Number ?? x.RecordId.ToString(), StringComparer.Ordinal)])).ToList();
        return new OutageReportDto(await administration.OutageDtoAsync(outage, ct).ConfigureAwait(false), outage.StartedAt, to, systems, entries.Count, truncated);
    }

    /// <summary>
    /// Changes and approval events made here (not by the sync) in the period, from the audit trail: every change of the business systems and
    /// Organisation, and every change of any record (or child row of a record) that came from the source.
    /// </summary>
    private async Task<(IReadOnlyList<AuditEntryDto> Entries, bool Truncated)> ReadEntriesAsync(DateTimeOffset from, DateTimeOffset to, string source, CancellationToken ct)
    {
        var options = await settings.GetAsync<SyncSettings>(ct).ConfigureAwait(false);
        var systems = new HashSet<string>(options.EffectiveSuspendedSystems, StringComparer.Ordinal) { "org" };
        var candidates = new List<AuditEntryDto>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            var result = await auditQuery.SearchAsync(new AuditFilter(From: from, To: to), new PageRequest(page, PageRequest.MaxPageSize), ct).ConfigureAwait(false);
            candidates.AddRange(result.Items.Where(e => e.Channel != Channels.Sync
                && ((e.Kind == AuditKinds.Change && e.SystemCode is not ("integration" or "audit" or "scheduling" or "notify" or "notifications"))
                    || (e.Kind == AuditKinds.Event && e.Action.StartsWith("workflow.", StringComparison.Ordinal) && e.Data is not null))));
            if (candidates.Count >= MaxReportEntries)
            {
                truncated = true;
                break;
            }

            if (result.Items.Count < PageRequest.MaxPageSize)
            {
                break;
            }
        }

        var ids = candidates.Select(KeyOf).Where(k => k is not null).Select(k => k!.Value.Id).Distinct().ToList();
        var synced = (await db.References.AsNoTracking().Where(r => r.SourceSystem == source && r.IsOrigin && ids.Contains(r.RecordId)).Select(r => r.RecordId)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var list = candidates.Where(e => (e.SystemCode is { } system && systems.Contains(system)) || e.Kind == AuditKinds.Event
            || (KeyOf(e) is { } key && synced.Contains(key.Id))).ToList();
        return (list, truncated);
    }

    /// <summary>The record an entry belongs to: its audit parent, the workflow subject of an approval event, or the entry's own record.</summary>
    private static (string Type, Guid Id)? KeyOf(AuditEntryDto e)
    {
        if (e.Kind == AuditKinds.Event && e.Data is { } data)
        {
            try
            {
                using var json = JsonDocument.Parse(data);
                if (json.RootElement.TryGetProperty("resourceType", out var type) && json.RootElement.TryGetProperty("resourceId", out var id)
                    && Guid.TryParse(id.GetString(), out var subject) && type.GetString() is { } t)
                {
                    return (t, subject);
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }

        if (e.ParentType is { } parentType && Guid.TryParse(e.ParentId, out var parentId))
        {
            return (parentType, parentId);
        }

        return e.EntityType is { } entityType && Guid.TryParse(e.RecordId, out var recordId) ? (entityType, recordId) : null;
    }

    public async Task<Result> AcknowledgeAsync(Guid outageId, CancellationToken ct)
    {
        var outage = await db.Outages.FirstOrDefaultAsync(o => o.Id == outageId, ct).ConfigureAwait(false);
        if (outage is null)
        {
            return Error.NotFound();
        }

        if (outage.ReportAcknowledgedAt is null)
        {
            outage.ReportAcknowledgedAt = clock.UtcNow;
            outage.ReportAcknowledgedBy = context.UserId;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result.Success();
    }

    public async Task<Result<byte[]>> ExportExcelAsync(Guid outageId, string language, CancellationToken ct)
    {
        var report = await ReportAsync(outageId, ct).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return report.Error!;
        }

        var ar = language == "ar";
        using var book = new XLWorkbook();
        string[] headers = ar
            ? ["النظام", "نوع السجل", "الرقم", "العنوان", "معرّف Salesforce", "الوقت", "الإجراء", "الحقل", "القيمة السابقة", "القيمة الجديدة", "المستخدم", "أعيد إدخاله"]
            : ["System", "Record type", "Number", "Title", "Salesforce ID", "At", "Action", "Field", "Old value", "New value", "User", "Re-entered"];
        foreach (var system in report.Value.Systems.DefaultIfEmpty(new OutageSystemReportDto("-", [])))
        {
            var sheet = book.Worksheets.Add(system.SystemCode.Length > 31 ? system.SystemCode[..31] : system.SystemCode);
            sheet.RightToLeft = ar;
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
                sheet.Cell(1, c + 1).Style.Font.Bold = true;
            }

            var row = 2;
            foreach (var record in system.Records)
            {
                foreach (var change in record.Changes)
                {
                    object?[] values =
                    [
                        system.SystemCode, (ar ? record.TypeNameAr : record.TypeNameEn) ?? record.EntityType, record.Number,
                        (ar ? record.TitleAr ?? record.TitleEn : record.TitleEn ?? record.TitleAr), record.SalesforceId,
                        change.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture), change.Action, change.Field,
                        change.OldValue, change.NewValue, change.UserName, record.ReEntered ? (ar ? "نعم" : "Yes") : string.Empty,
                    ];
                    for (var c = 0; c < values.Length; c++)
                    {
                        sheet.Cell(row, c + 1).Value = Safe(values[c] as string);
                    }

                    row++;
                }
            }

            sheet.Columns().AdjustToContents(1, 200, 8, 60);
        }

        await using var stream = new MemoryStream();
        book.SaveAs(stream);
        await MarkExportedAsync(outageId, "xlsx", ct).ConfigureAwait(false);
        return stream.ToArray();
    }

    public async Task<Result<byte[]>> ExportPdfAsync(Guid outageId, string language, CancellationToken ct)
    {
        var report = await ReportAsync(outageId, ct).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return report.Error!;
        }

        var ar = language == "ar";
        var r = report.Value;
        static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"<!doctype html><html lang=\"{(ar ? "ar" : "en")}\" dir=\"{(ar ? "rtl" : "ltr")}\"><head><meta charset=\"utf-8\"><style>")
            .Append("body{font-family:'Noto Naskh Arabic','Noto Sans Arabic','IBM Plex Sans Arabic',sans-serif;font-size:10pt;margin:16mm}h1{font-size:16pt}h2{font-size:13pt;margin-block-start:14pt}")
            .Append("table{width:100%;border-collapse:collapse;margin-block:6pt}th,td{border:1px solid #999;padding:3pt;text-align:start;vertical-align:top;word-break:break-word}th{background:#eee}")
            .Append(".rec{margin-block-start:8pt;font-weight:bold}</style></head><body>")
            .Append(CultureInfo.InvariantCulture, $"<h1>{(ar ? "تقرير التغييرات أثناء فترة الانقطاع" : "Outage changes report")}</h1>")
            .Append(CultureInfo.InvariantCulture, $"<p>{H(r.From.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} → {H(r.To.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} UTC · {H(r.Outage.Reason)}</p>");
        foreach (var system in r.Systems)
        {
            html.Append(CultureInfo.InvariantCulture, $"<h2>{H(system.SystemCode)}</h2>");
            foreach (var record in system.Records)
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"rec\">{H((ar ? record.TypeNameAr : record.TypeNameEn) ?? record.EntityType)} {H(record.Number)} — {H(ar ? record.TitleAr ?? record.TitleEn : record.TitleEn ?? record.TitleAr)} · Salesforce: {H(record.SalesforceId ?? "—")}{(record.ReEntered ? (ar ? " · ✓ أعيد إدخاله" : " · ✓ re-entered") : string.Empty)}</div>")
                    .Append(CultureInfo.InvariantCulture, $"<table><tr><th>{(ar ? "الوقت" : "At")}</th><th>{(ar ? "الإجراء" : "Action")}</th><th>{(ar ? "الحقل" : "Field")}</th><th>{(ar ? "القيمة السابقة" : "Old value")}</th><th>{(ar ? "القيمة الجديدة" : "New value")}</th><th>{(ar ? "المستخدم" : "User")}</th></tr>");
                foreach (var c in record.Changes)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<tr><td>{H(c.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}</td><td>{H(c.Action)}</td><td>{H(c.Field)}</td><td>{H(c.OldValue)}</td><td>{H(c.NewValue)}</td><td>{H(c.UserName)}</td></tr>");
                }

                html.Append("</table>");
            }
        }

        html.Append("</body></html>");
        try
        {
            var bytes = await pdf.RenderAsync(html.ToString(), ct).ConfigureAwait(false);
            await MarkExportedAsync(outageId, "pdf", ct).ConfigureAwait(false);
            return bytes;
        }
        catch (PdfRenderingException ex)
        {
            return Error.Rule("integration.sync.pdf", ex.Message);
        }
    }

    private async Task MarkExportedAsync(Guid outageId, string format, CancellationToken ct)
    {
        await AcknowledgeAsync(outageId, ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, IntegrationPermissions.SystemCode, SyncOutage.EntityType, outageId.ToString(), new { format, report = "outage-changes" }), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Formula-injection guard for spreadsheet cells.</summary>
    private static string Safe(string? value) => value is { Length: > 0 } && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value ?? string.Empty;

    public async Task<Result> SetReEnteredAsync(Guid outageId, Guid recordId, string entityType, ReEnteredInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Outages.AnyAsync(o => o.Id == outageId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var row = await RecordAsync(outageId, entityType, recordId, null, ct).ConfigureAwait(false);
        row.ReEntered = input.ReEntered;
        row.ReEnteredAt = input.ReEntered ? clock.UtcNow : null;
        row.ReEnteredBy = input.ReEntered ? context.UserId : null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<OutageConflictDto>>> ConflictsAsync(Guid outageId, CancellationToken ct)
    {
        if (!await db.Outages.AnyAsync(o => o.Id == outageId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var rows = await db.OutageRecords.AsNoTracking().Where(r => r.OutageId == outageId && r.ChangedLocally).OrderByDescending(r => r.Conflict).ThenBy(r => r.EntityTypeCode)
            .ToListAsync(ct).ConfigureAwait(false);
        var resolved = new Dictionary<Guid, ResolvedRecord>();
        foreach (var byType in rows.GroupBy(r => r.EntityTypeCode))
        {
            foreach (var (id, record) in await resolver.ResolveAsync(byType.Key, [.. byType.Select(r => r.RecordId)], context.User, ct).ConfigureAwait(false))
            {
                resolved[id] = record;
            }
        }

        return rows.Select(r =>
        {
            var x = resolved.GetValueOrDefault(r.RecordId);
            return new OutageConflictDto(r.RecordId, r.EntityTypeCode, x?.Number, x?.TitleAr, x?.TitleEn, x?.Link, r.ExternalId, r.Conflict, r.Resolution, r.PendingSourceModifiedAt,
                r.PendingPayload is null ? null : Salesforce.SourceRecord.FromJson(r.PendingPayload).Values);
        }).ToList();
    }

    public async Task<Result<OutageConflictDto>> ResolveConflictAsync(Guid outageId, Guid recordId, ResolveConflictInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Choice is not (ConflictResolutions.KeepLocal or ConflictResolutions.TakeSource))
        {
            return Error.Validation([new FieldError("choice", "invalid", "validation.invalid")]);
        }

        var outage = await db.Outages.AsNoTracking().FirstOrDefaultAsync(o => o.Id == outageId && o.Status != OutageStatuses.Closed, ct).ConfigureAwait(false);
        var row = outage is null ? null : await db.OutageRecords.FirstOrDefaultAsync(r => r.OutageId == outageId && r.RecordId == recordId && r.ChangedLocally, ct).ConfigureAwait(false);
        if (outage is null || row is null)
        {
            return Error.NotFound();
        }

        row.Resolution = input.Choice;
        if (input.Choice == ConflictResolutions.TakeSource && row.PendingPayload is not null && outage.Status == OutageStatuses.Reconciling)
        {
            var connection = await db.SyncConnections.AsNoTracking().FirstAsync(c => c.Id == outage.ConnectionId, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var outcome = await engine.ApplyPendingAsync(connection, row, ct).ConfigureAwait(false);
            if (outcome is RecordOutcome.Created or RecordOutcome.Updated or RecordOutcome.Unchanged)
            {
                row.PendingPayload = null;
                row.PendingMappingId = null;
            }
        }

        if (input.Choice == ConflictResolutions.KeepLocal)
        {
            row.PendingPayload = null; // the Salesforce version is discarded until the outage closes
        }

        row.Conflict = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("integration.sync.conflict.resolve", true, IntegrationPermissions.SystemCode, row.EntityTypeCode, recordId.ToString(),
            new { input.Choice, outage = outageId }), ct).ConfigureAwait(false);
        return (await ConflictsAsync(outageId, ct).ConfigureAwait(false)).Value.First(c => c.RecordId == recordId);
    }

    private async Task<SyncOutageRecord> RecordAsync(Guid outageId, string entityType, Guid recordId, string? externalId, CancellationToken ct)
    {
        var row = db.OutageRecords.Local.FirstOrDefault(r => r.OutageId == outageId && r.RecordId == recordId)
            ?? await db.OutageRecords.FirstOrDefaultAsync(r => r.OutageId == outageId && r.RecordId == recordId, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new SyncOutageRecord { OutageId = outageId, EntityTypeCode = entityType, RecordId = recordId, ExternalId = externalId };
            db.OutageRecords.Add(row);
        }

        row.ExternalId ??= externalId;
        return row;
    }
}
