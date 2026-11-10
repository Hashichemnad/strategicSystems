using System.Globalization;
using System.Text.Json.Nodes;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Requests.Application;

public sealed record PdfFile(byte[] Content, string FileName);

public sealed record TemplatePreviewInput(PdfTemplate? Template, string? Language);

/// <summary>Request PDF templates with merge fields (ADR 0088): values HTML-encoded, secrets masked, rendered by Gotenberg.</summary>
internal sealed class RequestPdf(
    RequestReader reader,
    RequestTypeCache types,
    RequestQueries queries,
    IPdfRenderer renderer,
    ILookupService lookups,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    ISettingsStore settings,
    IAuditLog audit,
    IRequestContext context,
    IClock clock)
{
    public async Task<Result<PdfFile>> RenderAsync(Guid id, string templateCode, string? language, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return Error.NotFound();
        }

        var dto = await queries.ToDtoAsync(request, ct).ConfigureAwait(false);
        if (dto.Templates.All(t => t.Code != templateCode) || type.Templates.FirstOrDefault(t => t.Code == templateCode) is not { } template)
        {
            return Error.NotFound();
        }

        var model = await ModelAsync(type, request, dto.Data, dto.ProcessData, language ?? "ar", ct).ConfigureAwait(false);
        var pdf = await RenderHtmlAsync(template, model, language, ct).ConfigureAwait(false);
        if (pdf.IsFailure)
        {
            return pdf.Error;
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, RequestPermissions.SystemCode, Request.EntityType, request.Id.ToString("D"),
            new { template = templateCode, language }), ct).ConfigureAwait(false);
        return new PdfFile(pdf.Value, $"{request.Number}-{templateCode}.pdf");
    }

    /// <summary>Builder preview with sample values (field codes shown as values).</summary>
    public async Task<Result<PdfFile>> PreviewAsync(string typeCode, TemplatePreviewInput input, CancellationToken ct)
    {
        if (types.Find(typeCode) is not { } type || input.Template is null)
        {
            return Error.NotFound();
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["request.number"] = $"{type.NumberPrefix}-{clock.Today.Year}-0001",
            ["request.title"] = type.NameEn,
            ["request.type"] = input.Language == "en" ? type.NameEn : type.NameAr,
            ["today"] = clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["org.name"] = await OrgNameAsync(input.Language ?? "ar", ct).ConfigureAwait(false),
        };
        foreach (var field in type.Form.AllFields)
        {
            values[$"field.{field.Code}"] = $"[{(input.Language == "en" ? field.LabelEn ?? field.LabelAr : field.LabelAr ?? field.LabelEn)}]";
        }

        var pdf = await RenderHtmlAsync(input.Template, new TemplateModel(values, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>()), input.Language,
            ct).ConfigureAwait(false);
        return pdf.IsFailure ? pdf.Error : new PdfFile(pdf.Value, $"{type.Code}-preview.pdf");
    }

    private async Task<Result<byte[]>> RenderHtmlAsync(PdfTemplate template, TemplateModel model, string? language, CancellationToken ct)
    {
        var lang = language == "en" ? "en" : "ar";
        var body = TemplateRenderer.Render((lang == "en" ? template.HtmlEn ?? template.HtmlAr : template.HtmlAr ?? template.HtmlEn) ?? string.Empty, model);
        var dir = lang == "ar" ? "rtl" : "ltr";
        const string Style = "body{font-family:'IBM Plex Sans Arabic',sans-serif;font-size:12pt;margin:24px}table{border-collapse:collapse;width:100%}"
            + "td,th{border:1px solid #999;padding:4px 6px;text-align:start}h1,h2{margin:0 0 8px}";
        var html = $"<!doctype html><html lang=\"{lang}\" dir=\"{dir}\"><head><meta charset=\"utf-8\"/><style>{Style}</style></head><body>{body}</body></html>";
        try
        {
            return await renderer.RenderAsync(html, ct).ConfigureAwait(false);
        }
        catch (PdfRenderingException ex)
        {
            return Error.Rule("requests.pdf.failed", ex.MessageKey);
        }
    }

    private async Task<TemplateModel> ModelAsync(TypeModel type, Request request, JsonObject masked, JsonObject process, string language, CancellationToken ct)
    {
        var ar = language != "en";
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["request.number"] = request.Number,
            ["request.title"] = ar ? request.TitleAr ?? request.TitleEn : request.TitleEn ?? request.TitleAr,
            ["request.type"] = ar ? type.NameAr ?? type.NameEn : type.NameEn ?? type.NameAr,
            ["request.status"] = RequestLinkTargets.StatusLabel(request.Status, ar),
            ["request.submittedAt"] = request.SubmittedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["today"] = clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["org.name"] = await OrgNameAsync(language, ct).ConfigureAwait(false),
        };
        if (request.RequesterEmployeeId is { } e && await employees.GetAsync(e, ct).ConfigureAwait(false) is { } emp)
        {
            values["request.requester"] = ar ? emp.NameAr ?? emp.NameEn : emp.NameEn ?? emp.NameAr;
        }

        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(StringComparer.Ordinal);
        foreach (var field in type.Form.AllFields)
        {
            if (field.Type == FieldTypes.Table && masked[field.Code] is JsonArray list)
            {
                var items = new List<IReadOnlyDictionary<string, string?>>();
                foreach (var row in list.OfType<JsonObject>())
                {
                    var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var column in field.Columns ?? [])
                    {
                        map[column.Code] = await DisplayAsync(column, row[column.Code], ar, ct).ConfigureAwait(false);
                    }

                    items.Add(map);
                }

                rows[field.Code] = items;
                continue;
            }

            values[$"field.{field.Code}"] = await DisplayAsync(field, masked[field.Code], ar, ct).ConfigureAwait(false);
        }

        foreach (var stage in type.Stages)
        {
            if (process[stage.Code] is JsonObject stageValues)
            {
                foreach (var field in RequestEngine.StageForm(stage).AllFields)
                {
                    values[$"process.{stage.Code}.{field.Code}"] = await DisplayAsync(field, stageValues[field.Code], ar, ct).ConfigureAwait(false);
                }
            }
        }

        return new TemplateModel(values, rows);
    }

    /// <summary>A value as people read it (labels of codes, names of people and units).</summary>
    public async Task<string?> DisplayAsync(FormField field, JsonNode? node, bool ar, CancellationToken ct)
    {
        if (FormDataValidator.IsEmpty(node))
        {
            return null;
        }

        var data = new FormValues(new JsonObject { ["v"] = node!.DeepClone() });
        switch (field.Type)
        {
            case FieldTypes.Checkbox:
                return data.Flag("v") ? (ar ? "نعم" : "Yes") : (ar ? "لا" : "No");
            case FieldTypes.Picklist:
            case FieldTypes.MultiPicklist:
                {
                    var codes = field.Type == FieldTypes.Picklist ? [data.Text("v")!] : data.Codes("v");
                    var labels = await lookups.GetValuesAsync(field.LookupType ?? string.Empty, true, ct).ConfigureAwait(false);
                    return string.Join(", ", codes.Select(c => labels.FirstOrDefault(l => l.Code == c) is { } l ? (ar ? l.LabelAr ?? l.LabelEn : l.LabelEn ?? l.LabelAr) ?? c : c));
                }

            case FieldTypes.Employee:
                return data.Id("v") is { } employee && await employees.GetAsync(employee, ct).ConfigureAwait(false) is { } emp ? (ar ? emp.NameAr ?? emp.NameEn : emp.NameEn ?? emp.NameAr) : null;
            case FieldTypes.Unit:
                return data.Id("v") is { } unit && await org.GetUnitAsync(unit, ct).ConfigureAwait(false) is { } u ? (ar ? u.NameAr ?? u.NameEn : u.NameEn ?? u.NameAr) : null;
            case FieldTypes.File:
                return data.Ids("v").Count.ToString(CultureInfo.InvariantCulture);
            case FieldTypes.RichText:
                return Platform.Kernel.Text.RichText.ToPlainText(data.Text("v"));
            default:
                return data.Text("v");
        }
    }

    private async Task<string?> OrgNameAsync(string language, CancellationToken ct)
    {
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        return language == "en" ? branding.OrganisationNameEn ?? branding.OrganisationNameAr : branding.OrganisationNameAr ?? branding.OrganisationNameEn;
    }
}
