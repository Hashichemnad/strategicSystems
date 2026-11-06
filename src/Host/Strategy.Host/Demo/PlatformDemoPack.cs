using Microsoft.AspNetCore.Identity;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Identity.Application;
using Strategy.Platform.Identity.Domain;
using Strategy.Platform.Identity.Infrastructure;
using Strategy.Platform.Kernel.Demo;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Domain;

namespace Strategy.Host.Demo;

/// <summary>
/// The platform part of the demo pack (ADR 0153): a demo organisation (organisation → sector → departments → section), eight demo employees with
/// sign-in accounts (the given password, e-mail confirmed, two-step verification set up at first sign-in where a role requires it), system
/// access to every system and the office roles of the demo Director General. Codes start with <c>DEMO-</c>, names say "(demo)".
/// Removal: employees marked as left, accounts deactivated and locked, units deactivated (deepest first).
/// </summary>
internal sealed class PlatformDemoPack : IDemoPackContributor
{
    public const string ContributorCode = "platform";
    private const string UnitType = "org.unit";
    private const string EmployeeType = "org.employee";
    private const string UserType = "identity.user";

    private sealed record UnitSeed(string Key, string Code, string NameAr, string NameEn, string Level, string? Parent);

    private sealed record PersonSeed(string Key, string Number, string NameAr, string NameEn, string TitleAr, string TitleEn, string Unit, string? Manager);

    private static readonly UnitSeed[] UnitSeeds =
    [
        new("org", "DEMO-ORG", "المؤسسة التجريبية (عرض)", "Demo Organisation (demo)", "organisation", null),
        new("sector", "DEMO-SEC", "قطاع الخدمات المؤسسية (عرض)", "Corporate Services Sector (demo)", "sector", "org"),
        new("strategy", "DEMO-STR", "إدارة الاستراتيجية والأداء (عرض)", "Strategy & Performance Department (demo)", "department", "sector"),
        new("finance", "DEMO-FIN", "إدارة المالية (عرض)", "Finance Department (demo)", "department", "sector"),
        new("audit", "DEMO-AUD", "مكتب التدقيق الداخلي (عرض)", "Internal Audit Office (demo)", "department", "org"),
        new("pmo", "DEMO-PMO", "قسم إدارة المشاريع (عرض)", "Project Management Office (demo)", "section", "strategy"),
    ];

    /// <summary>Keys other contributors use: <c>director</c>, <c>strategyHead</c>, <c>pm</c>, <c>analyst</c>, <c>financeHead</c>, <c>accountant</c>, <c>auditHead</c>, <c>auditor</c>.</summary>
    private static readonly PersonSeed[] PersonSeeds =
    [
        new("director", "DEMO-001", "سارة المنصوري (عرض)", "Sara Al Mansoori (demo)", "المديرة العامة", "Director General", "org", null),
        new("strategyHead", "DEMO-002", "خالد الشامسي (عرض)", "Khalid Al Shamsi (demo)", "مدير إدارة الاستراتيجية", "Strategy Director", "strategy", "director"),
        new("pm", "DEMO-003", "ليلى الحوسني (عرض)", "Layla Al Hosani (demo)", "مديرة مشاريع", "Project Manager", "pmo", "strategyHead"),
        new("analyst", "DEMO-004", "عمر الكعبي (عرض)", "Omar Al Kaabi (demo)", "محلل أداء", "Performance Analyst", "strategy", "strategyHead"),
        new("financeHead", "DEMO-005", "نورة الظاهري (عرض)", "Noura Al Dhaheri (demo)", "مديرة الإدارة المالية", "Finance Director", "finance", "director"),
        new("accountant", "DEMO-006", "يوسف النعيمي (عرض)", "Yousef Al Nuaimi (demo)", "محاسب", "Accountant", "finance", "financeHead"),
        new("auditHead", "DEMO-007", "مريم البلوشي (عرض)", "Maryam Al Balushi (demo)", "رئيسة التدقيق الداخلي", "Chief Audit Executive", "audit", "director"),
        new("auditor", "DEMO-008", "حمد المهيري (عرض)", "Hamad Al Muhairi (demo)", "مدقق داخلي", "Internal Auditor", "audit", "auditHead"),
    ];

