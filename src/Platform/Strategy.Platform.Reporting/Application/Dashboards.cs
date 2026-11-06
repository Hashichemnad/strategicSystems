using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting.Domain;
using Strategy.Platform.Reporting.Infrastructure;

namespace Strategy.Platform.Reporting.Application;

public sealed record DashboardSummaryDto(
    Guid Id, string Code, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn, string Level, string? SystemCode, string Status,
    bool IsRoleDefault, bool Pinned, bool CanEdit, int PublishedVersion);

public sealed record ReportingDashboardDto(
    Guid Id, string Code, string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn, string Level, string? SystemCode, string Status,
    DashboardDefinition Definition, DashboardDefinition? Draft, bool CanEdit, bool HasDraft, int PublishedVersion, IReadOnlyList<string> RoleKeys, uint Version);

public sealed record ReportingPeriodDto(string Kind, string From, string To, string PreviousFrom, string PreviousTo);

public sealed record DashboardDataDto(Guid Id, string Code, string TitleAr, string TitleEn, string Level, string? SystemCode, ReportingPeriodDto Period, Guid? UnitId,
    IReadOnlyList<WidgetConfig> Widgets, IReadOnlyList<WidgetDataDto> Data, IReadOnlyList<UnitNameDto> Units);

public sealed record DashboardInput(string TitleAr, string TitleEn, string? DescriptionAr, string? DescriptionEn, string Level, string? SystemCode, DashboardDefinition Definition);

public sealed record DashboardRoleInput(string RoleKey, int Sort, bool IsDefault);

public sealed record ShareInput(string PrincipalKind, string PrincipalRef);

public sealed record SavedFilterDto(Guid Id, string Name, JsonElement Filters, bool IsDefault);

public sealed record SavedFilterInput(string Name, JsonElement Filters, bool IsDefault);

public sealed record PinDto(Guid Id, string Kind, Guid RefId, string? WidgetId, int Sort, string? TitleAr, string? TitleEn, string? Code);

public sealed record PinInput(string Kind, Guid RefId, string? WidgetId);

