using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Strategy.Platform.Access.Domain;
using Strategy.Platform.Access.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Access.Application;

/// <summary>Bulk role transfer, effective access of one user, and the access review report (SPEC §0.2).</summary>
internal sealed partial class AccessReporting(
    AccessDbContext db,
    AccessModelCache models,
    UserAccessLoader loader,
    AccessAdministration administration,
    IUserDirectory users,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    IEnumerable<IRoleTransferHook> hooks,
    IClock clock,
    ILogger<AccessReporting> logger)
{
    // ---- Bulk role transfer ----

    public async Task<Result<TransferDto>> TransferAsync(TransferInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var roleIds = input.RoleIds?.Distinct().ToList() ?? [];
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (roleIds.Count == 0 || roles.Count != roleIds.Count)
        {
            errors.Add(new FieldError("roleIds", "validation.unknownReference", "validation.unknownReference"));
        }

        var fromUsers = input.FromUserIds?.Distinct().ToList() ?? [];
        if ((fromUsers.Count == 0) == (input.FromUnitId is null))
        {
            errors.Add(new FieldError("fromUserIds", "access.errors.transferSource", "access.errors.transferSource"));
        }

        if ((input.ToUserId is null) == (input.ToUnitId is null))
        {
            errors.Add(new FieldError("toUserId", "access.errors.transferTarget", "access.errors.transferTarget"));
        }

        var targetKind = input.ToUserId is not null ? PrincipalKinds.User : PrincipalKinds.Unit;
        var targetId = input.ToUserId ?? input.ToUnitId;
        if (targetId is not null)
        {
            await administration.ValidatePrincipalAsync(targetKind, targetId, errors, ct).ConfigureAwait(false);
        }

        if (input.FromUnitId is { } fromUnit && await org.GetUnitAsync(fromUnit, ct).ConfigureAwait(false) is null)
        {
            errors.Add(new FieldError("fromUnitId", "validation.unknownReference", "validation.unknownReference"));
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            errors.Add(new FieldError("reason", "validation.notEmpty", "validation.notEmpty"));
        }
        else if (input.Reason.Length > 1000)
        {
            errors.Add(new FieldError("reason", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = 1000 }));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var effective = input.EffectiveDate ?? clock.Today;
        var query = db.Assignments.Where(a => roleIds.Contains(a.RoleId) && a.ValidFrom <= effective && (a.ValidTo == null || a.ValidTo >= effective));
        query = input.FromUnitId is { } unitId
            ? query.Where(a => a.PrincipalKind == PrincipalKinds.Unit && a.PrincipalId == unitId)
            : query.Where(a => a.PrincipalKind == PrincipalKinds.User && fromUsers.Contains(a.PrincipalId!.Value));
        var moving = await query.ToListAsync(ct).ConfigureAwait(false);
        if (moving.Count == 0)
        {
            return Error.Rule("access.transfer.nothingToMove", "access.errors.nothingToMove");
        }

        var transferId = Guid.CreateVersion7();
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var assignment in moving)
        {
            assignment.End(effective.AddDays(-1));
        }

        foreach (var roleId in moving.Select(a => a.RoleId).Distinct())
        {
            var held = await db.Assignments.AnyAsync(
                a => a.RoleId == roleId && a.PrincipalKind == targetKind && a.PrincipalId == targetId && (a.ValidTo == null || a.ValidTo >= effective), ct)
                .ConfigureAwait(false);
            if (!held)
            {
                db.Assignments.Add(RoleAssignment.Create(roleId, targetKind, targetId, effective, null, input.Reason, transferId));
            }
        }

        var roleKeys = roles.Select(r => $"{r.SystemCode}.{r.Code}").Order(StringComparer.Ordinal).ToList();
        var from = input.FromUnitId is { } source ? $"unit:{source}" : string.Join(",", fromUsers.Select(u => $"user:{u}"));
        var to = $"{targetKind}:{targetId}";
        db.Transfers.Add(RoleTransferLog.Create(transferId, string.Join(",", roleKeys), from, to, moving.Count, input.Reason!.Trim()));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var transfer = new RoleTransfer(transferId, roleKeys, fromUsers, input.ToUserId is { } toUser ? [toUser] : []);
        foreach (var hook in hooks)
        {
            await hook.OnRoleTransferredAsync(transfer, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        models.Invalidate();
        LogTransfer(logger, transferId, from, to, moving.Count);
        var saved = await db.Transfers.AsNoTracking().FirstAsync(t => t.Id == transferId, ct).ConfigureAwait(false);
        return ToDto(saved);
    }

    public async Task<IReadOnlyList<TransferDto>> ListTransfersAsync(CancellationToken ct) =>
        (await db.Transfers.AsNoTracking().OrderByDescending(t => t.CreatedAt).Take(200).ToListAsync(ct).ConfigureAwait(false)).Select(ToDto).ToList();

    // ---- Effective access of one user ----

    public async Task<Result<EffectiveAccessDto>> EffectiveAsync(Guid userId, CancellationToken ct)
    {
        if (!(await users.GetManyAsync([userId], ct).ConfigureAwait(false)).TryGetValue(userId, out var user))
        {
            return Error.NotFound();
        }

        var (_, access) = await loader.LoadAsync(user.Id, user.EmployeeId, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var recordRoles = await db.RecordRoles.AsNoTracking()
            .Where(g => g.ValidFrom <= now && (g.ValidTo == null || g.ValidTo > now)
                && ((g.PrincipalKind == RecordPrincipalKinds.User && g.PrincipalId == user.Id)
                    || (g.PrincipalKind == RecordPrincipalKinds.Employee && user.EmployeeId != null && g.PrincipalId == user.EmployeeId)))
            .OrderBy(g => g.ResourceType).ThenBy(g => g.ValidFrom).Take(1000)
            .Select(g => new EffectiveRecordRoleDto(g.ResourceType, g.ResourceId, g.RoleCode, g.ValidFrom, g.ValidTo))
            .ToListAsync(ct).ConfigureAwait(false);
        return new EffectiveAccessDto(
            user.Id,
            access.Systems.Order(StringComparer.Ordinal).ToList(),
            access.Roles.Select(r => r.Key).Order(StringComparer.Ordinal).ToList(),
            access.Permissions.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new EffectivePermissionDto(p.Key, p.Value.OrderByDescending(s => s).Select(s => s.ToCode()).ToList()))
                .ToList(),
            recordRoles,
            access.RequiresMfa);
    }

    // ---- Access review ----

    public async Task<IReadOnlyList<AccessReviewRowDto>> ReviewAsync(string? systemCode, Guid? unitId, Guid? userId, CancellationToken ct)
    {
        var model = await models.GetAsync(ct).ConfigureAwait(false);
        var today = clock.Today;
        var allUsers = userId is { } only
            ? (await users.GetManyAsync([only], ct).ConfigureAwait(false)).Values.ToList()
            : (await users.ListAllAsync(ct).ConfigureAwait(false)).ToList();
        var employeeMap = await employees.GetManyAsync(allUsers.Select(u => u.EmployeeId).OfType<Guid>().Distinct().ToList(), ct).ConfigureAwait(false);
        IReadOnlyList<Guid>? unitFilter = unitId is { } filter ? await org.GetSelfAndDescendantIdsAsync(filter, ct).ConfigureAwait(false) : null;

        var unitPaths = new Dictionary<Guid, IReadOnlyList<UnitReference>>();
        async Task<IReadOnlyList<UnitReference>> PathAsync(Guid unit)
        {
            if (!unitPaths.TryGetValue(unit, out var path))
            {
                unitPaths[unit] = path = await org.GetPathAsync(unit, cancellationToken: ct).ConfigureAwait(false);
            }

            return path;
        }

        var roleNames = await db.Roles.AsNoTracking().Select(r => new { r.Id, r.NameAr, r.NameEn }).ToDictionaryAsync(r => r.Id, ct)
            .ConfigureAwait(false);
        var rows = new List<AccessReviewRowDto>();
        foreach (var user in allUsers.OrderBy(u => u.UserName, StringComparer.Ordinal))
        {
            var employee = user.EmployeeId is { } e ? employeeMap.GetValueOrDefault(e) : null;
            if (unitFilter is not null && (employee is null || !unitFilter.Contains(employee.UnitId)))
            {
                continue;
            }

            var path = employee is null || employee.Status == "left" ? [] : await PathAsync(employee.UnitId).ConfigureAwait(false);
            var ancestors = path.Select(p => p.Id).ToHashSet();
            bool Matches(string kind, Guid? id) => kind switch
            {
                PrincipalKinds.Everyone => true,
                PrincipalKinds.User => id == user.Id,
                PrincipalKinds.Unit => id is { } unit && ancestors.Contains(unit),
                _ => false,
            };

            var systems = model.SystemGrants.Where(g => Matches(g.PrincipalKind, g.PrincipalId)).Select(g => g.SystemCode).ToHashSet(StringComparer.Ordinal);
            foreach (var assignment in model.Assignments.Where(a => a.ActiveOn(today) && Matches(a.PrincipalKind, a.PrincipalId)))
            {
                if (!model.Roles.TryGetValue(assignment.RoleId, out var role) || !role.Active || (systemCode is not null && role.SystemCode != systemCode))
                {
                    continue;
                }

                var system = model.Systems.GetValueOrDefault(role.SystemCode);
                var roleEntity = roleNames[role.Id];
                rows.Add(new AccessReviewRowDto(
                    user.Id, user.UserName, user.Active, employee?.NameAr, employee?.NameEn, (path.Count > 0 ? path[^1].Code : null),
                    role.SystemCode, system?.Platform == true || systems.Contains(role.SystemCode), role.Code, roleEntity.NameAr, roleEntity.NameEn,
                    assignment.PrincipalKind, assignment.ValidFrom, assignment.ValidTo,
                    role.Permissions.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new RolePermissionDto(p.Key, p.Value.ToCode())).ToList()));
            }
        }

        return rows;
    }

    public async Task<byte[]> ExportReviewAsync(string? systemCode, Guid? unitId, string language, CancellationToken ct)
    {
        var rows = await ReviewAsync(systemCode, unitId, null, ct).ConfigureAwait(false);
        var arabic = language == "ar";
        string[] headers = arabic
            ? ["المستخدم", "الموظف", "الوحدة", "النظام", "الوصول إلى النظام", "الدور", "المصدر", "من", "إلى", "الصلاحيات (النطاق)", "الحساب نشط"]
            : ["User", "Employee", "Unit", "System", "System access", "Role", "Source", "From", "To", "Permissions (scope)", "Account active"];
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(arabic ? "مراجعة الصلاحيات" : "Access review");
        sheet.RightToLeft = arabic;
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.UserName;
            sheet.Cell(r, 2).Value = (arabic ? row.EmployeeNameAr ?? row.EmployeeNameEn : row.EmployeeNameEn ?? row.EmployeeNameAr) ?? string.Empty;
            sheet.Cell(r, 3).Value = row.UnitCode ?? string.Empty;
            sheet.Cell(r, 4).Value = row.SystemCode;
            sheet.Cell(r, 5).Value = Yes(row.SystemAccess, arabic);
            sheet.Cell(r, 6).Value = (arabic ? row.RoleNameAr ?? row.RoleNameEn : row.RoleNameEn ?? row.RoleNameAr) ?? row.RoleCode;
            sheet.Cell(r, 7).Value = Source(row.Source, arabic);
            sheet.Cell(r, 8).Value = row.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sheet.Cell(r, 9).Value = row.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            sheet.Cell(r, 10).Value = string.Join("; ", row.Permissions.Select(p => $"{p.Permission} ({p.Scope})"));
            sheet.Cell(r, 11).Value = Yes(row.UserActive, arabic);
            r++;
        }

        sheet.Columns().AdjustToContents(1, Math.Min(r, 200));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string Yes(bool value, bool arabic) => value ? (arabic ? "نعم" : "Yes") : (arabic ? "لا" : "No");

    private static string Source(string kind, bool arabic) => kind switch
    {
        PrincipalKinds.User => arabic ? "مباشر" : "Direct",
        PrincipalKinds.Unit => arabic ? "الوحدة" : "Unit",
        _ => arabic ? "الجميع" : "Everyone",
    };

    private static TransferDto ToDto(RoleTransferLog t) => new(t.Id, t.RoleKeys, t.FromDescription, t.ToDescription, t.MovedCount, t.Reason, t.CreatedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role transfer {TransferId} from {From} to {To}: {Count} assignment(s) moved")]
    private static partial void LogTransfer(ILogger logger, Guid transferId, string from, string to, int count);
}

public sealed record TransferInput(
    IReadOnlyList<Guid>? RoleIds, IReadOnlyList<Guid>? FromUserIds, Guid? FromUnitId, Guid? ToUserId, Guid? ToUnitId, DateOnly? EffectiveDate, string? Reason);
