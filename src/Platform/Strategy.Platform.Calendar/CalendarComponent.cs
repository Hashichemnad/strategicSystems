using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar.Api;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Calendar.Domain;
using Strategy.Platform.Calendar.Infrastructure;
using Strategy.Platform.Http.Modules;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Calendar;

public static class CalendarPermissions
{
    public const string SystemCode = "calendar";
    public const string Read = "calendar.business-hours.read";
    public const string Manage = "calendar.business-hours.manage";
}

/// <summary>Business hours and holidays (<c>/api/v1/calendar</c>, schema <c>calendar</c>, ADR 0027).</summary>
public sealed class CalendarComponent : IPlatformComponent
{
    public string Name => "calendar";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CalendarDbContext>(CalendarDbContext.SchemaName);
        services.AddMemoryCache();
        services.AddScoped<BusinessHoursCalculator>();
        services.AddScoped<IBusinessHoursCalculator>(sp => sp.GetRequiredService<BusinessHoursCalculator>());
        services.AddScoped<CalendarAdministration>();
        services.AddScoped<HolidayImport>();
        services.AddScoped<IDatabaseSeeder, CalendarSeeder>();
        services.AddAccessCatalogue(new CalendarCatalogue());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CalendarEndpoints.Map(endpoints.MapPlatformGroup(this));
}

internal sealed class CalendarCatalogue : IAccessCatalogueSource
{
    public SystemDefinition System => new(CalendarPermissions.SystemCode, SystemKind.Platform, "ساعات العمل والعطلات", "Business hours and holidays", 46);

    public IEnumerable<PermissionDefinition> Permissions =>
    [
        new(CalendarPermissions.Read, "عرض ساعات العمل والعطلات", "View business hours and holidays", Scopable: false),
        new(CalendarPermissions.Manage, "إدارة ساعات العمل والعطلات", "Manage business hours and holidays", Scopable: false),
    ];

    public IEnumerable<RoleSeed> DefaultRoles =>
    [
        new("administrator", "مسؤول ساعات العمل والعطلات", "Business hours administrator",
            [new(CalendarPermissions.Read), new(CalendarPermissions.Manage)], RequiresMfa: true),
        new("viewer", "مستعرض ساعات العمل والعطلات", "Business hours viewer", [new(CalendarPermissions.Read)]),
    ];
}

/// <summary>
/// Seeds the default record once (ADR 0027): Monday–Friday 07:30–15:30, Saturday and Sunday closed, in the installation's time zone. Editable.
/// </summary>
internal sealed class CalendarSeeder : IDatabaseSeeder
{
    public int Order => 25;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<CalendarDbContext>();
        if (await db.BusinessHours.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var zone = (await services.GetRequiredService<ISettingsStore>().GetAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false)).TimeZone;
        StoredDay[] days =
        [
            .. Enumerable.Range(1, 5).Select(d => new StoredDay(d, DayModes.Hours, "07:30", "15:30")),
            new(6, DayModes.Closed, null, null),
            new(7, DayModes.Closed, null, null),
        ];
        db.BusinessHours.Add(BusinessHours.Create("ساعات العمل الرسمية", "Standard business hours", zone, days, isDefault: true));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        CalendarChangeSignal.Raise();
    }
}
