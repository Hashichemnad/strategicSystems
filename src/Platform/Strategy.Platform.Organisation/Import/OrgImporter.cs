using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Import;

public static class ImportKinds
{
    public const string Units = "units";
    public const string Employees = "employees";

    public static readonly IReadOnlyList<string> All = [Units, Employees];
}

/// <summary>One problem found in an uploaded file. <see cref="Row"/> is the sheet row number; <see cref="Column"/> a column key.</summary>
public sealed record ImportIssue(int Row, string? Column, string Code, string MessageKey, IReadOnlyDictionary<string, object?>? Params = null);

public sealed record ImportReportDto(
    string Kind,
    bool Committed,
    int TotalRows,
    int Created,
    int Updated,
    IReadOnlyList<ImportIssue> Issues);

/// <summary>Optional: provisions user accounts for imported employees flagged <c>create_user</c> (implemented by Identity).</summary>
public interface IUserProvisioning
{
    Task ProvisionAsync(IReadOnlyList<Guid> employeeIds, CancellationToken cancellationToken);
}

/// <summary>
/// Bulk import of units and employees from the Excel / CSV templates (docs/design/organisation.md). Validates every row first;
/// commit is all-or-nothing and upserts by unit code / employee number.
/// </summary>
internal sealed class OrgImporter(
    OrgDbContext db,
    OrgStructure structure,
    EmployeeRegister register,
    IEnumerable<IUserProvisioning> provisioning,
    IEnumerable<Contracts.IEmployeeLifecycleHook> lifecycleHooks)
{
    public static readonly IReadOnlyList<ImportColumn> UnitColumns =
    [
        new("code", "Code", "الرمز", Required: true),
        new("parent_code", "Parent code", "رمز الوحدة الأم"),
        new("name_ar", "Name (Arabic)", "الاسم بالعربية"),
        new("name_en", "Name (English)", "الاسم بالإنجليزية"),
        new("level_code", "Level code", "رمز المستوى", Required: true),
        new("head_employee_no", "Head (employee no.)", "المدير (الرقم الوظيفي)"),
        new("deputy_employee_no", "Deputy (employee no.)", "النائب (الرقم الوظيفي)"),
        new("cost_centre", "Cost centre", "مركز التكلفة"),
        new("active", "Active (Y/N)", "نشط (نعم/لا)"),
        new("effective_from", "Effective from (yyyy-mm-dd)", "ساري من (yyyy-mm-dd)"),
    ];

    public static readonly IReadOnlyList<ImportColumn> EmployeeColumns =
    [
        new("employee_no", "Employee no.", "الرقم الوظيفي", Required: true),
        new("name_ar", "Name (Arabic)", "الاسم بالعربية"),
        new("name_en", "Name (English)", "الاسم بالإنجليزية"),
        new("email", "Email", "البريد الإلكتروني", Required: true),
        new("mobile", "Mobile", "الجوال"),
        new("job_title_ar", "Job title (Arabic)", "المسمى الوظيفي بالعربية"),
        new("job_title_en", "Job title (English)", "المسمى الوظيفي بالإنجليزية"),
        new("grade", "Grade", "الدرجة"),
        new("unit_code", "Unit code", "رمز الوحدة", Required: true),
        new("manager_employee_no", "Manager (employee no.)", "المدير المباشر (الرقم الوظيفي)"),
        new("status", "Status (active/on_leave/left)", "الحالة (active/on_leave/left)"),
        new("join_date", "Join date (yyyy-mm-dd)", "تاريخ الالتحاق (yyyy-mm-dd)"),
        new("create_user", "Create user account (Y/N)", "إنشاء حساب مستخدم (نعم/لا)"),
    ];

    public static IReadOnlyList<ImportColumn> ColumnsOf(string kind) => kind == ImportKinds.Units ? UnitColumns : EmployeeColumns;

    public static byte[] Template(string kind) => kind == ImportKinds.Units
        ? ImportTable.Template("units", UnitColumns,
        [
            ["ORG", null, "المؤسسة", "The Organisation", "organisation", null, null, "CC-100", "Y", null],
            ["SEC-OPS", "ORG", "قطاع العمليات", "Operations Sector", "sector", null, null, null, "Y", null],
            ["DEP-FIN", "SEC-OPS", "إدارة المالية", "Finance Department", "department", null, null, "CC-210", "Y", null],
        ])
        : ImportTable.Template("employees", EmployeeColumns,
        [
            ["E0001", "سارة أحمد", "Sara Ahmed", "sara.ahmed@example.org", "+971500000001", "مديرة", "Director", "G12", "DEP-FIN", null, "active", "2020-01-05", "Y"],
            ["E0002", "خالد علي", "Khalid Ali", "khalid.ali@example.org", null, "محلل", "Analyst", "G9", "DEP-FIN", "E0001", "active", "2022-03-01", "N"],
        ]);

    public async Task<ImportReportDto> RunAsync(string kind, IReadOnlyList<ImportRow> rows, bool commit, CancellationToken cancellationToken)
    {
        var issues = new List<ImportIssue>();
        var outcome = kind == ImportKinds.Units
            ? await ImportUnitsAsync(rows, issues, cancellationToken).ConfigureAwait(false)
            : await ImportEmployeesAsync(rows, issues, cancellationToken).ConfigureAwait(false);

        var committed = false;
        if (commit && issues.Count == 0 && rows.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            committed = true;
            foreach (var employeeId in outcome.Left)
            {
                foreach (var hook in lifecycleHooks)
                {
                    await hook.EmployeeLeftAsync(employeeId, cancellationToken).ConfigureAwait(false);
                }
            }

            if (outcome.UsersToProvision.Count > 0)
            {
                foreach (var provider in provisioning)
                {
                    await provider.ProvisionAsync(outcome.UsersToProvision, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        db.ChangeTracker.Clear();
        return new ImportReportDto(kind, committed, rows.Count, outcome.Created, outcome.Updated, issues.OrderBy(i => i.Row).ToList());
    }

    private sealed record Outcome(int Created, int Updated, IReadOnlyList<Guid> UsersToProvision, IReadOnlyList<Guid>? LeftEmployees = null)
    {
        public IReadOnlyList<Guid> Left => LeftEmployees ?? [];
    }

    private async Task<Outcome> ImportUnitsAsync(IReadOnlyList<ImportRow> rows, List<ImportIssue> issues, CancellationToken cancellationToken)
    {
        var levels = await db.Levels.AsNoTracking().Where(l => l.Active).Select(l => l.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        var numbers = rows.SelectMany(r => new[] { r["head_employee_no"], r["deputy_employee_no"] }).OfType<string>().Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => numbers.Contains(e.EmployeeNo))
            .ToDictionaryAsync(e => e.EmployeeNo, e => e.Id, cancellationToken).ConfigureAwait(false);

        // 1. Row-level checks.
        var valid = new Dictionary<string, (ImportRow Row, UnitDetails Details, string? Parent, bool Active, DateOnly? Effective)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var before = issues.Count;
            var code = Required(row, "code", issues);
            var level = Required(row, "level_code", issues);
            if (row["name_ar"] is null && row["name_en"] is null)
            {
                issues.Add(new ImportIssue(row.Number, "name_ar", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
            }

            if (level is not null && !levels.Contains(level))
            {
                issues.Add(new ImportIssue(row.Number, "level_code", "org.unit.unknownLevel", "org.errors.unknownLevel"));
            }

            var head = Employee(row, "head_employee_no", employees, issues);
            var deputy = Employee(row, "deputy_employee_no", employees, issues);
            var active = YesNo(row, "active", issues) ?? true;
            var effective = Date(row, "effective_from", issues);
            Length(row, "code", OrgUnit.CodeMaxLength, issues);
            Length(row, "name_ar", OrgUnit.NameMaxLength, issues);
            Length(row, "name_en", OrgUnit.NameMaxLength, issues);
            Length(row, "cost_centre", OrgUnit.CostCentreMaxLength, issues);

            if (code is not null && valid.ContainsKey(code))
            {
                issues.Add(new ImportIssue(row.Number, "code", "import.duplicateInFile", "import.errors.duplicateInFile"));
                continue;
            }

            if (issues.Count == before && code is not null && level is not null)
            {
                valid[code] = (row, new UnitDetails(code, row["name_ar"], row["name_en"], level, head, deputy, row["cost_centre"]),
                    row["parent_code"], active, effective);
            }
        }

        if (issues.Count > 0)
        {
            return new Outcome(0, 0, []);
        }

        // 2. Parents first (parents may be in the file or already in the database); cycles are refused.
        var existing = await db.Units.ToDictionaryAsync(u => u.Code, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        var ordered = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var code in valid.Keys)
        {
            if (!Visit(code))
            {
                return new Outcome(0, 0, []);
            }
        }

        bool Visit(string code)
        {
            if (state.TryGetValue(code, out var mark))
            {
                if (mark == 1)
                {
                    issues.Add(new ImportIssue(valid[code].Row.Number, "parent_code", "org.unit.cycle", "org.errors.unitCycle"));
                    return false;
                }

                return true;
            }

            state[code] = 1;
            if (valid[code].Parent is { } parent)
            {
                if (valid.ContainsKey(parent))
                {
                    if (!Visit(parent))
                    {
                        return false;
                    }
                }
                else if (!existing.ContainsKey(parent))
                {
                    issues.Add(new ImportIssue(valid[code].Row.Number, "parent_code", "org.unit.parentNotFound", "org.errors.parentNotFound"));
                }
            }

            state[code] = 2;
            ordered.Add(code);
            return true;
        }

        if (issues.Count > 0)
        {
            return new Outcome(0, 0, []);
        }

        // 3. Apply through the same domain rules as the screens.
        int created = 0, updated = 0;
        foreach (var code in ordered)
        {
            var (row, details, parentCode, active, effective) = valid[code];
            Guid? parentId = parentCode is null ? null : (db.Units.Local.FirstOrDefault(u => u.Code == parentCode) ?? existing[parentCode]).Id;
            Result outcome;
            if (existing.TryGetValue(code, out var unit))
            {
                outcome = await structure.UpdateAsync(unit, details, effective, cancellationToken).ConfigureAwait(false);
                if (outcome.IsSuccess && unit.ParentId != parentId)
                {
                    outcome = await structure.MoveAsync(unit, parentId, effective, cancellationToken).ConfigureAwait(false);
                }

                if (outcome.IsSuccess && unit.Active != active)
                {
                    outcome = active
                        ? await structure.ActivateAsync(unit, effective, cancellationToken).ConfigureAwait(false)
                        : await structure.DeactivateAsync(unit, effective, cancellationToken).ConfigureAwait(false);
                }

                updated++;
            }
            else
            {
                var result = await structure.CreateAsync(details, parentId, effective, cancellationToken).ConfigureAwait(false);
                outcome = result;
                if (result.IsSuccess && !active)
                {
                    outcome = result.Value.Deactivate(effective ?? structure.Today);
                }

                created++;
            }

            if (outcome.IsFailure)
            {
                // Field errors (e.g. a grade that is not an active lookup code) point at their column.
                issues.AddRange(outcome.Error.Fields is { Count: > 0 } fields
                    ? fields.Select(f => new ImportIssue(row.Number, f.Field, f.Code, f.MessageKey, f.Params))
                    : [new ImportIssue(row.Number, null, outcome.Error.Code, outcome.Error.MessageKey, outcome.Error.Params)]);
            }
        }

        return new Outcome(created, updated, []);
    }

    private async Task<Outcome> ImportEmployeesAsync(IReadOnlyList<ImportRow> rows, List<ImportIssue> issues, CancellationToken cancellationToken)
    {
        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Code, u => u.Id, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        var existing = await db.Employees.ToDictionaryAsync(e => e.EmployeeNo, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);

        var valid = new Dictionary<string, (ImportRow Row, string? Manager, string Status, bool CreateUser, DateOnly? JoinDate)>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var before = issues.Count;
            var number = Required(row, "employee_no", issues);
            var email = Required(row, "email", issues);
            var unitCode = Required(row, "unit_code", issues);
            if (row["name_ar"] is null && row["name_en"] is null)
            {
                issues.Add(new ImportIssue(row.Number, "name_ar", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
            }

            if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _))
            {
                issues.Add(new ImportIssue(row.Number, "email", "validation.email", "validation.email"));
            }
            else if (email is not null && !emails.Add(Domain.Employee.NormaliseEmail(email)))
            {
                issues.Add(new ImportIssue(row.Number, "email", "import.duplicateInFile", "import.errors.duplicateInFile"));
            }

            if (unitCode is not null && !units.ContainsKey(unitCode))
            {
                issues.Add(new ImportIssue(row.Number, "unit_code", "org.employee.unitNotFound", "org.errors.unitNotFound"));
            }

            var status = row["status"] ?? EmployeeStatus.Active;
            if (!EmployeeStatus.All.Contains(status))
            {
                issues.Add(new ImportIssue(row.Number, "status", "validation.unknownCode", "validation.unknownCode"));
            }

            var createUser = YesNo(row, "create_user", issues) ?? false;
            var joinDate = Date(row, "join_date", issues);
            Length(row, "employee_no", Domain.Employee.NumberMaxLength, issues);
            Length(row, "name_ar", Domain.Employee.NameMaxLength, issues);
            Length(row, "name_en", Domain.Employee.NameMaxLength, issues);
            Length(row, "email", Domain.Employee.EmailMaxLength, issues);
            Length(row, "mobile", Domain.Employee.MobileMaxLength, issues);
            Length(row, "job_title_ar", Domain.Employee.NameMaxLength, issues);
            Length(row, "job_title_en", Domain.Employee.NameMaxLength, issues);
            Length(row, "grade", Domain.Employee.GradeMaxLength, issues);

            if (number is not null && valid.ContainsKey(number))
            {
                issues.Add(new ImportIssue(row.Number, "employee_no", "import.duplicateInFile", "import.errors.duplicateInFile"));
                continue;
            }

            if (issues.Count == before && number is not null)
            {
                valid[number] = (row, row["manager_employee_no"], status, createUser, joinDate);
            }
        }

        foreach (var (number, entry) in valid)
        {
            if (entry.Manager is { } manager && !valid.ContainsKey(manager) && !existing.ContainsKey(manager))
            {
                issues.Add(new ImportIssue(entry.Row.Number, "manager_employee_no", "org.employee.managerNotFound", "org.errors.managerNotFound"));
            }
            else if (entry.Manager == number)
            {
                issues.Add(new ImportIssue(entry.Row.Number, "manager_employee_no", "org.employee.managerCycle", "org.errors.managerCycle"));
            }
        }

        if (issues.Count > 0)
        {
            return new Outcome(0, 0, []);
        }

        // Pass 1: create / update everyone without managers (so managers in the same file exist); pass 2: set managers.
        int created = 0, updated = 0;
        var people = new Dictionary<string, Domain.Employee>(StringComparer.Ordinal);
        var provision = new List<Guid>();
        var left = new List<Guid>();
        foreach (var (number, entry) in valid)
        {
            var row = entry.Row;
            var current = existing.GetValueOrDefault(number);
            var details = new EmployeeDetails(
                number, row["name_ar"], row["name_en"], row["email"]!, row["mobile"], row["job_title_ar"], row["job_title_en"], row["grade"],
                units[row["unit_code"]!], current?.ManagerEmployeeId, entry.JoinDate);
            Result outcome;
            if (current is not null)
            {
                outcome = await register.UpdateAsync(current, details, null, cancellationToken).ConfigureAwait(false);
                people[number] = current;
                updated++;
            }
            else
            {
                var result = await register.CreateAsync(details with { ManagerEmployeeId = null }, cancellationToken).ConfigureAwait(false);
                outcome = result;
                if (result.IsSuccess)
                {
                    people[number] = result.Value;
                }

                created++;
            }

            if (outcome.IsFailure)
            {
                // Field errors (e.g. a grade that is not an active lookup code) point at their column.
                issues.AddRange(outcome.Error.Fields is { Count: > 0 } fields
                    ? fields.Select(f => new ImportIssue(row.Number, f.Field, f.Code, f.MessageKey, f.Params))
                    : [new ImportIssue(row.Number, null, outcome.Error.Code, outcome.Error.MessageKey, outcome.Error.Params)]);
            }
        }

        if (issues.Count > 0)
        {
            return new Outcome(created, updated, []);
        }

        foreach (var (number, entry) in valid)
        {
            var employee = people[number];
            var managerId = entry.Manager is null ? (Guid?)null : (people.GetValueOrDefault(entry.Manager) ?? existing[entry.Manager]).Id;
            var row = entry.Row;
            if (employee.ManagerEmployeeId != managerId)
            {
                var details = new EmployeeDetails(
                    employee.EmployeeNo, employee.NameAr, employee.NameEn, employee.Email, employee.Mobile, employee.JobTitleAr, employee.JobTitleEn,
                    employee.Grade, employee.UnitId, managerId, employee.JoinDate);
                var outcome = await register.UpdateAsync(employee, details, null, cancellationToken).ConfigureAwait(false);
                if (outcome.IsFailure)
                {
                    issues.Add(new ImportIssue(row.Number, "manager_employee_no", outcome.Error.Code, outcome.Error.MessageKey, outcome.Error.Params));
                }
            }

            if (employee.Status != entry.Status)
            {
                employee.ChangeStatus(entry.Status);
                if (entry.Status == EmployeeStatus.Left)
                {
                    left.Add(employee.Id);
                }
            }

            if (entry.CreateUser && entry.Status != EmployeeStatus.Left)
            {
                provision.Add(employee.Id);
            }
        }

        return new Outcome(created, updated, provision, left);
    }

    private static string? Required(ImportRow row, string column, List<ImportIssue> issues)
    {
        var value = row[column];
        if (value is null)
        {
            issues.Add(new ImportIssue(row.Number, column, "validation.notEmpty", "validation.notEmpty"));
        }

        return value;
    }

    private static void Length(ImportRow row, string column, int max, List<ImportIssue> issues)
    {
        if (row[column] is { } value && value.Length > max)
        {
            issues.Add(new ImportIssue(row.Number, column, "validation.maximumLength", "validation.maximumLength",
                new Dictionary<string, object?> { ["maxLength"] = max }));
        }
    }

    private static Guid? Employee(ImportRow row, string column, Dictionary<string, Guid> employees, List<ImportIssue> issues)
    {
        if (row[column] is not { } number)
        {
            return null;
        }

        if (employees.TryGetValue(number, out var id))
        {
            return id;
        }

        issues.Add(new ImportIssue(row.Number, column, "org.unit.employeeNotFound", "org.errors.employeeNotFound"));
        return null;
    }

    private static bool? YesNo(ImportRow row, string column, List<ImportIssue> issues)
    {
        try
        {
            return ImportTable.ParseYesNo(row[column]);
        }
        catch (FormatException)
        {
            issues.Add(new ImportIssue(row.Number, column, "import.yesNo", "import.errors.yesNo"));
            return null;
        }
    }

    private static DateOnly? Date(ImportRow row, string column, List<ImportIssue> issues)
    {
        try
        {
            return ImportTable.ParseDate(row[column]);
        }
        catch (FormatException)
        {
            issues.Add(new ImportIssue(row.Number, column, "import.date", "import.errors.date"));
            return null;
        }
    }
}