    /// <summary>Units headed by a demo person (approver resolution: department director, head of unit).</summary>
    private static readonly (string Unit, string Head)[] Heads =
    [
        ("org", "director"), ("sector", "director"), ("strategy", "strategyHead"), ("finance", "financeHead"), ("audit", "auditHead"), ("pmo", "pm"),
    ];

    private static readonly string[] Systems = ["portfolio", "kpi", "risk", "teams", "tasks", "requests", "docreg", "ip", "bench", "cap", "auditmgmt"];

    /// <summary>The demo Director General holds the office roles of every system (as the Development seed's administrator does).</summary>
    private static readonly string[] DirectorRoles =
    [
        "portfolio.administrator", "portfolio.pm-office", "portfolio.dg-office", "portfolio.budget-section", "portfolio.administration",
        "portfolio.finance-section", "portfolio.gm-office", "kpi.strategy-office", "kpi.administrator", "requests.office", "requests.type-administrator",
        "docreg.office", "docreg.administrator", "ip.office", "ip.administrator", "risk.risk-office", "risk.risk-committee", "cap.cap-office",
        "bench.bench-office", "auditmgmt.audit-office", "auditmgmt.audit-committee", "reporting.executive-viewer", "reporting.report-author",
    ];

    /// <summary>Office roles of the other demo people, so that several people take part in the demo flows.</summary>
    private static readonly (string Person, string Role)[] PersonRoles =
    [
        ("strategyHead", "kpi.strategy-office"), ("pm", "portfolio.pm-office"), ("auditHead", "auditmgmt.audit-office"),
        ("auditor", "auditmgmt.audit-office"), ("financeHead", "portfolio.budget-section"),
        ("strategyHead", "risk.coordinator"), ("financeHead", "risk.coordinator"), ("auditHead", "risk.coordinator"),
    ];

    public string Code => ContributorCode;

    public int Order => 0;

    public static string Email(string key) => $"{key.ToLowerInvariant()}@demo.example.org";

    public static IReadOnlyDictionary<string, DemoPerson> PeopleFrom(IReadOnlyList<DemoRecordRef> records)
    {
        var units = UnitsFrom(records);
        var people = new Dictionary<string, DemoPerson>(StringComparer.Ordinal);
        foreach (var seed in PersonSeeds)
        {
            var employee = records.FirstOrDefault(r => r.Key == "employee:" + seed.Key);
            var user = records.FirstOrDefault(r => r.Key == "user:" + seed.Key);
            if (employee is not null && user is not null && units.TryGetValue(seed.Unit, out var unit))
            {
                people[seed.Key] = new DemoPerson(seed.Key, user.ResourceId, employee.ResourceId, unit, Email(seed.Key), seed.NameAr, seed.NameEn);
            }
        }

        return people;
    }

