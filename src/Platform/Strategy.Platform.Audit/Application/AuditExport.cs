using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Audit.Infrastructure;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Audit.Application;

/// <summary>Audit Log export (SPEC §0.2: Excel / PDF, bilingual). Capped at <see cref="MaxRows"/> rows; the export itself is audited.</summary>
internal sealed class AuditExport(AuditDbContext db, IPdfRenderer pdf, ISettingsStore settings, IAuditLog log)
{
    public const int MaxRows = 10_000;

    private static readonly ReportColumn[] Columns =
    [
        new("الوقت (UTC)", "Time (UTC)", 11), new("المستخدم", "User", 11), new("القناة", "Channel", 6), new("النظام", "System", 7),
        new("الكائن", "Object", 10), new("السجل", "Record", 12), new("الإجراء", "Action", 8), new("الحقل", "Field", 9),
        new("القيمة السابقة", "Old value", 10), new("القيمة الجديدة", "New value", 10), new("السبب", "Reason", 6),
    ];

    public async Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(AuditFilter filter, string format, string language, CancellationToken ct)
    {
        var arabic = language == "ar";
        var rows = await AuditQuery.Filter(db.Entries.AsNoTracking(), filter).OrderByDescending(e => e.Seq).Take(MaxRows)
            .Select(e => new { e.OccurredAt, e.UserName, e.Channel, e.SystemCode, e.EntityType, e.RecordId, e.Action, e.Field, e.OldValue, e.NewValue, e.Reason, e.Success })
            .ToListAsync(ct).ConfigureAwait(false);

        (byte[], string, string) result;
        if (format == "pdf")
        {
            var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
            var document = new ReportDocument
            {
                TitleAr = "سجل التدقيق",
                TitleEn = "Audit log",
                OrganisationAr = branding.OrganisationNameAr,
                OrganisationEn = branding.OrganisationNameEn,
                PrimaryColor = branding.PrimaryColor,
                Language = arabic ? "ar" : "en",
            }.Table(Columns, rows.Select(r => new[]
            {
                r.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), r.UserName, r.Channel,
                r.SystemCode, r.EntityType, r.RecordId, r.Success == false ? r.Action + " ✗" : r.Action, r.Field,
                Short(r.Field is null ? Summary(r.OldValue) : r.OldValue), Short(r.Field is null ? Summary(r.NewValue) : r.NewValue), r.Reason,
            }));
            result = (await pdf.RenderAsync(document.ToHtml(), ct).ConfigureAwait(false), "application/pdf", "audit-log.pdf");
        }
        else
        {
            var headers = Columns.Select(c => arabic ? c.HeaderAr : c.HeaderEn).ToList();
            var content = ExcelExport.Build(arabic ? "سجل التدقيق" : "Audit log", headers, rows.Select(r => (IReadOnlyList<object?>)
                [r.OccurredAt, r.UserName, r.Channel, r.SystemCode, r.EntityType, r.RecordId, r.Action, r.Field, r.OldValue, r.NewValue, r.Reason]), arabic);
            result = (content, ExcelExport.ContentType, "audit-log.xlsx");
        }

        await log.WriteAsync(new AuditEvent(AuditActions.Export, true, AuditComponent.SystemCode, "audit.entry", null,
            new { format, language, rows = rows.Count, filter }), ct).ConfigureAwait(false);
        return result;
    }

    private static string? Short(string? value) => value is { Length: > 300 } ? value[..300] + "…" : value;

    /// <summary>A create / delete snapshot as "field: value" lines instead of raw JSON (PDF readability).</summary>
    private static string? Summary(string? json)
    {
        if (json is null || !json.StartsWith('{'))
        {
            return json;
        }

        try
        {
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
            return string.Join(" · ", values.Where(v => !string.IsNullOrEmpty(v.Value)).Select(v => $"{v.Key}: {v.Value}"));
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }
}
