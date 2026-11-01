using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Domain;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;

namespace Strategy.Platform.Organisation.Domain;

/// <summary>Employee status codes (fixed logic, SPEC §0.1). Labels live in the translation files.</summary>
public static class EmployeeStatus
{
    public const string Active = "active";
    public const string OnLeave = "on_leave";
    public const string Left = "left";

    public static readonly IReadOnlyList<string> All = [Active, OnLeave, Left];
}

public sealed record EmployeeDetails(
    string EmployeeNo,
    string? NameAr,
    string? NameEn,
    string Email,
    string? Mobile,
    string? JobTitleAr,
    string? JobTitleEn,
    string? Grade,
    Guid UnitId,
    Guid? ManagerEmployeeId,
    DateOnly? JoinDate);

/// <summary>One record per person (SPEC §0.1). Placement history is kept in <see cref="EmployeeAssignment"/>.</summary>
public sealed class Employee : AuditableEntity
{
    public const int NumberMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 256;
    public const int MobileMaxLength = 30;
    public const int GradeMaxLength = 50;

    private Employee()
    {
    }

    public string EmployeeNo { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    /// <summary>Stored lower-cased; unique.</summary>
    public string Email { get; private set; } = string.Empty;

    public string? Mobile { get; private set; }

    public string? JobTitleAr { get; private set; }

    public string? JobTitleEn { get; private set; }

    /// <summary>Grade code (lookup in Phase 2, ADR 0014).</summary>
    public string? Grade { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public string Status { get; private set; } = EmployeeStatus.Active;

    public DateOnly? JoinDate { get; private set; }

    public string NameSearch { get; private set; } = string.Empty;

    public bool IsActive => Status != EmployeeStatus.Left;

    public static Employee Create(EmployeeDetails details)
    {
        var employee = new Employee();
        employee.Apply(details);
        return employee;
    }

    /// <summary>Applies new details; returns true when unit, manager or job title changed (a new assignment is due).</summary>
    public bool Update(EmployeeDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var placementChanged = UnitId != details.UnitId
            || ManagerEmployeeId != details.ManagerEmployeeId
            || JobTitleAr != Text.Clean(details.JobTitleAr)
            || JobTitleEn != Text.Clean(details.JobTitleEn);
        Apply(details);
        return placementChanged;
    }

    public Result ChangeStatus(string status)
    {
        if (!EmployeeStatus.All.Contains(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown employee status.");
        }

        if (Status == status)
        {
            return OrgErrors.StatusUnchanged;
        }

        Status = status;
        return Result.Success();
    }

    public static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();

    private void Apply(EmployeeDetails details)
    {
        EmployeeNo = details.EmployeeNo.Trim();
        NameAr = Text.Clean(details.NameAr);
        NameEn = Text.Clean(details.NameEn);
        Email = NormaliseEmail(details.Email);
        Mobile = Text.Clean(details.Mobile);
        JobTitleAr = Text.Clean(details.JobTitleAr);
        JobTitleEn = Text.Clean(details.JobTitleEn);
        Grade = Text.Clean(details.Grade);
        UnitId = details.UnitId;
        ManagerEmployeeId = details.ManagerEmployeeId;
        JoinDate = details.JoinDate;
        NameSearch = SearchText.Normalise(EmployeeNo, NameAr, NameEn, Email);
    }
}

/// <summary>Where an employee sat, under whom, with which title, on <c>[ValidFrom, ValidTo)</c>.</summary>
public sealed class EmployeeAssignment : AuditableEntity
{
    private EmployeeAssignment()
    {
    }

    public Guid EmployeeId { get; private set; }

    public Guid UnitId { get; private set; }

    public Guid? ManagerEmployeeId { get; private set; }

    public string? JobTitleAr { get; private set; }

    public string? JobTitleEn { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public static EmployeeAssignment Snapshot(Employee employee, DateOnly validFrom)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var assignment = new EmployeeAssignment { EmployeeId = employee.Id, ValidFrom = validFrom };
        assignment.CopyFrom(employee);
        return assignment;
    }

    /// <summary>Same rule as unit versions: a same-day change rewrites, otherwise close and open a new one.</summary>
    public EmployeeAssignment? Record(Employee employee, DateOnly effective)
    {
        ArgumentNullException.ThrowIfNull(employee);
        if (effective <= ValidFrom)
        {
            CopyFrom(employee);
            return null;
        }

        ValidTo = effective;
        return Snapshot(employee, effective);
    }

    private void CopyFrom(Employee employee)
    {
        UnitId = employee.UnitId;
        ManagerEmployeeId = employee.ManagerEmployeeId;
        JobTitleAr = employee.JobTitleAr;
        JobTitleEn = employee.JobTitleEn;
    }
}

/// <summary>Employee photos are files (Files service, purpose <c>org.employee-photo</c>): PNG / JPEG / WebP by magic number, at most 1 MB.</summary>
public static class EmployeePhotos
{
    public const int MaxBytes = 1024 * 1024;

    public static readonly FilePurpose Purpose = new(
        "org.employee-photo", "org.employee", "org", FileKinds.Images, MaxBytes, SingleFile: true);
}