    public static IReadOnlyDictionary<string, Guid> UnitsFrom(IReadOnlyList<DemoRecordRef> records) =>
        records.Where(r => r.Key?.StartsWith("unit:", StringComparison.Ordinal) == true)
            .ToDictionary(r => r.Key!["unit:".Length..], r => r.ResourceId, StringComparer.Ordinal);

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.Password))
        {
            throw new DemoPackException("demo.passwordRequired", "A password for the demo users is required.");
        }

        await context.RunAsync(async services =>
        {
            await services.GetRequiredService<IdentitySettingsCache>().ReloadAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        var effective = new DateOnly(context.Today.Year - 1, 1, 1);
        var units = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var seed in UnitSeeds)
        {
            var unit = (await context.SendAsync(new CreateUnit(seed.Code, seed.NameAr, seed.NameEn, seed.Level,
                seed.Parent is null ? null : units[seed.Parent], null, null, null, effective), cancellationToken).ConfigureAwait(false)).Require("unit " + seed.Code);
            units[seed.Key] = unit.Id;
            await context.TrackAsync(UnitType, unit.Id, "unit:" + seed.Key, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var employees = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var seed in PersonSeeds)
        {
            var employee = (await context.SendAsync(new CreateEmployee(seed.Number, seed.NameAr, seed.NameEn, Email(seed.Key), null, seed.TitleAr, seed.TitleEn,
                null, units[seed.Unit], seed.Manager is null ? null : employees[seed.Manager], effective), cancellationToken).ConfigureAwait(false))
                .Require("employee " + seed.Number);
            employees[seed.Key] = employee.Id;
            await context.TrackAsync(EmployeeType, employee.Id, "employee:" + seed.Key, cancellationToken: cancellationToken).ConfigureAwait(false);

            var user = new StrategyUser
            {
                UserName = Email(seed.Key),
                Email = Email(seed.Key),
                EmailConfirmed = true,
                EmployeeId = employee.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var password = context.Password;
            var created = await context.RunAsync(services => services.GetRequiredService<StrategyUserManager>().CreateAsync(user, password)).ConfigureAwait(false);
            if (!created.Succeeded)
            {
                throw new DemoPackException("demo.userRefused", "Demo user refused: " + string.Join(", ", created.Errors.Select(e => e.Code)));
            }

            await context.TrackAsync(UserType, user.Id, "user:" + seed.Key, cancellationToken: cancellationToken).ConfigureAwait(false);
            var roles = seed.Key == "director" ? DirectorRoles : PersonRoles.Where(r => r.Person == seed.Key).Select(r => r.Role).ToArray();
            await context.RunAsync(async services =>
            {
                foreach (var system in Systems)
                {
                    await AccessGrants.GrantSystemAccessAsync(services, system, AccessGrants.User, user.Id, cancellationToken).ConfigureAwait(false);
                }

                foreach (var role in roles)
                {
                    await AccessGrants.AssignRoleAsync(services, role, AccessGrants.User, user.Id, cancellationToken).ConfigureAwait(false);
                }

                return true;
            }).ConfigureAwait(false);
        }

        foreach (var (unitKey, head) in Heads)
        {
            var unit = (await context.SendAsync(new GetUnit(units[unitKey]), cancellationToken).ConfigureAwait(false)).Require("unit " + unitKey);
            (await context.SendAsync(new UpdateUnit(unit.Id, unit.Code, unit.NameAr, unit.NameEn, unit.LevelCode, employees[head], null, unit.CostCentre, null,
                unit.Version), cancellationToken).ConfigureAwait(false)).Require("head of " + unitKey);
        }
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        var records = await context.TrackedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var record in records.Where(r => r.ResourceType == UserType))
        {
            var id = record.ResourceId;
            if (await context.RunAsync(services => services.GetRequiredService<StrategyUserManager>().FindByIdAsync(id.ToString())).ConfigureAwait(false) is not { } found)
            {
                continue;
            }

            if (found.Active)
            {
                (await context.RunAsync(services => services.GetRequiredService<UserAdministration>().SetActiveAsync(id, false, cancellationToken)).ConfigureAwait(false))
                    .Require("deactivate " + found.UserName);
            }

            await context.RunAsync(async services =>
            {
                var users = services.GetRequiredService<StrategyUserManager>();
                var user = (await users.FindByIdAsync(id.ToString()).ConfigureAwait(false))!;
                await users.SetLockoutEnabledAsync(user, true).ConfigureAwait(false);
                return await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        foreach (var record in records.Where(r => r.ResourceType == EmployeeType))
        {
            var employee = await context.SendAsync(new GetEmployee(record.ResourceId), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (employee.IsSuccess && employee.Value.Status != EmployeeStatus.Left)
            {
                (await context.SendAsync(new ChangeEmployeeStatus(record.ResourceId, EmployeeStatus.Left, employee.Value.Version), cancellationToken)
                    .ConfigureAwait(false)).Require("employee left");
            }
        }

        // Children were created after their parents: deactivate in reverse order.
        foreach (var record in records.Where(r => r.ResourceType == UnitType).Reverse())
        {
            var unit = await context.SendAsync(new GetUnit(record.ResourceId), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (unit.IsSuccess && unit.Value.Active)
            {
                (await context.SendAsync(new DeactivateUnit(record.ResourceId, null, unit.Value.Version), cancellationToken).ConfigureAwait(false))
                    .Require("deactivate unit " + unit.Value.Code);
            }
        }
    }
}
