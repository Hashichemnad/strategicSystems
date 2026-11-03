using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

public sealed record PersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record UnitDto(Guid Id, string Code, string? NameAr, string? NameEn);

/// <summary>
/// Loads requests and projects within the caller's read scope (ApplyScope in the database query; 404 otherwise) and what is around them:
/// settings, today in the installation's time zone, people and units. Every handler starts here.
/// </summary>
internal sealed class RequestReader(
    PortfolioDbContext db, IAccessService access, ISettingsStore settings, IClock clock, IEmployeeDirectory employees, IOrgDirectory org, IUserDirectory users)
{
    public async Task<IQueryable<ProgramRequest>> VisibleAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false)
    {
        var source = tracked ? db.Requests : db.Requests.AsNoTracking();
        return await access.ApplyScopeAsync(source, user, PortfolioPermissions.RequestRead, RequestScope.Map, ct).ConfigureAwait(false);
    }

    public async Task<ProgramRequest?> FindAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await VisibleAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, ProgramRequest request, CancellationToken ct) =>
        access.CanAsync(user, permission, RequestScope.Map.RefOf(request), ct);

    public async Task<IQueryable<Project>> VisibleProjectsAsync(ClaimsPrincipal user, CancellationToken ct) =>
        await access.ApplyScopeAsync(db.Projects.AsNoTracking(), user, PortfolioPermissions.ProjectRead, ProjectScope.Map, ct).ConfigureAwait(false);

    public Task<PortfolioSettings> SettingsAsync(CancellationToken ct) => settings.GetAsync<PortfolioSettings>(ct);

    /// <summary>Today in the installation's time zone (Settings → Regional): the "current year" of the date rules.</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var zoneId = (await settings.GetAsync<RegionalSettings>(ct).ConfigureAwait(false)).TimeZone;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return clock.Today;
        }
    }

    public async Task<RequestRuleContext> RuleContextAsync(ProgramRequest? request, CancellationToken ct)
    {
        var configuration = await SettingsAsync(ct).ConfigureAwait(false);
        return new RequestRuleContext(
            await TodayAsync(ct).ConfigureAwait(false), configuration.StartYearsAhead, configuration.EndYearsAhead, configuration.ActiveCriteria,
            request?.CostEstimationUploaded ?? false, request?.ProcurementStatus == ProcurementStatuses.QuotationsUploaded);
    }

    public async Task<Dictionary<Guid, PersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new PersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public async Task<Dictionary<Guid, UnitDto>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, UnitDto>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = new UnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr);
            }
        }

        return result;
    }

    /// <summary>The head of the nearest unit at the configured department level on the unit's path (Project Owner default, SPEC §1.9).</summary>
    public async Task<Guid?> DepartmentDirectorOfAsync(Guid? unitId, CancellationToken ct)
    {
        if (unitId is not { } unit)
        {
            return null;
        }

        var level = (await settings.GetAsync<ApprovalSettings>(ct).ConfigureAwait(false)).DepartmentLevelCode;
        var path = await org.GetPathAsync(unit, cancellationToken: ct).ConfigureAwait(false);
        foreach (var node in path.Reverse())
        {
            if (node.LevelCode == level)
            {
                return await org.GetHeadOfUnitAsync(node.Id, cancellationToken: ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>Active users of the employees.</summary>
    public async Task<IReadOnlyList<Guid>> UsersOfEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken ct) =>
        (await users.GetByEmployeesAsync(employeeIds, ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => u.Id).ToList();

    /// <summary>The unit of the caller's employee (default requesting unit).</summary>
    public async Task<Guid?> UnitOfAsync(Guid? employeeId, CancellationToken ct) =>
        employeeId is { } id ? (await employees.GetAsync(id, ct).ConfigureAwait(false))?.UnitId : null;
}
