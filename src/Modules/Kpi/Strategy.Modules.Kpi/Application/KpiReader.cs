using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Kpi.Application;

public sealed record KpiPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record KpiUnitDto(Guid Id, string? Code, string? NameAr, string? NameEn);

/// <summary>Loads cards and results the user may read (404 otherwise), names of people and units, settings.</summary>
internal sealed class KpiReader(KpiDbContext db, IAccessService access, IEmployeeDirectory employees, IOrgDirectory org, ISettingsStore settings)
{
    public async Task<IQueryable<KpiCard>> CardsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await access.ApplyScopeAsync(tracked ? db.Cards : db.Cards.AsNoTracking(), user, KpiPermissions.CardRead, KpiScope.Card, ct).ConfigureAwait(false);

    public async Task<KpiCard?> CardAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await CardsAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, KpiCard card, CancellationToken ct) =>
        access.CanAsync(user, permission, KpiScope.Card.RefOf(card), ct);

    public async Task<IQueryable<KpiResult>> ResultsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false, string permission = KpiPermissions.ResultRead) =>
        await access.ApplyScopeAsync(tracked ? db.Results : db.Results.AsNoTracking(), user, permission, KpiScope.Result, ct).ConfigureAwait(false);

    public async Task<KpiResult?> ResultAsync(Guid id, ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await (await ResultsAsync(user, ct, tracked).ConfigureAwait(false)).FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, KpiResult result, CancellationToken ct) =>
        access.CanAsync(user, permission, KpiScope.Result.RefOf(result), ct);

    public Task<bool> CanAsync(ClaimsPrincipal user, string permission, CancellationToken ct) => access.CanAsync(user, permission, null, ct);

    public Task<KpiSettings> SettingsAsync(CancellationToken ct) => settings.GetAsync<KpiSettings>(ct);

    public async Task<Dictionary<Guid, KpiPersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var people = await employees.GetManyAsync(list, ct).ConfigureAwait(false);
        return people.Values.ToDictionary(e => e.Id, e => new KpiPersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr));
    }

    public async Task<Dictionary<Guid, KpiUnitDto>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, KpiUnitDto>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = new KpiUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr);
            }
        }

        return result;
    }

    public static KpiPersonDto? Person(Guid? id, IReadOnlyDictionary<Guid, KpiPersonDto> people) =>
        id is { } i ? people.GetValueOrDefault(i) ?? new KpiPersonDto(i, null, null) : null;

    public static KpiUnitDto? Unit(Guid? id, IReadOnlyDictionary<Guid, KpiUnitDto> units) =>
        id is { } i ? units.GetValueOrDefault(i) ?? new KpiUnitDto(i, null, null, null) : null;

    /// <summary>The unit and every unit below it (department scorecards, filters).</summary>
    public Task<IReadOnlyList<Guid>> UnitAndBelowAsync(Guid unitId, CancellationToken ct) => org.GetSelfAndDescendantIdsAsync(unitId, ct);

    public Task<Guid?> HeadOfUnitAsync(Guid unitId, CancellationToken ct) => org.GetHeadOfUnitAsync(unitId, null, ct);

    public Task<EmployeeReference?> EmployeeAsync(Guid id, CancellationToken ct) => employees.GetAsync(id, ct);
}
