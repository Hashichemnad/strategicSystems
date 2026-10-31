using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Organisation.Domain;

/// <summary>Business-rule errors of the organisation domain (codes are stable; keys live in the <c>org</c> namespace).</summary>
public static class OrgErrors
{
    public static Error UnitCycle => Error.Rule("org.unit.cycle", "org.errors.unitCycle");

    public static Error LevelOrder => Error.Rule("org.unit.levelOrder", "org.errors.levelOrder");

    public static Error UnknownLevel => Error.Rule("org.unit.unknownLevel", "org.errors.unknownLevel");

    public static Error ParentInactive => Error.Rule("org.unit.parentInactive", "org.errors.parentInactive");

    public static Error DuplicateUnitCode => Error.Conflict("org.unit.duplicateCode", "org.errors.duplicateUnitCode");

    public static Error EffectiveDateTooEarly(DateOnly earliest) =>
        Error.Rule("org.unit.effectiveDateTooEarly", "org.errors.effectiveDateTooEarly",
            new Dictionary<string, object?> { ["earliest"] = earliest.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });

    public static Error EffectiveDateInFuture => Error.Rule("org.unit.effectiveDateInFuture", "org.errors.effectiveDateInFuture");

    public static Error UnitHasActiveChildren => Error.Rule("org.unit.hasActiveChildren", "org.errors.unitHasActiveChildren");

    public static Error UnitHasActiveEmployees => Error.Rule("org.unit.hasActiveEmployees", "org.errors.unitHasActiveEmployees");

    public static Error UnitAlreadyActive => Error.Rule("org.unit.alreadyActive", "org.errors.unitAlreadyActive");

    public static Error UnitAlreadyInactive => Error.Rule("org.unit.alreadyInactive", "org.errors.unitAlreadyInactive");

    public static Error UnitInactive => Error.Rule("org.unit.inactive", "org.errors.unitInactive");

    public static Error DuplicateLevelCode => Error.Conflict("org.level.duplicateCode", "org.errors.duplicateLevelCode");

    public static Error DuplicateEmployeeNo => Error.Conflict("org.employee.duplicateNumber", "org.errors.duplicateEmployeeNo");

    public static Error DuplicateEmail => Error.Conflict("org.employee.duplicateEmail", "org.errors.duplicateEmail");

    public static Error ManagerCycle => Error.Rule("org.employee.managerCycle", "org.errors.managerCycle");

    public static Error ManagerNotFound => Error.Rule("org.employee.managerNotFound", "org.errors.managerNotFound");

    public static Error UnknownGrade => Error.Validation([new FieldError("grade", "org.employee.unknownGrade", "org.errors.unknownGrade")]);

    public static Error UnitNotFound => Error.Rule("org.employee.unitNotFound", "org.errors.unitNotFound");

    public static Error EmployeeNotFound => Error.Rule("org.unit.employeeNotFound", "org.errors.employeeNotFound");

    public static Error StatusUnchanged => Error.Rule("org.employee.statusUnchanged", "org.errors.statusUnchanged");

    public static Error DelegationSelf => Error.Rule("org.delegation.self", "org.errors.delegationSelf");

    public static Error DelegateInactive => Error.Rule("org.delegation.delegateInactive", "org.errors.delegateInactive");

    public static Error DelegationInPast => Error.Rule("org.delegation.inPast", "org.errors.delegationInPast");

    public static Error DelegationOverlap => Error.Rule("org.delegation.overlap", "org.errors.delegationOverlap");

    public static Error DelegationCycle => Error.Rule("org.delegation.cycle", "org.errors.delegationCycle");

    public static Error DelegationNotActive => Error.Rule("org.delegation.notActive", "org.errors.delegationNotActive");

    public static Error PhotoInvalid => Error.Rule("org.photo.invalid", "org.errors.photoInvalid");

    public static Error PhotoTooLarge => Error.Rule("org.photo.tooLarge", "org.errors.photoTooLarge",
        new Dictionary<string, object?> { ["maxKb"] = EmployeePhoto.MaxBytes / 1024 });

    public static Error NoEmployeeLinked => Error.Rule("org.delegation.noEmployee", "org.errors.noEmployeeLinked");
}
