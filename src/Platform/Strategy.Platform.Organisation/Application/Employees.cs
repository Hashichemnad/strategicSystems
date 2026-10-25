using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

public interface IEmployeeFields
{
    string? EmployeeNo { get; }

    string? NameAr { get; }

    string? NameEn { get; }

    string? Email { get; }

    string? Mobile { get; }

    string? JobTitleAr { get; }

    string? JobTitleEn { get; }

    string? Grade { get; }

    Guid? UnitId { get; }
}

internal static class EmployeeRules
{
    public static void AddEmployeeRules<T>(this AbstractValidator<T> validator)
        where T : IEmployeeFields
    {
        validator.RuleFor(x => x.EmployeeNo).NotEmpty().MaximumLength(Employee.NumberMaxLength);
        validator.RuleFor(x => x.NameAr).NotEmpty().When(x => string.IsNullOrWhiteSpace(x.NameEn)).WithErrorCode("validation.atLeastOneLanguage");
        validator.RuleFor(x => x.NameAr).MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(x => x.NameEn).MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(x => x.Email).NotEmpty().MaximumLength(Employee.EmailMaxLength).EmailAddress();
        validator.RuleFor(x => x.Mobile).MaximumLength(Employee.MobileMaxLength)
            .Matches(@"^\+?[0-9 ()-]{5,30}$").When(x => !string.IsNullOrWhiteSpace(x.Mobile)).WithErrorCode("validation.phoneFormat");
        validator.RuleFor(x => x.JobTitleAr).MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(x => x.JobTitleEn).MaximumLength(Employee.NameMaxLength);
        validator.RuleFor(x => x.Grade).MaximumLength(Employee.GradeMaxLength);
        validator.RuleFor(x => x.UnitId).NotEmpty();
    }
}

