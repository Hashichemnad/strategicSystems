using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Application;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Reporting.Api;

public sealed record MetricDimensionDto(string Code, string? Lookup, IReadOnlyList<DimensionLabelDto> Labels);

public sealed record MetricCatalogueDto(
    string Code, string SystemCode, string LabelAr, string LabelEn, string Kind, string Aggregation, string ValueKind, bool? HigherIsBetter,
    IReadOnlyList<MetricDimensionDto> Dimensions, bool Live);

public sealed record ListCatalogueDto(string Code, string SystemCode, string LabelAr, string LabelEn);

public sealed record ReportingCatalogueDto(IReadOnlyList<string> WidgetTypes, IReadOnlyList<MetricCatalogueDto> Metrics, IReadOnlyList<ListCatalogueDto> Lists);

public sealed record DashboardBody(DashboardInput Dashboard, uint Version);

public sealed record ReportRunInput(ReportDefinition Definition, int? Page, int? PageSize, string? Language);

public sealed record ReportExportInput(ReportDefinition Definition, string Format, string? Language, string? Title);

public sealed record SavedReportBody(SavedReportInput Report, uint? Version);

public sealed record ScheduleBody(ScheduleInput Schedule, uint? Version);

public sealed record ReportingSettingsInput(ReportingSettings Settings, uint Version);

public sealed record RefreshStatusDto(string SystemCode, DateTimeOffset? At, string? Kind, int Facts, string? Error);

public sealed record BriefRequest(string? Kind, string? From, string? To, Guid? UnitId);

