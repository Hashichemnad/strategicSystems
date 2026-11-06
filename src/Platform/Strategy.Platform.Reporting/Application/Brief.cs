using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.Platform.Reporting.Application;

public sealed record BriefEvidenceDto(string Ref, string ResourceType, Guid Id, string? Number, string? TitleAr, string? TitleEn, string Link);

public sealed record BriefPointDto(string TextAr, string TextEn, IReadOnlyList<BriefEvidenceDto> Evidence);

public sealed record BriefDto(Guid? Id, string PeriodKey, IReadOnlyList<BriefPointDto> Improved, IReadOnlyList<BriefPointDto> Attention, IReadOnlyList<BriefPointDto> Decisions,
    bool AiGenerated, Guid? UsageId, DateTimeOffset? SavedAt);

public sealed record BriefInput(string PeriodKey, IReadOnlyList<BriefPointDto> Improved, IReadOnlyList<BriefPointDto> Attention, IReadOnlyList<BriefPointDto> Decisions,
    bool AiGenerated);

/// <summary>
/// AI executive brief (ADR 0147): built only from the viewer's own executive dashboard (current and previous period); evidence is referenced by
/// opaque ids mapped server-side — unknown ids are dropped, so the brief can only cite records the viewer may open. A draft until saved.
/// </summary>
public sealed class BriefService(AiGateway gateway, DashboardService dashboards, ReportingDbContext db, ReportingClock clock)
{
    public const string Feature = "reporting-executive-brief";