/// <summary>
/// Creates and updates employees with their placement history and the uniqueness / reporting-line rules.
/// Works on the tracked context without saving (used by handlers and the importer).
/// </summary>
internal sealed class EmployeeRegister(OrgDbContext db, IClock clock)
{
    public async Task<Result<Employee>> CreateAsync(EmployeeDetails details, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(null, details, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        var employee = Employee.Create(details);
        db.Employees.Add(employee);
        db.Assignments.Add(EmployeeAssignment.Snapshot(employee, details.JoinDate is { } joined && joined <= clock.Today ? joined : clock.Today));
        return employee;
    }

    public async Task<Result> UpdateAsync(Employee employee, EmployeeDetails details, DateOnly? effective, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(employee.Id, details, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check;
        }

        var effectiveDate = effective ?? clock.Today;
        if (effectiveDate > clock.Today)
        {
            return OrgErrors.EffectiveDateInFuture;
        }

        var current = db.Assignments.Local.FirstOrDefault(a => a.EmployeeId == employee.Id && a.ValidTo is null)
            ?? await db.Assignments.FirstOrDefaultAsync(a => a.EmployeeId == employee.Id && a.ValidTo == null, cancellationToken).ConfigureAwait(false);
        if (current is not null && effectiveDate < current.ValidFrom)
        {
            return OrgErrors.EffectiveDateTooEarly(current.ValidFrom);
        }

        if (employee.Update(details))
        {
            var next = current is null ? EmployeeAssignment.Snapshot(employee, effectiveDate) : current.Record(employee, effectiveDate);
            if (next is not null)
            {
                db.Assignments.Add(next);
            }
        }

        return Result.Success();
    }

    private async Task<Result> CheckAsync(Guid? employeeId, EmployeeDetails details, CancellationToken cancellationToken)
    {
        var number = details.EmployeeNo.Trim();
        var email = Employee.NormaliseEmail(details.Email);
        if (db.Employees.Local.Any(e => e.EmployeeNo == number && e.Id != employeeId)
            || await db.Employees.AnyAsync(e => e.EmployeeNo == number && e.Id != employeeId, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.DuplicateEmployeeNo;
        }

        if (db.Employees.Local.Any(e => e.Email == email && e.Id != employeeId)
            || await db.Employees.AnyAsync(e => e.Email == email && e.Id != employeeId, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.DuplicateEmail;
        }

        var unit = db.Units.Local.FirstOrDefault(u => u.Id == details.UnitId)
            ?? await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == details.UnitId, cancellationToken).ConfigureAwait(false);
        if (unit is null)
        {
            return OrgErrors.UnitNotFound;
        }

        if (!unit.Active)
        {
            return OrgErrors.UnitInactive;
        }

        return details.ManagerEmployeeId is { } managerId
            ? await CheckManagerAsync(employeeId, managerId, cancellationToken).ConfigureAwait(false)
            : Result.Success();
    }

    /// <summary>The manager must exist and must not be the employee or report (directly or not) to the employee.</summary>
    private async Task<Result> CheckManagerAsync(Guid? employeeId, Guid managerId, CancellationToken cancellationToken)
    {
        if (managerId == employeeId)
        {
            return OrgErrors.ManagerCycle;
        }

        Guid? next = managerId;
        var seen = new HashSet<Guid>();
        var first = true;
        while (next is { } current && seen.Add(current))
        {
            var manager = db.Employees.Local.FirstOrDefault(e => e.Id == current)
                ?? await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == current, cancellationToken).ConfigureAwait(false);
            if (manager is null)
            {
                return first ? OrgErrors.ManagerNotFound : Result.Success();
            }

            if (employeeId is not null && manager.ManagerEmployeeId == employeeId)
            {
                return OrgErrors.ManagerCycle;
            }

            first = false;
            next = manager.ManagerEmployeeId;
        }

        return Result.Success();
    }
}

// ---- Commands ----------------------------------------------------------------------------------------------

public sealed record CreateEmployee(
    string? EmployeeNo,
    string? NameAr,
    string? NameEn,
    string? Email,
    string? Mobile,
    string? JobTitleAr,
    string? JobTitleEn,
    string? Grade,
    Guid? UnitId,
    Guid? ManagerEmployeeId,
    DateOnly? JoinDate) : ICommand<EmployeeDto>, IEmployeeFields;

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployee>
{
    public CreateEmployeeValidator() => this.AddEmployeeRules();
}

internal sealed class CreateEmployeeHandler(OrgDbContext db, EmployeeRegister register, EmployeeReader reader)
    : ICommandHandler<CreateEmployee, EmployeeDto>
{
    public async Task<Result<EmployeeDto>> HandleAsync(CreateEmployee command, CancellationToken cancellationToken)
    {
        var created = await register.CreateAsync(
            new EmployeeDetails(command.EmployeeNo!, command.NameAr, command.NameEn, command.Email!, command.Mobile, command.JobTitleAr,
                command.JobTitleEn, command.Grade, command.UnitId!.Value, command.ManagerEmployeeId, command.JoinDate),
            cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await reader.GetAsync(created.Value.Id, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record UpdateEmployee(
    Guid Id,
    string? EmployeeNo,
    string? NameAr,
    string? NameEn,
    string? Email,
    string? Mobile,
    string? JobTitleAr,
    string? JobTitleEn,
    string? Grade,
    Guid? UnitId,
    Guid? ManagerEmployeeId,
    DateOnly? JoinDate,
    DateOnly? EffectiveDate,
    uint Version) : ICommand<EmployeeDto>, IEmployeeFields;

public sealed class UpdateEmployeeValidator : AbstractValidator<UpdateEmployee>
{
    public UpdateEmployeeValidator() => this.AddEmployeeRules();
}

internal sealed class UpdateEmployeeHandler(EmployeeChange change, EmployeeRegister register) : ICommandHandler<UpdateEmployee, EmployeeDto>
{
    public Task<Result<EmployeeDto>> HandleAsync(UpdateEmployee command, CancellationToken cancellationToken) =>
        change.ApplyAsync(command.Id, command.Version, (employee, ct) => register.UpdateAsync(
            employee,
            new EmployeeDetails(command.EmployeeNo!, command.NameAr, command.NameEn, command.Email!, command.Mobile, command.JobTitleAr,
                command.JobTitleEn, command.Grade, command.UnitId!.Value, command.ManagerEmployeeId, command.JoinDate),
            command.EffectiveDate,
            ct), cancellationToken);
}

public sealed record ChangeEmployeeStatus(Guid Id, string? Status, uint Version) : ICommand<EmployeeDto>;

public sealed class ChangeEmployeeStatusValidator : AbstractValidator<ChangeEmployeeStatus>
{
    public ChangeEmployeeStatusValidator() =>
        RuleFor(x => x.Status).NotEmpty().Must(s => EmployeeStatus.All.Contains(s!)).WithErrorCode("validation.unknownCode");
}

internal sealed class ChangeEmployeeStatusHandler(EmployeeChange change, IEnumerable<IEmployeeLifecycleHook> hooks)
    : ICommandHandler<ChangeEmployeeStatus, EmployeeDto>
{
    public async Task<Result<EmployeeDto>> HandleAsync(ChangeEmployeeStatus command, CancellationToken cancellationToken)
    {
        var result = await change.ApplyAsync(command.Id, command.Version, (employee, _) =>
            Task.FromResult(employee.ChangeStatus(command.Status!)), cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && command.Status == EmployeeStatus.Left)
        {
            foreach (var hook in hooks)
            {
                await hook.EmployeeLeftAsync(command.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }
}

/// <summary>Load → record access check (404) → change → save with the client's row version (409).</summary>
internal sealed class EmployeeChange(OrgDbContext db, EmployeeReader reader, IAccessService access, IRequestContext context)
{
    public async Task<Result<EmployeeDto>> ApplyAsync(
        Guid id, uint expectedVersion, Func<Employee, CancellationToken, Task<Result>> change, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (employee is null
            || !await access.CanAsync(context.User, OrgPermissions.EmployeeManage, new ResourceRef(OrgPermissions.EmployeeResource, id), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        db.Entry(employee).Property(x => x.Version).OriginalValue = expectedVersion;
        var outcome = await change(employee, cancellationToken).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await reader.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }
}

// ---- Queries -----------------------------------------------------------------------------------------------

public sealed record ListEmployees(PageRequest Paging, string? Search, Guid? UnitId, bool IncludeDescendants, string? Status)
    : IQuery<PagedResult<EmployeeListItemDto>>;

public sealed class ListEmployeesValidator : AbstractValidator<ListEmployees>
{
    public ListEmployeesValidator()
    {
        RuleFor(x => x.Paging.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(x => x.Paging.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(x => x.Paging.SortOrder!.Value.Field)
            .Must(field => ListEmployeesHandler.SortableFields.Contains(field))
            .When(x => x.Paging.SortOrder is not null)
            .WithErrorCode("validation.unknownSortField")
            .OverridePropertyName("sort");
        RuleFor(x => x.Status).Must(s => EmployeeStatus.All.Contains(s!)).When(x => x.Status is not null).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.Search).MaximumLength(200);
    }
}

internal sealed class ListEmployeesHandler(OrgDbContext db, IAccessService access, IRequestContext context)
    : IQueryHandler<ListEmployees, PagedResult<EmployeeListItemDto>>
{
    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.Ordinal) { "employeeNo", "nameAr", "nameEn", "email", "status", "createdAt" };

    public async Task<Result<PagedResult<EmployeeListItemDto>>> HandleAsync(ListEmployees query, CancellationToken cancellationToken)
    {
        var employees = access.ApplyScope(db.Employees.AsNoTracking(), context.User, OrgPermissions.EmployeeRead);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + EscapeLike(SearchText.Normalise(query.Search)) + "%";
            employees = employees.Where(e => EF.Functions.ILike(e.NameSearch, pattern, "\\"));
        }

        if (query.UnitId is { } unitId)
        {
            if (query.IncludeDescendants)
            {
                var prefix = await db.Units.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Path).FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false) ?? "/none/";
                var subtree = db.Units.Where(u => u.Path.StartsWith(prefix)).Select(u => u.Id);
                employees = employees.Where(e => subtree.Contains(e.UnitId));
            }
            else
            {
                employees = employees.Where(e => e.UnitId == unitId);
            }
        }

        if (query.Status is not null)
        {
            employees = employees.Where(e => e.Status == query.Status);
        }

        var total = await employees.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await Sort(employees, query.Paging.SortOrder).Skip(query.Paging.Skip).Take(query.Paging.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var unitIds = page.Select(e => e.UnitId).Distinct().ToList();
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken)
            .ConfigureAwait(false);
        var employeeIds = page.Select(e => e.Id).ToList();
        var withPhoto = (await db.Photos.AsNoTracking().Where(p => employeeIds.Contains(p.EmployeeId)).Select(p => p.EmployeeId)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        var items = page.Select(e => new EmployeeListItemDto(
            e.Id, e.EmployeeNo, e.NameAr, e.NameEn, e.Email, e.JobTitleAr, e.JobTitleEn, e.UnitId, units[e.UnitId].Code,
            units[e.UnitId].NameAr, units[e.UnitId].NameEn, e.Status, withPhoto.Contains(e.Id))).ToList();
        return new PagedResult<EmployeeListItemDto>(items, query.Paging.Page, query.Paging.PageSize, total);
    }

    private static IQueryable<Employee> Sort(IQueryable<Employee> items, (string Field, bool Descending)? order) =>
        order switch
        {
            ("employeeNo", var desc) => desc ? items.OrderByDescending(x => x.EmployeeNo) : items.OrderBy(x => x.EmployeeNo),
            ("nameAr", var desc) => desc ? items.OrderByDescending(x => x.NameAr) : items.OrderBy(x => x.NameAr),
            ("nameEn", var desc) => desc ? items.OrderByDescending(x => x.NameEn) : items.OrderBy(x => x.NameEn),
            ("email", var desc) => desc ? items.OrderByDescending(x => x.Email) : items.OrderBy(x => x.Email),
            ("status", var desc) => desc ? items.OrderByDescending(x => x.Status) : items.OrderBy(x => x.Status),
            ("createdAt", true) => items.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
            _ => items.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
        };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}

public sealed record GetEmployee(Guid Id) : IQuery<EmployeeDto>;

internal sealed class GetEmployeeHandler(EmployeeReader reader) : IQueryHandler<GetEmployee, EmployeeDto>
{
    public Task<Result<EmployeeDto>> HandleAsync(GetEmployee query, CancellationToken cancellationToken) => reader.GetAsync(query.Id, cancellationToken);
}

public sealed record GetEmployeeHistory(Guid Id) : IQuery<IReadOnlyList<EmployeeAssignmentDto>>;

internal sealed class GetEmployeeHistoryHandler(OrgDbContext db, IAccessService access, IRequestContext context, OrgReader orgReader)
    : IQueryHandler<GetEmployeeHistory, IReadOnlyList<EmployeeAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeAssignmentDto>>> HandleAsync(GetEmployeeHistory query, CancellationToken cancellationToken)
    {
        var visible = access.ApplyScope(db.Employees.AsNoTracking(), context.User, OrgPermissions.EmployeeRead);
        if (!await visible.AnyAsync(e => e.Id == query.Id, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var assignments = await db.Assignments.AsNoTracking().Where(a => a.EmployeeId == query.Id)
            .OrderByDescending(a => a.ValidFrom).ToListAsync(cancellationToken).ConfigureAwait(false);
        var managers = await orgReader.EmployeeRefsAsync(assignments.Select(a => a.ManagerEmployeeId), cancellationToken).ConfigureAwait(false);
        var unitIds = assignments.Select(a => a.UnitId).Distinct().ToList();
        var units = await db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken)
            .ConfigureAwait(false);
        return assignments.Select(a => new EmployeeAssignmentDto(
            a.ValidFrom, a.ValidTo, OrgMapping.PathItem(units[a.UnitId]), OrgReader.Lookup(managers, a.ManagerEmployeeId),
            a.JobTitleAr, a.JobTitleEn)).ToList();
    }
}

internal sealed class EmployeeReader(OrgDbContext db, IAccessService access, IRequestContext context, OrgReader orgReader)
{
    public async Task<Result<EmployeeDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await access.ApplyScope(db.Employees.AsNoTracking(), context.User, OrgPermissions.EmployeeRead)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound();
        }

        var unit = await db.Units.AsNoTracking().FirstAsync(u => u.Id == employee.UnitId, cancellationToken).ConfigureAwait(false);
        var managers = await orgReader.EmployeeRefsAsync([employee.ManagerEmployeeId], cancellationToken).ConfigureAwait(false);
        var hasPhoto = await db.Photos.AnyAsync(p => p.EmployeeId == id, cancellationToken).ConfigureAwait(false);
        return new EmployeeDto(
            employee.Id, employee.EmployeeNo, employee.NameAr, employee.NameEn, employee.Email, employee.Mobile, employee.JobTitleAr,
            employee.JobTitleEn, employee.Grade, employee.UnitId, OrgMapping.PathItem(unit), OrgReader.Lookup(managers, employee.ManagerEmployeeId),
            employee.Status, employee.JoinDate, hasPhoto, employee.CreatedAt, employee.UpdatedAt, employee.Version);
    }
}