/// <summary><c>/api/v1/reporting</c> (docs/design/reporting.md): every figure is filtered by the caller's access in the engine and the modules.</summary>
internal static class ReportingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/catalogue", CatalogueAsync).WithName("GetReportingCatalogue").RequirePermission(ReportingPermissions.DashboardRead);

        // Dashboards
        group.MapGet("/dashboards", async (ClaimsPrincipal user, DashboardService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(user, ct)))
            .WithName("ListDashboards").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapGet("/dashboards/{key}", async (string key, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.GetAsync(key, user, ct)).ToOk())
            .WithName("GetDashboard").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapGet("/dashboards/{key}/data", async (string key, string? kind, string? from, string? to, bool? compare, Guid? unit, bool? draft, ClaimsPrincipal user,
                DashboardService s, CancellationToken ct) => (await s.DataAsync(key, user, kind, from, to, compare ?? true, unit, draft ?? false, ct)).ToOk())
            .WithName("GetDashboardData").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapGet("/dashboards/{key}/export", ExportDashboardAsync).WithName("ExportDashboard").RequirePermission(ReportingPermissions.ReportExport);
        group.MapPost("/dashboards", async (DashboardInput body, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.CreateAsync(body, user, ct)).ToOk())
            .WithName("CreateDashboard").RequirePermission(ReportingPermissions.DashboardBuild);
        group.MapPut("/dashboards/{id:guid}", async (Guid id, DashboardBody body, ClaimsPrincipal user, DashboardService s, CancellationToken ct) =>
                (await s.UpdateAsync(id, body.Dashboard, body.Version, user, ct)).ToOk())
            .WithName("UpdateDashboard").RequirePermission(ReportingPermissions.DashboardBuild);
        group.MapPost("/dashboards/{id:guid}/publish", async (Guid id, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.PublishAsync(id, user, ct)).ToOk())
            .WithName("PublishDashboard").RequirePermission(ReportingPermissions.DashboardBuild);
        group.MapPost("/dashboards/{id:guid}/archive", async (Guid id, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.ArchiveAsync(id, user, ct)).ToNoContent())
            .WithName("ArchiveDashboard").RequirePermission(ReportingPermissions.DashboardBuild);
        group.MapPut("/dashboards/{id:guid}/roles", async (Guid id, List<DashboardRoleInput> body, ClaimsPrincipal user, DashboardService s, CancellationToken ct) =>
                (await s.SetRolesAsync(id, body, user, ct)).ToNoContent())
            .WithName("SetDashboardRoles").RequirePermission(ReportingPermissions.DashboardManage);
        group.MapGet("/dashboards/{id:guid}/shares", async Task<Results<Ok<IReadOnlyList<ShareInput>>, ProblemHttpResult>> (Guid id, ClaimsPrincipal user, DashboardService s, CancellationToken ct) =>
                await s.CanShareDashboardAsync(id, user, ct) ? TypedResults.Ok(await s.SharesAsync("dashboard", id, ct)) : Error.NotFound().ToProblem())
            .WithName("ListDashboardShares").RequirePermission(ReportingPermissions.DashboardBuild);
        group.MapPut("/dashboards/{id:guid}/shares", async (Guid id, List<ShareInput> body, ClaimsPrincipal user, DashboardService s, CancellationToken ct) =>
                (await s.ShareAsync("dashboard", id, body, user, (i, c) => s.CanShareDashboardAsync(i, user, c), ct)).ToNoContent())
            .WithName("ShareDashboard").RequirePermission(ReportingPermissions.DashboardBuild);
        group.MapGet("/dashboards/{id:guid}/filters", async (Guid id, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.FiltersAsync(id, user, ct)).ToOk())
            .WithName("ListSavedFilters").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapPost("/dashboards/{id:guid}/filters", async (Guid id, SavedFilterInput body, ClaimsPrincipal user, DashboardService s, CancellationToken ct) =>
                (await s.SaveFilterAsync(id, body, user, ct)).ToOk())
            .WithName("SaveFilter").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapDelete("/filters/{id:guid}", async (Guid id, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.DeleteFilterAsync(id, user, ct)).ToNoContent())
            .WithName("DeleteSavedFilter").RequirePermission(ReportingPermissions.DashboardRead);

        // Pins and My Work
        group.MapGet("/pins", async (ClaimsPrincipal user, DashboardService s, CancellationToken ct) => TypedResults.Ok(await s.PinsAsync(user, ct)))
            .WithName("ListPins").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapPost("/pins", async (PinInput body, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.PinAsync(body, user, ct)).ToOk())
            .WithName("Pin").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapDelete("/pins/{id:guid}", async (Guid id, ClaimsPrincipal user, DashboardService s, CancellationToken ct) => (await s.UnpinAsync(id, user, ct)).ToNoContent())
            .WithName("Unpin").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapGet("/my-work/cards", async (ClaimsPrincipal user, MyWorkCardsService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(user, ct)))
            .WithName("GetMyWorkCards").RequirePermission(ReportingPermissions.DashboardRead);
        group.MapPut("/my-work/layout", async (List<MyWorkLayoutItem> body, ClaimsPrincipal user, MyWorkCardsService s, CancellationToken ct) =>
                (await s.SaveLayoutAsync(user, body, ct)).ToNoContent())
            .WithName("SaveMyWorkLayout").RequirePermission(ReportingPermissions.DashboardRead);

        // Report builder
        group.MapGet("/datasets", async (ClaimsPrincipal user, ReportService s, CancellationToken ct) => TypedResults.Ok(await s.DatasetsAsync(user, ct)))
            .WithName("ListDatasets").RequirePermission(ReportingPermissions.ReportRun);
        group.MapPost("/reports/run", async (ReportRunInput body, ClaimsPrincipal user, ReportService s, CancellationToken ct) =>
                (await s.RunAsync(body.Definition, body.Page ?? 1, body.PageSize ?? 50, body.Language ?? "ar", user, ct)).ToOk())
            .WithName("RunReport").RequirePermission(ReportingPermissions.ReportRun);
        group.MapPost("/reports/export", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (ReportExportInput body, ClaimsPrincipal user, ReportService s, CancellationToken ct) =>
            {
                var file = await s.ExportAsync(body.Definition, body.Format, body.Language ?? "ar", body.Title ?? body.Definition?.Dataset ?? string.Empty, user, ct);
                return file.IsSuccess ? TypedResults.File(file.Value.Content, file.Value.ContentType, file.Value.FileName) : file.Error.ToProblem();
            })
            .WithName("ExportReport").RequirePermission(ReportingPermissions.ReportExport);
        group.MapGet("/reports", async (ClaimsPrincipal user, ReportService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(user, ct)))
            .WithName("ListReports").RequirePermission(ReportingPermissions.ReportRun);
        group.MapGet("/reports/{id:guid}", async (Guid id, ClaimsPrincipal user, ReportService s, CancellationToken ct) => (await s.GetAsync(id, user, ct)).ToOk())
            .WithName("GetReport").RequirePermission(ReportingPermissions.ReportRun);
        group.MapPost("/reports", async (SavedReportInput body, ClaimsPrincipal user, ReportService s, CancellationToken ct) => (await s.SaveAsync(null, body, null, user, ct)).ToOk())
            .WithName("CreateReport").RequirePermission(ReportingPermissions.ReportBuild);
        group.MapPut("/reports/{id:guid}", async (Guid id, SavedReportBody body, ClaimsPrincipal user, ReportService s, CancellationToken ct) =>
                (await s.SaveAsync(id, body.Report, body.Version, user, ct)).ToOk())
            .WithName("UpdateReport").RequirePermission(ReportingPermissions.ReportBuild);
        group.MapPost("/reports/{id:guid}/archive", async (Guid id, ClaimsPrincipal user, ReportService s, CancellationToken ct) => (await s.ArchiveAsync(id, user, ct)).ToNoContent())
            .WithName("ArchiveReport").RequirePermission(ReportingPermissions.ReportBuild);
        group.MapGet("/reports/{id:guid}/shares", async Task<Results<Ok<IReadOnlyList<ShareInput>>, ProblemHttpResult>> (Guid id, ClaimsPrincipal user, ReportService r, DashboardService s, CancellationToken ct) =>
                await r.CanShareAsync(id, user, ct) ? TypedResults.Ok(await s.SharesAsync("report", id, ct)) : Error.NotFound().ToProblem())
            .WithName("ListReportShares").RequirePermission(ReportingPermissions.ReportShare);
        group.MapPut("/reports/{id:guid}/shares", async (Guid id, List<ShareInput> body, ClaimsPrincipal user, ReportService r, DashboardService s, CancellationToken ct) =>
                (await s.ShareAsync("report", id, body, user, (i, c) => r.CanShareAsync(i, user, c), ct)).ToNoContent())
            .WithName("ShareReport").RequirePermission(ReportingPermissions.ReportShare);

        // Schedules
        group.MapGet("/schedules", async (ClaimsPrincipal user, ScheduleService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(user, ct)))
            .WithName("ListSchedules").RequirePermission(ReportingPermissions.ScheduleManage);
        group.MapPost("/schedules", async (ScheduleInput body, ClaimsPrincipal user, ScheduleService s, CancellationToken ct) => (await s.SaveAsync(null, body, null, user, ct)).ToOk())
            .WithName("CreateSchedule").RequirePermission(ReportingPermissions.ScheduleManage);
        group.MapPut("/schedules/{id:guid}", async (Guid id, ScheduleBody body, ClaimsPrincipal user, ScheduleService s, CancellationToken ct) =>
                (await s.SaveAsync(id, body.Schedule, body.Version, user, ct)).ToOk())
            .WithName("UpdateSchedule").RequirePermission(ReportingPermissions.ScheduleManage);
        group.MapGet("/schedules/{id:guid}/deliveries", async (Guid id, ClaimsPrincipal user, ScheduleService s, CancellationToken ct) => (await s.DeliveriesAsync(id, user, ct)).ToOk())
            .WithName("ListDeliveries").RequirePermission(ReportingPermissions.ScheduleManage);

        // AI executive brief
        group.MapPost("/brief", async (BriefRequest body, ClaimsPrincipal user, BriefService s, CancellationToken ct) =>
                (await s.GenerateAsync(user, body.Kind, body.From, body.To, body.UnitId, ct)).ToOk())
            .WithName("GenerateExecutiveBrief").RequirePermission(ReportingPermissions.BriefGenerate);
        group.MapPost("/briefs", async (BriefInput body, ClaimsPrincipal user, BriefService s, CancellationToken ct) => (await s.SaveAsync(body, user, ct)).ToOk())
            .WithName("SaveExecutiveBrief").RequirePermission(ReportingPermissions.BriefGenerate);
        group.MapGet("/briefs", async (ClaimsPrincipal user, BriefService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(user, ct)))
            .WithName("ListExecutiveBriefs").RequirePermission(ReportingPermissions.BriefGenerate);

        // Settings and refresh
        group.MapGet("/settings", async (ISettingsEditor editor, CancellationToken ct) => TypedResults.Ok(await editor.GetDocumentAsync<ReportingSettings>(ct)))
            .WithName("GetReportingSettings").RequirePermission(ReportingPermissions.SettingsManage);
        group.MapPut("/settings", async (ReportingSettingsInput body, ISettingsEditor editor, MetricRegistry registry, CancellationToken ct) =>
                (await editor.SaveAsync(body.Settings, body.Version, s => ValidateSettings(s, registry), ct)).ToOk())
            .WithName("SaveReportingSettings").RequirePermission(ReportingPermissions.SettingsManage);
        group.MapPost("/refresh", async (ReportingChangeTracker tracker, MetricRegistry registry, IAuditLog audit, CancellationToken ct) =>
            {
                foreach (var system in registry.MetricSources.Select(m => m.SystemCode))
                {
                    tracker.Mark(system);
                }

                await audit.WriteAsync(new AuditEvent("reporting.refresh.request", true, ReportingPermissions.SystemCode), ct);
                return TypedResults.Accepted((string?)null);
            })
            .WithName("RequestReportingRefresh").RequirePermission(ReportingPermissions.Refresh);
        group.MapGet("/refresh", async (FactStatus status, CancellationToken ct) => TypedResults.Ok(await status.ListAsync(ct)))
            .WithName("GetReportingRefreshStatus").RequirePermission(ReportingPermissions.Refresh);
    }

    internal static List<FieldError> ValidateSettings(ReportingSettings s, MetricRegistry registry)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(s.DepartmentLevelCode) || s.DepartmentLevelCode.Length > 50)
        {
            errors.Add(new FieldError("departmentLevelCode", "reporting.errors.departmentLevel", "reporting.errors.departmentLevel"));
        }

        if (s.FiscalStartMonth is < 1 or > 12)
        {
            errors.Add(new FieldError("fiscalStartMonth", "reporting.errors.fiscalStart", "reporting.errors.fiscalStart"));
        }

        if (s.RefreshMinutes is < 1 or > 1440 || s.ExportRowLimit is < 100 or > 500_000 || s.AttachmentLimitMb is < 1 or > 50 || s.DeliveryRetentionDays is < 1 or > 365)
        {
            errors.Add(new FieldError("limits", "reporting.errors.limits", "reporting.errors.limits"));
        }

        if (s.DeliveryMode is not ("attachment" or "link"))
        {
            errors.Add(new FieldError("deliveryMode", "reporting.errors.deliveryMode", "reporting.errors.deliveryMode"));
        }

        if (s.Scorecard.Count > 20 || s.Scorecard.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count() != s.Scorecard.Count)
        {
            errors.Add(new FieldError("scorecard", "reporting.errors.scorecard", "reporting.errors.scorecard"));
        }

        foreach (var (column, index) in s.Scorecard.Select((c, i) => (c, i)))
        {
            if (string.IsNullOrWhiteSpace(column.Code) || !registry.TryMetric(column.Metric, out var metric, out _)
                || column.Filters.Keys.Any(k => k != "unit" && !metric.Dimensions.Contains(k, StringComparer.Ordinal))
                || (column.HigherIsBetter ? column.Green < column.Amber : column.Green > column.Amber))
            {
                errors.Add(new FieldError($"scorecard[{index}]", "reporting.errors.scorecardColumn", "reporting.errors.scorecardColumn"));
            }
        }

        return errors;
    }

    private static async Task<Ok<ReportingCatalogueDto>> CatalogueAsync(ClaimsPrincipal user, MetricRegistry registry, MetricEvaluator evaluator, IAccessService access,
        ILookupService lookups, CancellationToken ct)
    {
        var metrics = new List<MetricCatalogueDto>();
        foreach (var m in registry.Metrics.OrderBy(m => m.SystemCode, StringComparer.Ordinal).ThenBy(m => m.Code, StringComparer.Ordinal))
        {
            if (await evaluator.IsLiveAsync(user, m.Code, ct) is not { } live)
            {
                continue;
            }

            var dims = new List<MetricDimensionDto>();
            foreach (var d in m.Dimensions)
            {
                var labels = new List<DimensionLabelDto>();
                string? lookup = null;
                if (m.DimensionLabels?.TryGetValue(d, out var fixedLabels) == true)
                {
                    labels.AddRange(fixedLabels.Select(l => new DimensionLabelDto(d, l.Key, l.Value.Ar, l.Value.En)));
                }
                else if (m.DimensionLookups?.TryGetValue(d, out var type) == true)
                {
                    lookup = type;
                    labels.AddRange((await lookups.GetValuesAsync(type, cancellationToken: ct)).Select(v => new DimensionLabelDto(d, v.Code, v.LabelAr, v.LabelEn)));
                }

                dims.Add(new MetricDimensionDto(d, lookup, labels));
            }

            metrics.Add(new MetricCatalogueDto(m.Code, m.SystemCode, m.LabelAr, m.LabelEn, m.Kind.ToString().ToLowerInvariant(), m.Aggregation.ToString().ToLowerInvariant(),
                m.ValueKind.ToString().ToLowerInvariant(), m.HigherIsBetter, dims, live));
        }

        var lists = new List<ListCatalogueDto>();
        foreach (var l in registry.Lists)
        {
            if (await access.CanAsync(user, l.Permission, cancellationToken: ct))
            {
                lists.Add(new ListCatalogueDto(l.Code, l.SystemCode, l.LabelAr, l.LabelEn));
            }
        }

        return TypedResults.Ok(new ReportingCatalogueDto(WidgetTypes.All, metrics, lists));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportDashboardAsync(
        string key, string format, string? kind, string? from, string? to, Guid? unit, string? lang, ClaimsPrincipal user, DashboardService dashboards,
        DashboardRenderer renderer, UnitNames unitNames, IAuditLog audit, CancellationToken ct)
    {
        var language = lang == "en" ? "en" : "ar";
        var data = await dashboards.DataAsync(key, user, kind, from, to, true, unit, false, ct);
        if (data.IsFailure)
        {
            return data.Error.ToProblem();
        }

        var names = await unitNames.ForAsync(data.Value, language, ct);
        Result<ExportedFile> file = format switch
        {
            "xlsx" => DashboardRenderer.Excel(data.Value, language, names),
            "pdf" => await renderer.PdfAsync(data.Value, language, names, ct),
            _ => Error.Validation([new FieldError("format", "reporting.errors.format", "reporting.errors.format")]),
        };
        if (file.IsFailure)
        {
            return file.Error.ToProblem();
        }

        await audit.WriteAsync(new AuditEvent("reporting.dashboard.export", true, ReportingPermissions.SystemCode, "reporting.dashboard", data.Value.Id.ToString("D"),
            new { format, data.Value.Period, unit }), ct);
        return TypedResults.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }
}