/// <summary>Dashboards: who sees which, definitions, publishing, role defaults, sharing, pins, saved filters (ADR 0143).</summary>
public sealed class DashboardService(
    ReportingDbContext db, IAccessService access, IUserRoleQuery roles, IOrgDirectory org, IEmployeeDirectory employees, WidgetValidator validator,
    WidgetEvaluator widgets, ReportingClock clock, DepartmentMap departments, IAuditLog audit, UnitNames unitNames)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DashboardDefinition Parse(string json) => JsonSerializer.Deserialize<DashboardDefinition>(json, Json) ?? new DashboardDefinition();

    private async Task<bool> CanManageAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await access.CanAsync(user, ReportingPermissions.DashboardManage, cancellationToken: ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<string>> RoleKeysAsync(ClaimsPrincipal user, CancellationToken ct) =>
        user.UserId() is { } id ? await roles.RoleKeysOfAsync(id, user.EmployeeIdOf(), ct).ConfigureAwait(false) : [];

    /// <summary>May the user open this dashboard (published, or theirs / managed while draft)?</summary>
    public async Task<bool> CanViewAsync(Dashboard d, ClaimsPrincipal user, CancellationToken ct)
    {
        if (d.Status == DashboardStatuses.Archived)
        {
            return false;
        }

        var manage = await CanManageAsync(user, ct).ConfigureAwait(false);
        if (d.Status != DashboardStatuses.Published && !manage && d.OwnerUserId != user.UserId())
        {
            return false;
        }

        return d.Level switch
        {
            DashboardLevels.Executive => await access.CanAsync(user, ReportingPermissions.ExecutiveRead, cancellationToken: ct).ConfigureAwait(false),
            DashboardLevels.System => await access.CanAsync(user, ReportingPermissions.DashboardRead, cancellationToken: ct).ConfigureAwait(false)
                && (manage || await SystemOpenAsync(d.SystemCode, user, ct).ConfigureAwait(false)),
            DashboardLevels.Role => manage || await HoldsDashboardRoleAsync(d.Id, user, ct).ConfigureAwait(false),
            _ => d.OwnerUserId == user.UserId() || manage || await SharedWithAsync("dashboard", d.Id, user, ct).ConfigureAwait(false),
        };
    }

    /// <summary>A system dashboard shows when the viewer can see at least one of its metrics (system access + a module permission).</summary>
    private async Task<bool> SystemOpenAsync(string? systemCode, ClaimsPrincipal user, CancellationToken ct)
    {
        if (systemCode is null)
        {
            return false;
        }

        foreach (var permission in registryPermissions(systemCode))
        {
            if ((await access.GetScopeAsync(user, permission, ct).ConfigureAwait(false)).Allowed)
            {
                return true;
            }
        }

        return false;

        IEnumerable<string> registryPermissions(string system) => widgets.ScopePermissionsOf(system);
    }

    private async Task<bool> HoldsDashboardRoleAsync(Guid dashboardId, ClaimsPrincipal user, CancellationToken ct)
    {
        var keys = await RoleKeysAsync(user, ct).ConfigureAwait(false);
        return await db.DashboardRoles.AnyAsync(r => r.DashboardId == dashboardId && keys.Contains(r.RoleKey), ct).ConfigureAwait(false);
    }

    private async Task<bool> SharedWithAsync(string kind, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var shares = await db.Shares.AsNoTracking().Where(s => s.TargetKind == kind && s.TargetId == id).ToListAsync(ct).ConfigureAwait(false);
        return await MatchesAnyAsync(shares, user, ct).ConfigureAwait(false);
    }

    internal async Task<bool> MatchesAnyAsync(IReadOnlyList<Share> shares, ClaimsPrincipal user, CancellationToken ct)
    {
        if (shares.Count == 0)
        {
            return false;
        }

        var userId = user.UserId()?.ToString("D");
        if (shares.Any(s => s.PrincipalKind == SharePrincipalKinds.User && s.PrincipalRef == userId))
        {
            return true;
        }

        var keys = await RoleKeysAsync(user, ct).ConfigureAwait(false);
        if (shares.Any(s => s.PrincipalKind == SharePrincipalKinds.Role && keys.Contains(s.PrincipalRef, StringComparer.Ordinal)))
        {
            return true;
        }

        if (user.EmployeeIdOf() is { } employeeId && await employees.GetAsync(employeeId, ct).ConfigureAwait(false) is { } employee)
        {
            var path = await org.GetPathAsync(employee.UnitId, cancellationToken: ct).ConfigureAwait(false);
            var ids = path.Select(u => u.Id.ToString("D")).ToHashSet(StringComparer.Ordinal);
            return shares.Any(s => s.PrincipalKind == SharePrincipalKinds.Unit && ids.Contains(s.PrincipalRef));
        }

        return false;
    }

    public async Task<IReadOnlyList<DashboardSummaryDto>> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var all = await db.Dashboards.AsNoTracking().Where(d => d.Status != DashboardStatuses.Archived).OrderBy(d => d.Level).ThenBy(d => d.Code).ToListAsync(ct).ConfigureAwait(false);
        var keys = await RoleKeysAsync(user, ct).ConfigureAwait(false);
        var defaults = await db.DashboardRoles.AsNoTracking().Where(r => keys.Contains(r.RoleKey) && r.IsDefault).Select(r => r.DashboardId).ToListAsync(ct).ConfigureAwait(false);
        var userId = user.UserId();
        var pins = await db.Pins.AsNoTracking().Where(p => p.UserId == userId && p.Kind == "dashboard").Select(p => p.RefId).ToListAsync(ct).ConfigureAwait(false);
        var manage = await CanManageAsync(user, ct).ConfigureAwait(false);
        var result = new List<DashboardSummaryDto>();
        foreach (var d in all)
        {
            if (await CanViewAsync(d, user, ct).ConfigureAwait(false))
            {
                result.Add(new DashboardSummaryDto(d.Id, d.Code, d.TitleAr, d.TitleEn, d.DescriptionAr, d.DescriptionEn, d.Level, d.SystemCode, d.Status,
                    defaults.Contains(d.Id), pins.Contains(d.Id), CanEdit(d, user, manage), d.PublishedVersion));
            }
        }

        return result;
    }

    private static bool CanEdit(Dashboard d, ClaimsPrincipal user, bool manage) =>
        manage || (d.Level == DashboardLevels.Custom && d.OwnerUserId == user.UserId());

    private async Task<Dashboard?> FindAsync(string codeOrId, CancellationToken ct) =>
        Guid.TryParse(codeOrId, out var id)
            ? await db.Dashboards.FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false)
            : await db.Dashboards.FirstOrDefaultAsync(d => d.Code == codeOrId, ct).ConfigureAwait(false);

    public async Task<Result<ReportingDashboardDto>> GetAsync(string codeOrId, ClaimsPrincipal user, CancellationToken ct)
    {
        var d = await FindAsync(codeOrId, ct).ConfigureAwait(false);
        if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var manage = await CanManageAsync(user, ct).ConfigureAwait(false);
        var edit = CanEdit(d, user, manage);
        var roleKeys = await db.DashboardRoles.AsNoTracking().Where(r => r.DashboardId == d.Id).OrderBy(r => r.Sort).Select(r => r.RoleKey).ToListAsync(ct).ConfigureAwait(false);
        return new ReportingDashboardDto(d.Id, d.Code, d.TitleAr, d.TitleEn, d.DescriptionAr, d.DescriptionEn, d.Level, d.SystemCode, d.Status, Parse(d.DefinitionJson),
            edit && d.DraftJson is not null ? Parse(d.DraftJson) : null, edit, d.DraftJson is not null, d.PublishedVersion, roleKeys, d.Version);
    }

    /// <summary>All widget data of a dashboard in one call (the executive dashboard's &lt; 3 s budget).</summary>
    public async Task<Result<DashboardDataDto>> DataAsync(
        string codeOrId, ClaimsPrincipal user, string? kind, string? from, string? to, bool compare, Guid? unitId, bool draft, CancellationToken ct)
    {
        var d = await FindAsync(codeOrId, ct).ConfigureAwait(false);
        if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var today = await clock.TodayAsync(ct).ConfigureAwait(false);
        var settingsFiscal = await widgets.FiscalStartAsync(ct).ConfigureAwait(false);
        if (ReportPeriod.Parse(kind, from, to, today, settingsFiscal) is not { } period)
        {
            return Error.Validation([new FieldError("from", "reporting.errors.period", "reporting.errors.period")]);
        }

        IReadOnlySet<Guid>? units = null;
        if (unitId is { } u)
        {
            units = await departments.UnitAndBelowAsync(u, ct).ConfigureAwait(false);
        }

        var manage = await CanManageAsync(user, ct).ConfigureAwait(false);
        var definition = Parse(draft && CanEdit(d, user, manage) && d.DraftJson is not null ? d.DraftJson : d.DefinitionJson);
        var context = new WidgetContext(period, compare, unitId, units);
        var data = await widgets.EvaluateAsync(definition.Widgets, context, user, ct).ConfigureAwait(false);
        var previous = period.Previous();
        var unitCodes = data.SelectMany(w => w.Rows ?? []).SelectMany(r => r.Key.Where(k => k.Key == "unit").Select(k => k.Value));
        if (unitId is { } selected)
        {
            unitCodes = unitCodes.Append(selected.ToString("D"));
        }

        var names = await unitNames.ListAsync(unitCodes, ct).ConfigureAwait(false);
        return new DashboardDataDto(d.Id, d.Code, d.TitleAr, d.TitleEn, d.Level, d.SystemCode,
            new ReportingPeriodDto(period.Kind, Month(period.From), Month(period.To), Month(previous.From), Month(previous.To)), unitId, definition.Widgets, data, names);
    }

    private static string Month(DateOnly month) => month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<List<FieldError>> ValidateInputAsync(DashboardInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.TitleAr) && string.IsNullOrWhiteSpace(input.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "reporting.errors.titleRequired", "reporting.errors.titleRequired"));
        }

        if ((input.TitleAr?.Length ?? 0) > 200 || (input.TitleEn?.Length ?? 0) > 200)
        {
            errors.Add(new FieldError("titleEn", "reporting.errors.tooLong", "reporting.errors.tooLong"));
        }

        if (!DashboardLevels.All.Contains(input.Level, StringComparer.Ordinal))
        {
            errors.Add(new FieldError("level", "reporting.errors.level", "reporting.errors.level"));
        }

        if (input.Definition is null)
        {
            errors.Add(new FieldError("definition", "reporting.errors.widgetType", "reporting.errors.widgetType"));
            return errors;
        }

        errors.AddRange(await validator.ValidateAsync(input.Definition, user, ct).ConfigureAwait(false));
        return errors;
    }

    public async Task<Result<ReportingDashboardDto>> CreateAsync(DashboardInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        var errors = await ValidateInputAsync(input, user, ct).ConfigureAwait(false);
        if (errors.Count == 0 && input.Level != DashboardLevels.Custom && !await CanManageAsync(user, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var code = (input.Level == DashboardLevels.Custom ? "custom." : input.Level + ".") + Guid.CreateVersion7().ToString("N")[..12];
        var d = new Dashboard(code, input.TitleAr ?? input.TitleEn!, input.TitleEn ?? input.TitleAr!, input.Level, input.SystemCode,
            input.Level == DashboardLevels.Custom ? user.UserId() : null, JsonSerializer.Serialize(input.Definition, Json));
        d.Describe(input.TitleAr ?? input.TitleEn!, input.TitleEn ?? input.TitleAr!, input.DescriptionAr, input.DescriptionEn);
        db.Dashboards.Add(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(d.Id.ToString("D"), user, ct).ConfigureAwait(false);
    }

    public async Task<Result<ReportingDashboardDto>> UpdateAsync(Guid id, DashboardInput input, uint version, ClaimsPrincipal user, CancellationToken ct)
    {
        var d = await db.Dashboards.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (!CanEdit(d, user, await CanManageAsync(user, ct).ConfigureAwait(false)))
        {
            return Error.Forbidden();
        }

        if (d.Version != version)
        {
            return Error.Conflict();
        }

        var errors = await ValidateInputAsync(input with { Level = d.Level }, user, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        d.Describe(input.TitleAr ?? input.TitleEn!, input.TitleEn ?? input.TitleAr!, input.DescriptionAr, input.DescriptionEn);
        d.SaveDraft(JsonSerializer.Serialize(input.Definition, Json));
        d.Edited();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(d.Id.ToString("D"), user, ct).ConfigureAwait(false);
    }

    public async Task<Result<ReportingDashboardDto>> PublishAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var d = await db.Dashboards.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (!CanEdit(d, user, await CanManageAsync(user, ct).ConfigureAwait(false)))
        {
            return Error.Forbidden();
        }

        d.Publish(clock.UtcNow);
        d.Edited();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.WriteAsync(new AuditEvent("reporting.dashboard.publish", true, "reporting", "reporting.dashboard", d.Id.ToString("D"),
            new { d.Code, d.PublishedVersion }), ct).ConfigureAwait(false);
        return await GetAsync(d.Id.ToString("D"), user, ct).ConfigureAwait(false);
    }

    public async Task<Result> ArchiveAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var d = await db.Dashboards.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (!CanEdit(d, user, await CanManageAsync(user, ct).ConfigureAwait(false)) || d.Level is DashboardLevels.Executive)
        {
            return Error.Forbidden();
        }

        d.Archive();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetRolesAsync(Guid id, IReadOnlyList<DashboardRoleInput> input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await CanManageAsync(user, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var d = await db.Dashboards.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (d is null)
        {
            return Error.NotFound();
        }

        if (input.Any(r => string.IsNullOrWhiteSpace(r.RoleKey) || r.RoleKey.Length > 120 || !r.RoleKey.Contains('.', StringComparison.Ordinal)))
        {
            return Error.Validation([new FieldError("roleKey", "reporting.errors.role", "reporting.errors.role")]);
        }

        var existing = await db.DashboardRoles.Where(r => r.DashboardId == id).ToListAsync(ct).ConfigureAwait(false);
        db.DashboardRoles.RemoveRange(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var role in input.DistinctBy(r => r.RoleKey))
        {
            if (role.IsDefault)
            {
                // One default per role: the newest assignment wins.
                var others = await db.DashboardRoles.Where(r => r.RoleKey == role.RoleKey && r.IsDefault).ToListAsync(ct).ConfigureAwait(false);
                db.DashboardRoles.RemoveRange(others);
            }

            db.DashboardRoles.Add(new DashboardRole(id, role.RoleKey, role.Sort, role.IsDefault));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> ShareAsync(string kind, Guid id, IReadOnlyList<ShareInput> input, ClaimsPrincipal user, Func<Guid, CancellationToken, Task<bool>> canShare, CancellationToken ct)
    {
        if (!await canShare(id, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (input.Any(s => !SharePrincipalKinds.All.Contains(s.PrincipalKind, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(s.PrincipalRef) || s.PrincipalRef.Length > 120))
        {
            return Error.Validation([new FieldError("principal", "reporting.errors.share", "reporting.errors.share")]);
        }

        var existing = await db.Shares.Where(s => s.TargetKind == kind && s.TargetId == id).ToListAsync(ct).ConfigureAwait(false);
        db.Shares.RemoveRange(existing);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var s in input.DistinctBy(s => (s.PrincipalKind, s.PrincipalRef)))
        {
            db.Shares.Add(new Share(kind, id, s.PrincipalKind, s.PrincipalRef));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ShareInput>> SharesAsync(string kind, Guid id, CancellationToken ct) =>
        await db.Shares.AsNoTracking().Where(s => s.TargetKind == kind && s.TargetId == id).Select(s => new ShareInput(s.PrincipalKind, s.PrincipalRef))
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<bool> CanShareDashboardAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var d = await db.Dashboards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return d is not null && d.Level == DashboardLevels.Custom && CanEdit(d, user, await CanManageAsync(user, ct).ConfigureAwait(false))
            && await access.CanAsync(user, ReportingPermissions.DashboardBuild, cancellationToken: ct).ConfigureAwait(false);
    }

    // Pins and saved filters (per user)

    public async Task<IReadOnlyList<PinDto>> PinsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var pins = await db.Pins.AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.Sort).ToListAsync(ct).ConfigureAwait(false);
        var dashboardIds = pins.Where(p => p.Kind is "dashboard" or "widget").Select(p => p.RefId).ToList();
        var reportIds = pins.Where(p => p.Kind == "report").Select(p => p.RefId).ToList();
        var dashboards = await db.Dashboards.AsNoTracking().Where(d => dashboardIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct).ConfigureAwait(false);
        var reports = await db.Reports.AsNoTracking().Where(r => reportIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct).ConfigureAwait(false);
        var result = new List<PinDto>();
        foreach (var p in pins)
        {
            if (p.Kind == "report" && reports.TryGetValue(p.RefId, out var r))
            {
                result.Add(new PinDto(p.Id, p.Kind, p.RefId, null, p.Sort, r.Name, r.Name, null));
            }
            else if (dashboards.TryGetValue(p.RefId, out var d) && await CanViewAsync(d, user, ct).ConfigureAwait(false))
            {
                result.Add(new PinDto(p.Id, p.Kind, p.RefId, p.WidgetId, p.Sort, d.TitleAr, d.TitleEn, d.Code));
            }
        }

        return result;
    }

    public async Task<Result<PinDto>> PinAsync(PinInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (input.Kind is not ("dashboard" or "widget" or "report") || user.UserId() is not { } userId)
        {
            return Error.Validation([new FieldError("kind", "reporting.errors.pinKind", "reporting.errors.pinKind")]);
        }

        if (input.Kind != "report")
        {
            var d = await db.Dashboards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.RefId, ct).ConfigureAwait(false);
            if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
            {
                return Error.NotFound();
            }
        }

        var existing = await db.Pins.FirstOrDefaultAsync(p => p.UserId == userId && p.Kind == input.Kind && p.RefId == input.RefId && p.WidgetId == input.WidgetId, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            var sort = await db.Pins.Where(p => p.UserId == userId).CountAsync(ct).ConfigureAwait(false);
            existing = new UserPin(userId, input.Kind, input.RefId, input.WidgetId, sort);
            db.Pins.Add(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new PinDto(existing.Id, existing.Kind, existing.RefId, existing.WidgetId, existing.Sort, null, null, null);
    }

    public async Task<Result> UnpinAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var pin = await db.Pins.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct).ConfigureAwait(false);
        if (pin is null)
        {
            return Error.NotFound();
        }

        db.Pins.Remove(pin);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SavedFilterDto>>> FiltersAsync(Guid dashboardId, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var rows = await db.SavedFilters.AsNoTracking().Where(f => f.UserId == userId && f.DashboardId == dashboardId).OrderBy(f => f.Name).ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(f => new SavedFilterDto(f.Id, f.Name, JsonDocument.Parse(f.FiltersJson).RootElement.Clone(), f.IsDefault)).ToList();
    }

    public async Task<Result<SavedFilterDto>> SaveFilterAsync(Guid dashboardId, SavedFilterInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.UserId() is not { } userId)
        {
            return Error.Forbidden();
        }

        var d = await db.Dashboards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dashboardId, ct).ConfigureAwait(false);
        if (d is null || !await CanViewAsync(d, user, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var raw = input.Filters.GetRawText();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || raw.Length > 4000 || input.Filters.ValueKind != JsonValueKind.Object)
        {
            return Error.Validation([new FieldError("name", "reporting.errors.savedFilter", "reporting.errors.savedFilter")]);
        }

        if (input.IsDefault)
        {
            foreach (var other in await db.SavedFilters.Where(f => f.UserId == userId && f.DashboardId == dashboardId && f.IsDefault).ToListAsync(ct).ConfigureAwait(false))
            {
                other.MakeDefault(false);
            }
        }

        var filter = new SavedFilter(userId, dashboardId, input.Name.Trim(), raw, input.IsDefault);
        db.SavedFilters.Add(filter);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SavedFilterDto(filter.Id, filter.Name, input.Filters, filter.IsDefault);
    }

    public async Task<Result> DeleteFilterAsync(Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = user.UserId();
        var filter = await db.SavedFilters.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, ct).ConfigureAwait(false);
        if (filter is null)
        {
            return Error.NotFound();
        }

        db.SavedFilters.Remove(filter);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32];
}
