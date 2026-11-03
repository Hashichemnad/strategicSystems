using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.IntegrationTests.Workflow;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Workflow;
using Strategy.Platform.Workflow.Application;

namespace Strategy.IntegrationTests.Calendar;

/// <summary>
/// Approval SLAs counted in business hours (ADR 0027), with a frozen clock. Default record: Monday–Friday 07:30–15:30 Asia/Dubai (UTC+4).
/// Each test uses its own years so holidays of one test never touch another.
/// </summary>
public sealed class WorkingHoursSlaTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeSpan Dubai = TimeSpan.FromHours(4);
    private readonly TravellingClock _clock = new();
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await ApiFactory.CreateAsync(postgres, configureServices: services => services.AddSingleton<IClock>(_clock));

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>A Friday at 13:00 in Dubai, <paramref name="year"/>'s first Friday of March.</summary>
    private static DateTimeOffset FridayAfternoon(int year)
    {
        var date = new DateOnly(year, 3, 1);
        while (date.DayOfWeek != DayOfWeek.Friday)
        {
            date = date.AddDays(1);
        }

        return new DateTimeOffset(date.ToDateTime(new TimeOnly(13, 0)), Dubai);
    }

    private async Task<ApprovalWorld> ArrangeAsync(DateTimeOffset now, params ApprovalStepDefinition[] chain)
    {
        _clock.Frozen = now; // before anyone signs in: sessions follow the clock
        var world = await ApprovalWorld.CreateAsync(_factory, withSection: false);
        await SaveChainAsync(chain);
        return world;
    }

    /// <summary>
    /// Configuration through the services in a system scope: an administrator's authenticator code follows the clock, which is frozen here.
    /// </summary>
    private async Task SaveChainAsync(params ApprovalStepDefinition[] chain)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var processes = scope.ServiceProvider.GetRequiredService<ProcessAdministration>();
        var current = (await processes.GetAsync(Modules.Sample.Application.SampleApprovalProcess.Key, null, CancellationToken.None)).Value;
        var saved = await processes.SaveAsync(current.Key, new SaveProcessInput(chain, "test", current.VersionNo), CancellationToken.None);
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? string.Join(", ", saved.Error.Fields?.Select(f => f.Field + ":" + f.MessageKey) ?? [saved.Error.Code]) : null);
    }

    private async Task<T> CalendarAsync<T>(Func<CalendarAdministration, Task<Strategy.Platform.Kernel.Results.Result<T>>> action)
    {
        await using var scope = TestAccess.SystemScope(_factory);
        var result = await action(scope.ServiceProvider.GetRequiredService<CalendarAdministration>());
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? string.Join(", ", result.Error.Fields?.Select(f => f.MessageKey) ?? [result.Error.Code]) : null);
        return result.Value;
    }

    private Task<BusinessHoursDto> CreateHoursAsync(string name, DayDto[] days, string zone = "Asia/Dubai") =>
        CalendarAsync(a => a.CreateAsync(new BusinessHoursInput($"{name} ع", name, zone, days), CancellationToken.None));

    private Task<HolidayDto> CreateHolidayAsync(DateOnly date, params Guid[] records) =>
        CalendarAsync(a => a.CreateHolidayAsync(
            new HolidayInput("عطلة", "Holiday " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), date, null, true, null, null,
                false, records.Length == 0, records), CancellationToken.None));

    private async Task<BusinessHoursDto> DefaultAsync()
    {
        await using var scope = TestAccess.SystemScope(_factory);
        return (await scope.ServiceProvider.GetRequiredService<CalendarAdministration>().ListAsync(CancellationToken.None)).Single(b => b.IsDefault);
    }

    private static ApprovalStepDefinition Director(int hours, string slaMode = SlaModes.Working, string source = BusinessHoursSources.Default,
        Guid? businessHoursId = null) =>
        new("director", "مدير الإدارة", "Director", new ApproverRule(ApproverRuleKinds.DepartmentDirector), SlaHours: hours, SlaMode: slaMode,
            BusinessHoursSource: source, BusinessHoursId: businessHoursId);

    private static async Task<StepStateDto> SubmitAsync(ApprovalWorld world)
    {
        var item = await world.CreateItemAsync();
        await world.SubmitOkAsync(item);
        return (await ApprovalWorld.PanelAsync(world.SubmitterClient, item.Id)).Instances[0].Steps.Single(s => s.State == StepStates.Current);
    }

    private async Task<int> RunSlaJobAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>()
            .Initialise(Guid.CreateVersion7().ToString("N"), Channels.Job, StrategyClaims.CreateSystemPrincipal());
        return await scope.ServiceProvider.GetRequiredService<SlaEscalation>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Working_hours_skip_the_weekend_and_the_escalation_waits_for_them()
    {
        var friday = FridayAfternoon(2027);
        var world = await ArrangeAsync(friday, Director(4));
        var step = await SubmitAsync(world);

        // Friday 13:00–15:30 (2.5 h), then Monday 07:30–09:00 (1.5 h).
        var monday = friday.AddDays(3);
        step.DueAt.ShouldBe(new DateTimeOffset(monday.Date.AddHours(9), Dubai));
        step.SlaMode.ShouldBe(SlaModes.Working);
        step.BusinessHoursNameEn.ShouldBe("Standard business hours");
        step.BusinessHoursNameAr.ShouldBe("ساعات العمل الرسمية");

        var work = (await world.DirectorClient.GetFromJsonAsync<MyWorkDto>("/api/v1/workflow/my-work"))!;
        work.Approvals.Single().BusinessHoursNameEn.ShouldBe("Standard business hours");

        _clock.Frozen = new DateTimeOffset(friday.Date.AddDays(2).AddHours(23), Dubai); // Sunday night: 58 calendar hours later, not due
        (await RunSlaJobAsync()).ShouldBe(0);
        _clock.Frozen = new DateTimeOffset(monday.Date.AddHours(9).AddMinutes(1), Dubai);
        (await RunSlaJobAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_holiday_moves_the_due_date_and_one_for_another_record_does_not()
    {
        var friday = FridayAfternoon(2028);
        var world = await ArrangeAsync(friday, Director(4));
        var monday = DateOnly.FromDateTime(friday.Date).AddDays(3);
        var other = await CreateHoursAsync("Other " + Guid.CreateVersion7().ToString("N")[^6..], CalendarSettingsTests.Week());

        // A Monday holiday for the other record only changes nothing for the default record.
        await CreateHolidayAsync(monday, other.Id);
        (await SubmitAsync(world)).DueAt.ShouldBe(new DateTimeOffset(monday.ToDateTime(new TimeOnly(9, 0)), Dubai));

        // The same Monday as a holiday of the default record → due on Tuesday 09:00.
        await CreateHolidayAsync(monday, (await DefaultAsync()).Id);
        (await SubmitAsync(world)).DueAt.ShouldBe(new DateTimeOffset(monday.AddDays(1).ToDateTime(new TimeOnly(9, 0)), Dubai));
    }

    [Fact]
    public async Task A_unit_with_its_own_business_hours_applies_to_its_child_units()
    {
        var friday = FridayAfternoon(2029);
        var world = await ArrangeAsync(friday, Director(4, source: BusinessHoursSources.Unit));
        var always = await CreateHoursAsync("Operations 24/7 " + Guid.CreateVersion7().ToString("N")[^6..],
            CalendarSettingsTests.Week("allDay", null, null, weekend: true));

        // The department gets 24-hour business hours; the submitter sits in a team two levels below it.
        await CalendarAsync(a => a.SetUnitAsync(world.DepartmentId, new UnitAssignmentInput(always.Id), CancellationToken.None));
        Guid team;
        await using (var scope = TestAccess.SystemScope(_factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            team = (await dispatcher.SendAsync(new CreateUnit($"TEAM-{Guid.CreateVersion7().ToString("N")[^8..]}", "فريق", "Night team", "unit",
                world.SectionId, null, null, null, null))).Value.Id;
        }

        var nightOperator = await _factory.CreateUserAsync(unitId: team, managerEmployeeId: world.Director.EmployeeId, roles: TestIdentity.SampleEditor);
        var client = await _factory.SignInAsync(nightOperator);
        var item = await world.CreateItemAsync(client: client);
        (await world.SubmitAsync(item, client: client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var step = (await ApprovalWorld.PanelAsync(client, item.Id)).Instances[0].Steps.Single(s => s.State == StepStates.Current);
        step.DueAt.ShouldBe(friday.AddHours(4)); // 24 hours a day, weekend included
        step.BusinessHoursNameEn.ShouldBe(always.NameEn);

        // A step on the default record ignores the unit's record.
        await SaveChainAsync(Director(4));
        var other = await world.CreateItemAsync(client: client);
        (await world.SubmitAsync(other, client: client)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApprovalWorld.PanelAsync(client, other.Id)).Instances[0].Steps.Single(s => s.State == StepStates.Current).BusinessHoursNameEn
            .ShouldBe("Standard business hours");
    }

    [Fact]
    public async Task Calendar_hours_and_a_specific_record_in_another_time_zone_are_honoured()
    {
        var friday = FridayAfternoon(2030);
        var world = await ArrangeAsync(friday, Director(4, slaMode: SlaModes.Calendar));
        var calendarStep = await SubmitAsync(world);
        calendarStep.DueAt.ShouldBe(friday.AddHours(4));
        calendarStep.SlaMode.ShouldBe(SlaModes.Calendar);
        calendarStep.BusinessHoursNameEn.ShouldBeNull();

        // A specific record in UTC (Monday–Friday 08:00–16:00): Friday 13:00 Dubai = 09:00 UTC, + 4 h → 13:00 UTC.
        var utc = await CreateHoursAsync("UTC office " + Guid.CreateVersion7().ToString("N")[^6..], CalendarSettingsTests.Week(), "Etc/UTC");
        await SaveChainAsync(Director(4, source: BusinessHoursSources.Specific, businessHoursId: utc.Id));
        var specific = await SubmitAsync(world);
        specific.DueAt.ShouldBe(new DateTimeOffset(friday.UtcDateTime.Date.AddHours(13), TimeSpan.Zero));
        specific.BusinessHoursNameEn.ShouldBe(utc.NameEn);
    }
}
