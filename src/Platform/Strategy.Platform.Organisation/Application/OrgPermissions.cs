namespace Strategy.Platform.Organisation.Application;

/// <summary>Permissions of the organisation service (catalogue with labels arrives in Phase 2).</summary>
public static class OrgPermissions
{
    public const string UnitRead = "org.unit.read";
    public const string UnitManage = "org.unit.manage";
    public const string EmployeeRead = "org.employee.read";
    public const string EmployeeManage = "org.employee.manage";
    public const string ImportManage = "org.import.manage";
    public const string DelegationManage = "org.delegation.manage";

    public const string UnitResource = "org.unit";
    public const string EmployeeResource = "org.employee";
    public const string DelegationResource = "org.delegation";
}