    private static readonly JsonElement Schema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["improved","attention","decisions"],"properties":{
          "improved":{"type":"array","maxItems":6,"items":{"$ref":"#/$defs/point"}},
          "attention":{"type":"array","maxItems":6,"items":{"$ref":"#/$defs/point"}},
          "decisions":{"type":"array","maxItems":5,"items":{"$ref":"#/$defs/point"}}},
         "$defs":{"point":{"type":"object","required":["textAr","textEn","evidence"],"properties":{
          "textAr":{"type":"string","maxLength":1200},"textEn":{"type":"string","maxLength":1200},
          "evidence":{"type":"array","maxItems":5,"items":{"type":"string","maxLength":20}}}}}}
        """);

    private static string Pct(decimal? v, string? kind) => DashboardRenderer.Format(v, kind);

    public async Task<Result<BriefDto>> GenerateAsync(ClaimsPrincipal user, string? kind, string? from, string? to, Guid? unitId, CancellationToken ct)
    {
        var data = await dashboards.DataAsync("executive", user, kind, from, to, true, unitId, false, ct).ConfigureAwait(false);
        if (data.IsFailure)
        {
            return data.Error;
        }

        var dashboard = data.Value;
        var evidence = new Dictionary<string, BriefEvidenceDto>(StringComparer.Ordinal);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Period {dashboard.Period.From} to {dashboard.Period.To}; previous period {dashboard.Period.PreviousFrom} to {dashboard.Period.PreviousTo}.");
        foreach (var (w, d) in dashboard.Widgets.Zip(dashboard.Data))
        {
            if (d.State is "unavailable" || w.Type == WidgetTypes.Brief)
            {
                continue;
            }

            var title = w.TitleEn ?? d.MetricLabelEn ?? w.Id;
            if (d.Value is not null)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"{title}: {Pct(d.Value, d.ValueKind)} (previous {Pct(d.Previous, d.ValueKind)}){(d.Rag is { } rag ? ", " + rag : string.Empty)}");
            }

            foreach (var row in (d.Rows ?? []).Take(12))
            {
                var key = string.Join(", ", row.Key.Select(k => $"{k.Key}={Name(dashboard, d, k.Key, k.Value)}"));
                text.AppendLine(CultureInfo.InvariantCulture, $"  {title} [{key}]: {Pct(row.Value, d.ValueKind)} (previous {Pct(row.Previous, d.ValueKind)})");
            }

            foreach (var item in d.Items ?? [])
            {
                var refId = "ref-" + (evidence.Count + 1).ToString(CultureInfo.InvariantCulture);
                evidence[refId] = new BriefEvidenceDto(refId, item.ResourceType, item.Id, item.Number, item.TitleAr, item.TitleEn, item.Link);
                text.AppendLine(CultureInfo.InvariantCulture, $"  {title} item {refId}: {item.Number} {item.TitleEn ?? item.TitleAr} {item.DetailEn} {item.Date:yyyy-MM-dd}");
            }

            if (d.Scorecard is { } card)
            {
                foreach (var row in card.Rows)
                {
                    text.AppendLine(CultureInfo.InvariantCulture,
                        $"  Department {row.NameEn ?? row.NameAr}: {string.Join("; ", row.Cells.Zip(card.Columns).Select(p => $"{p.Second.LabelEn} {Pct(p.First.Value, p.Second.ValueKind)} {p.First.Rag}"))}");
                }
            }
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(Feature,
            new Dictionary<string, string> { ["period"] = $"{dashboard.Period.From} – {dashboard.Period.To}", ["scope"] = unitId is null ? "organisation" : "unit" },
            [new AiDocument("executive-dashboard", text.ToString())], Schema, ReportingPermissions.SystemCode, AuditDetail: new { dashboard.Period, evidence = evidence.Count }), ct)
            .ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        IReadOnlyList<BriefPointDto> Points(string property) =>
            call.Value.Output.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray().Select(p => new BriefPointDto(
                    p.TryGetProperty("textAr", out var ar) ? ar.GetString() ?? string.Empty : string.Empty,
                    p.TryGetProperty("textEn", out var en) ? en.GetString() ?? string.Empty : string.Empty,
                    p.TryGetProperty("evidence", out var refs) && refs.ValueKind == JsonValueKind.Array
                        ? refs.EnumerateArray().Select(r => r.GetString()).OfType<string>().Where(evidence.ContainsKey).Distinct(StringComparer.Ordinal).Select(r => evidence[r]).ToList()
                        : []))
                    .Where(p => p.TextAr.Length > 0 || p.TextEn.Length > 0).ToList()
                : [];

        return new BriefDto(null, dashboard.Period.Kind + ":" + dashboard.Period.From + ":" + dashboard.Period.To, Points("improved"), Points("attention"), Points("decisions"),
            true, call.Value.UsageId, null);
    }

    private static string Name(DashboardDataDto dashboard, WidgetDataDto d, string dim, string code) =>
        dim == "unit"
            ? dashboard.Units.FirstOrDefault(u => u.Id.ToString("D") == code) is { } u ? u.NameEn ?? u.NameAr ?? u.Code : code
            : d.Labels?.FirstOrDefault(l => l.Dimension == dim && l.Code == code) is { } l ? l.LabelEn ?? l.LabelAr ?? code : code;

    public async Task<Result<BriefDto>> SaveAsync(BriefInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (input.PeriodKey is null || input.Improved is null || input.Attention is null || input.Decisions is null || input.PeriodKey.Length > 50 || input.Improved.Count + input.Attention.Count + input.Decisions.Count > 30
            || input.Improved.Concat(input.Attention).Concat(input.Decisions).Any(p => p.TextAr.Length > 2000 || p.TextEn.Length > 2000))
        {
            return Error.Validation([new FieldError("brief", "reporting.errors.brief", "reporting.errors.brief")]);
        }

        var json = JsonSerializer.Serialize(new { input.Improved, input.Attention, input.Decisions }, DashboardService.Json);
        var brief = new ExecutiveBrief(input.PeriodKey, "executive", json, input.AiGenerated);
        db.Briefs.Add(brief);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BriefDto(brief.Id, brief.PeriodKey, input.Improved, input.Attention, input.Decisions, input.AiGenerated, null, clock.UtcNow);
    }

    public async Task<IReadOnlyList<BriefDto>> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var rows = await db.Briefs.AsNoTracking().Where(b => b.CreatedBy == userId).OrderByDescending(b => b.CreatedAt).Take(20).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(b =>
        {
            var content = JsonSerializer.Deserialize<BriefInput>(b.ContentJson, DashboardService.Json);
            return new BriefDto(b.Id, b.PeriodKey, content?.Improved ?? [], content?.Attention ?? [], content?.Decisions ?? [], b.AiGenerated, null, b.CreatedAt);
        }).ToList();
    }
}
