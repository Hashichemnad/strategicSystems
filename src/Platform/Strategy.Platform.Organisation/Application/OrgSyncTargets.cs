using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

/// <summary>
/// Sync targets of Organisation (ADR 0202, 0204): units are written through <see cref="OrgStructure"/> (hierarchy, versions, code rules);
/// employees are HR master data and are only <b>matched</b> to source people, never created by a sync.
/// </summary>
internal static class OrgSyncTargets
{
    public static IServiceCollection AddOrgSyncTargets(this IServiceCollection services)
    {
        services.AddSyncTarget<OrgUnitSyncWriter>(SyncTarget.Custom(OrgPermissions.UnitResource, "org", "وحدة تنظيمية", "Organisation unit")
            .Text("code", nameof(OrgUnit.Code), required: true, maxLength: OrgUnit.CodeMaxLength)
            .Text("nameAr", nameof(OrgUnit.NameAr), maxLength: OrgUnit.NameMaxLength)
            .Text("nameEn", nameof(OrgUnit.NameEn), maxLength: OrgUnit.NameMaxLength)
            .Text("levelCode", nameof(OrgUnit.LevelCode), required: true)
            .Reference("parentId", nameof(OrgUnit.ParentId), OrgPermissions.UnitResource)
            .Person("headEmployeeId", nameof(OrgUnit.HeadEmployeeId))
            .Person("deputyEmployeeId", nameof(OrgUnit.DeputyEmployeeId))
            .Text("costCentre", nameof(OrgUnit.CostCentre))
            .Bool("active", nameof(OrgUnit.Active)));
        services.AddScoped<IEmployeeMatcher, EmployeeMatcher>();
        return services;
    }
}

/// <summary>Creates / updates / moves / (de)activates a unit from synced values in one transaction with its external reference.</summary>
internal sealed class OrgUnitSyncWriter(OrgDbContext db, OrgStructure structure) : ISyncTargetWriter
{
    public string TargetType => OrgPermissions.UnitResource;

    public async Task<Result<Guid>> WriteAsync(SyncWrite write, Func<object, Guid, Task<Result>> linkInTransaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(linkInTransaction);
        T? Get<T>(string key) => write.Values.TryGetValue(key, out var v) && v is T t ? t : default;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var unit = write.ExistingId is { } id ? await db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false) : null;
        var details = new UnitDetails(
            Get<string>("code") ?? unit?.Code ?? string.Empty,
            write.Values.ContainsKey("nameAr") ? Get<string>("nameAr") : unit?.NameAr,
            write.Values.ContainsKey("nameEn") ? Get<string>("nameEn") : unit?.NameEn,
            Get<string>("levelCode") ?? unit?.LevelCode ?? string.Empty,
            write.Values.ContainsKey("headEmployeeId") ? Get<Guid?>("headEmployeeId") : unit?.HeadEmployeeId,
            write.Values.ContainsKey("deputyEmployeeId") ? Get<Guid?>("deputyEmployeeId") : unit?.DeputyEmployeeId,
            write.Values.ContainsKey("costCentre") ? Get<string>("costCentre") : unit?.CostCentre);
        var parentId = write.Values.ContainsKey("parentId") ? Get<Guid?>("parentId") : unit?.ParentId;
        Result outcome;
        if (unit is null)
        {
            var created = await structure.CreateAsync(details, parentId, null, cancellationToken).ConfigureAwait(false);
            outcome = created.IsSuccess ? Result.Success() : created.Error!;
            unit = created.IsSuccess ? created.Value : null;
        }
        else
        {
            outcome = await structure.UpdateAsync(unit, details, null, cancellationToken).ConfigureAwait(false);
            if (outcome.IsSuccess && parentId != unit.ParentId)
            {
                outcome = await structure.MoveAsync(unit, parentId, null, cancellationToken).ConfigureAwait(false);
            }
        }

        if (outcome.IsSuccess && unit is not null && write.Values.TryGetValue("active", out var active) && active is bool wanted && wanted != unit.Active)
        {
            outcome = wanted ? await structure.ActivateAsync(unit, null, cancellationToken).ConfigureAwait(false)
                : await structure.DeactivateAsync(unit, null, cancellationToken).ConfigureAwait(false);
        }

        if (outcome.IsFailure || unit is null)
        {
            return outcome.Error ?? Error.Rule("integration.sync.writeFailed", "integration.sync.exceptions.writeFailed");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var linked = await linkInTransaction(db, unit.Id).ConfigureAwait(false);
        if (linked.IsFailure)
        {
            return linked.Error!;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return unit.Id;
    }
}

/// <summary>Employee look-up for people matching (ADR 0204). A Federation ID is compared with the e-mail and the employee number.</summary>
internal sealed class EmployeeMatcher(OrgDbContext db) : IEmployeeMatcher
{
    public async Task<Guid?> FindAsync(string key, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Case-insensitive equality: ILIKE with every wildcard escaped.
        var pattern = value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        var query = db.Employees.AsNoTracking();
        var match = key switch
        {
            "email" => query.Where(e => EF.Functions.ILike(e.Email, pattern, "\\")),
            "employeeNo" => query.Where(e => EF.Functions.ILike(e.EmployeeNo, pattern, "\\")),
            "federationId" => query.Where(e => EF.Functions.ILike(e.Email, pattern, "\\") || EF.Functions.ILike(e.EmployeeNo, pattern, "\\")),
            _ => query.Where(_ => false),
        };
        var ids = await match.Select(e => e.Id).Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 1 ? ids[0] : null; // ambiguous matches are not guessed
    }
}
