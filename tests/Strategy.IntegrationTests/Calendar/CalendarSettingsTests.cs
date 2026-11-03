using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Strategy.IntegrationTests.Infrastructure;
using Strategy.Platform.Calendar;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Organisation.Application;

namespace Strategy.IntegrationTests.Calendar;

/// <summary>Settings → Business hours and Settings → Holidays (ADR 0027).</summary>
public sealed class CalendarSettingsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Hours = "/api/v1/calendar/business-hours";
    private const string Holidays = "/api/v1/calendar/holidays";

    internal static DayDto[] Week(string mode = "hours", string? start = "08:00", string? end = "16:00", bool weekend = false) =>
        [.. Enumerable.Range(1, 7).Select(d => d >= 6 && !weekend ? new DayDto(d, "closed", null, null) : new DayDto(d, mode, start, end))];

    internal static async Task<BusinessHoursDto> CreateAsync(HttpClient admin, string name, DayDto[] days, string timeZone = "Asia/Dubai")
    {
        var response = await admin.PostAsJsonAsync(Hours, new BusinessHoursInput($"{name} ع", name, timeZone, days));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BusinessHoursDto>())!;
    }

    [Fact]
    public async Task The_seeded_default_is_monday_to_friday_0730_to_1530_in_the_installation_time_zone()
    {
        var admin = await api.AdminClientAsync();
        var list = (await admin.GetFromJsonAsync<List<BusinessHoursDto>>(Hours))!;
        var standard = list.Single(b => b.IsDefault);
        standard.NameEn.ShouldBe("Standard business hours");
        standard.TimeZone.ShouldBe("Asia/Dubai");
        standard.Days.Take(5).ShouldAllBe(d => d.Mode == "hours" && d.Start == "07:30" && d.End == "15:30");
        standard.Days.Skip(5).ShouldAllBe(d => d.Mode == "closed");
    }

    [Fact]
    public async Task Refuses_invalid_business_hours_with_field_errors()
    {
        var admin = await api.AdminClientAsync();
        var days = Week();
        days[1] = new DayDto(2, "hours", "16:00", "08:00");
        days[2] = new DayDto(3, "hours", null, "12:00");
        var response = await admin.PostAsJsonAsync(Hours, new BusinessHoursInput(null, " ", "Mars/Olympus", days));
        var fields = (await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("field").GetString(), e.GetProperty("messageKey").GetString())).ToList();
        fields.ShouldContain(("nameAr", "validation.atLeastOneLanguage"));
        fields.ShouldContain(("timeZone", "calendar.errors.unknownTimeZone"));
        fields.ShouldContain(("days[1].end", "calendar.errors.endBeforeStart"));
        fields.ShouldContain(("days[2].start", "calendar.errors.timeRequired"));

        var closed = await admin.PostAsJsonAsync(Hours, new BusinessHoursInput("مغلق", "Closed", "Asia/Dubai", Week("closed", null, null, weekend: true)));
        (await closed.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors")[0].GetProperty("messageKey").GetString()
            .ShouldBe("calendar.errors.noWorkingDay");
    }

    [Fact]
    public async Task Exactly_one_default_which_cannot_be_deactivated()
    {
        var admin = await api.AdminClientAsync();
        var original = (await admin.GetFromJsonAsync<List<BusinessHoursDto>>(Hours))!.Single(b => b.IsDefault);
        await (await admin.PostAsJsonAsync($"{Hours}/{original.Id}/deactivate", new VersionInput(original.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "calendar.businessHours.default");

        var shifts = await CreateAsync(admin, "Shifts " + Guid.CreateVersion7().ToString("N")[^6..], Week("allDay", null, null, weekend: true));
        shifts.IsDefault.ShouldBeFalse();
        (await admin.PostAsJsonAsync($"{Hours}/{shifts.Id}/make-default", new VersionInput(shifts.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await admin.GetFromJsonAsync<List<BusinessHoursDto>>(Hours))!;
        list.Count(b => b.IsDefault).ShouldBe(1);
        list.Single(b => b.IsDefault).Id.ShouldBe(shifts.Id);

        // Put the seeded record back as the default; the other one can then be deactivated.
        var back = list.Single(b => b.Id == original.Id);
        (await admin.PostAsJsonAsync($"{Hours}/{back.Id}/make-default", new VersionInput(back.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = (await admin.GetFromJsonAsync<BusinessHoursDto>($"{Hours}/{shifts.Id}"))!;
        (await admin.PostAsJsonAsync($"{Hours}/{current.Id}/deactivate", new VersionInput(current.Version))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var inactive = (await admin.GetFromJsonAsync<BusinessHoursDto>($"{Hours}/{shifts.Id}"))!;
        await (await admin.PostAsJsonAsync($"{Hours}/{inactive.Id}/make-default", new VersionInput(inactive.Version)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "calendar.businessHours.inactive");
    }

    [Fact]
    public async Task Calculates_working_time_on_demand()
    {
        var admin = await api.AdminClientAsync();
        var standard = (await admin.GetFromJsonAsync<List<BusinessHoursDto>>(Hours))!.Single(b => b.IsDefault);
        // Friday 6 Nov 2026 13:00 Dubai + 4 working hours → Monday 9 Nov 09:00 Dubai (no holidays on those dates in this database).
        var response = await admin.PostAsJsonAsync($"{Hours}/{standard.Id}/calculate",
            new CalculateInput(new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.FromHours(4)), 4));
        (await response.Content.ReadFromJsonAsync<CalculateDto>())!.Due.ShouldBe(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(4)));
    }

    [Fact]
    public async Task Units_inherit_business_hours_from_their_parent_then_the_default()
    {
        var admin = await api.AdminClientAsync();
        var shifts = await CreateAsync(admin, "Operations " + Guid.CreateVersion7().ToString("N")[^6..], Week("allDay", null, null, weekend: true));
        Guid department, section, team;
        await using (var scope = TestAccess.SystemScope(api.Factory))
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var suffix = Guid.CreateVersion7().ToString("N")[^8..];
            department = (await dispatcher.SendAsync(new CreateUnit($"OPS-{suffix}", "عمليات", "Operations", "department", null, null, null, null, null))).Value.Id;
            section = (await dispatcher.SendAsync(new CreateUnit($"OPS-S-{suffix}", "قسم", "Shift section", "section", department, null, null, null, null))).Value.Id;
            team = (await dispatcher.SendAsync(new CreateUnit($"OPS-T-{suffix}", "فريق", "Night team", "unit", section, null, null, null, null))).Value.Id;
        }

        async Task<EffectiveBusinessHoursDto> EffectiveAsync(Guid unit) =>
            (await admin.GetFromJsonAsync<EffectiveBusinessHoursDto>($"/api/v1/calendar/units/{unit}/effective"))!;

        (await EffectiveAsync(team)).BusinessHours.IsDefault.ShouldBeTrue();
        (await admin.PutAsJsonAsync($"/api/v1/calendar/units/{department}", new UnitAssignmentInput(shifts.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var inherited = await EffectiveAsync(team);
        inherited.BusinessHours.Id.ShouldBe(shifts.Id);
        inherited.Inherited.ShouldBeTrue();
        inherited.FromUnitId.ShouldBe(department);

        // The section keeps the standard hours for itself; the team below it follows the section.
        var standard = (await admin.GetFromJsonAsync<List<BusinessHoursDto>>(Hours))!.Single(b => b.IsDefault);
        (await admin.PutAsJsonAsync($"/api/v1/calendar/units/{section}", new UnitAssignmentInput(standard.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EffectiveAsync(team)).BusinessHours.Id.ShouldBe(standard.Id);
        (await EffectiveAsync(department)).Inherited.ShouldBeFalse();

        // Back to "inherit": the team follows the department again.
        (await admin.PutAsJsonAsync($"/api/v1/calendar/units/{section}", new UnitAssignmentInput(null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EffectiveAsync(team)).BusinessHours.Id.ShouldBe(shifts.Id);
        (await admin.GetFromJsonAsync<List<UnitAssignmentDto>>("/api/v1/calendar/units"))!.ShouldContain(u => u.UnitId == department && u.BusinessHoursId == shifts.Id);

        await using var check = TestAccess.SystemScope(api.Factory);
        var calculator = check.ServiceProvider.GetRequiredService<IBusinessHoursCalculator>();
        (await calculator.ResolveForUnitAsync(team)).Id.ShouldBe(shifts.Id);
    }

    [Fact]
    public async Task Holidays_validate_dates_hours_records_and_overlaps()
    {
        var admin = await api.AdminClientAsync();
        var other = await CreateAsync(admin, "Branch " + Guid.CreateVersion7().ToString("N")[^6..], Week());
        var standard = (await admin.GetFromJsonAsync<List<BusinessHoursDto>>(Hours))!.Single(b => b.IsDefault);
        var year = 2031;

        var bad = await admin.PostAsJsonAsync(Holidays, new HolidayInput("عطلة", "Holiday", new DateOnly(year, 5, 3), new DateOnly(year, 5, 1), false,
            "12:00", "09:00", false, false, []));
        var errors = (await bad.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("field").GetString(), e.GetProperty("messageKey").GetString())).ToList();
        errors.ShouldContain(("endDate", "calendar.errors.endBeforeStart"));
        errors.ShouldContain(("endTime", "calendar.errors.endBeforeStart"));
        errors.ShouldContain(("businessHoursIds", "calendar.errors.holidayRecords"));

        // A holiday for one record only, then another on the same date for the other record: no conflict.
        var branchDay = await admin.PostAsJsonAsync(Holidays, new HolidayInput("يوم الفرع", "Branch day", new DateOnly(year, 5, 1), null, true, null, null,
            false, false, [other.Id]));
        branchDay.StatusCode.ShouldBe(HttpStatusCode.OK, await branchDay.Content.ReadAsStringAsync());
        (await admin.PostAsJsonAsync(Holidays, new HolidayInput("يوم المقر", "Head office day", new DateOnly(year, 5, 1), null, true, null, null,
            false, false, [standard.Id]))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A holiday for all records on that date overlaps both, and the message names the other holiday and the date.
        var clash = await admin.PostAsJsonAsync(Holidays, new HolidayInput("عطلة عامة", "Public holiday", new DateOnly(year, 4, 30), new DateOnly(year, 5, 2),
            true, null, null, false, true, null));
        var overlap = (await clash.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors")[0];
        overlap.GetProperty("messageKey").GetString().ShouldBe("calendar.errors.holidayOverlap");
        overlap.GetProperty("params").GetProperty("date").GetString().ShouldBe($"{year}-05-01");

        // The calculator honours the one-record holiday: the branch loses 1 May, the head office loses it too (own holiday), the 24/7 does not.
        await using var scope = TestAccess.SystemScope(api.Factory);
        var calculator = scope.ServiceProvider.GetRequiredService<IBusinessHoursCalculator>();
        var thursday = new DateTimeOffset(year, 5, 1, 9, 0, 0, TimeSpan.FromHours(4));
        (await calculator.IsWorkingTimeAsync(other.Id, thursday)).ShouldBeFalse();

        // Only this test's one-time holidays are checked: yearly holidays created by other tests of the class also show in this year.
        string[] mine = ["Branch day", "Head office day"];
        var listed = (await admin.GetFromJsonAsync<List<HolidayDto>>($"{Holidays}?year={year}"))!.Where(h => mine.Contains(h.NameEn)).ToList();
        listed.Select(h => h.NameEn).ShouldBe(mine, ignoreOrder: true);
        listed.ShouldNotContain(h => h.NameEn == "Public holiday"); // the refused one was not saved
        var branch = listed.Single(h => h.NameEn == "Branch day");
        var removed = await admin.PostAsJsonAsync($"{Holidays}/{branch.Id}/remove", new VersionInput(branch.Version));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetFromJsonAsync<List<HolidayDto>>($"{Holidays}?year={year}"))!.Select(h => h.NameEn)
            .ShouldNotContain("Branch day");
    }

    [Fact]
    public async Task Recurring_holidays_show_in_every_year()
    {
        var admin = await api.AdminClientAsync();
        var created = await admin.PostAsJsonAsync(Holidays, new HolidayInput("اليوم الوطني", "National Day", new DateOnly(2030, 12, 2), new DateOnly(2030, 12, 3),
            true, null, null, true, true, null));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var in2035 = (await admin.GetFromJsonAsync<List<HolidayDto>>($"{Holidays}?year=2035"))!.Single(h => h.NameEn == "National Day");
        in2035.OccursOn.ShouldBe(new DateOnly(2035, 12, 2));
        var across = await admin.PostAsJsonAsync(Holidays, new HolidayInput("رأس السنة", "Year end", new DateOnly(2030, 12, 31), new DateOnly(2031, 1, 1),
            true, null, null, true, true, null));
        (await across.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation.failed")).GetProperty("errors")[0].GetProperty("messageKey").GetString()
            .ShouldBe("calendar.errors.recurringAcrossYear");
    }

    [Fact]
    public async Task Imports_a_year_of_holidays_from_excel_only_when_every_row_is_valid()
    {
        var admin = await api.AdminClientAsync();
        var template = await admin.GetAsync("/api/v1/calendar/holidays/import/template?year=2032");
        template.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var workbook = new XLWorkbook(await template.Content.ReadAsStreamAsync()))
        {
            workbook.Worksheet(1).Cell(3, 3).GetString().ShouldBe("2032-01-01");
        }

        var broken = Workbook(
            ["عيد", "Eid", "2032-03-10", "2032-03-12", "Y", "", "", "N", ""],
            ["خطأ", "Wrong year", "2031-05-05", "", "Y", "", "", "N", ""],
            ["نصف يوم", "Half day", "2032-12-30", "", "N", "15:00", "12:00", "N", "Standard business hours"],
            ["مكرر", "Overlaps Eid", "2032-03-12", "", "Y", "", "", "N", ""]);
        var report = await ImportAsync(admin, broken, "commit");
        report.Committed.ShouldBeFalse();
        report.Issues.Select(i => (i.Row, i.Code)).ShouldBe(
            [(4, "calendar.errors.importOtherYear"), (5, "calendar.errors.endBeforeStart"), (6, "calendar.errors.holidayOverlap")], ignoreOrder: true);
        // Nothing from the refused file was saved (yearly holidays of other tests may also show in 2032).
        string[] fromFile = ["Eid", "Wrong year", "Half day", "Overlaps Eid"];
        (await admin.GetFromJsonAsync<List<HolidayDto>>($"{Holidays}?year=2032"))!.Where(h => fromFile.Contains(h.NameEn)).ShouldBeEmpty();

        var valid = Workbook(
            ["عيد", "Eid", "2032-03-10", "2032-03-12", "Y", "", "", "N", ""],
            ["نصف يوم", "Half day", "2032-12-30", "", "N", "12:00", "15:30", "N", "Standard business hours"]);
        var committed = await ImportAsync(admin, valid, "commit");
        committed.Issues.ShouldBeEmpty();
        committed.Created.ShouldBe(2);
        var holidays = (await admin.GetFromJsonAsync<List<HolidayDto>>($"{Holidays}?year=2032"))!;
        holidays.Single(h => h.NameEn == "Half day").AllDay.ShouldBeFalse();
        holidays.Single(h => h.NameEn == "Half day").AppliesToAll.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_calendar_administrators_change_business_hours()
    {
        var viewer = await api.Factory.CreateSignedInClientAsync(roles: "calendar.viewer");
        (await viewer.GetAsync(Hours)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await viewer.PostAsJsonAsync(Hours, new BusinessHoursInput("س", "X", "Asia/Dubai", Week())))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "access.forbidden");
        var user = await api.UserClientAsync();
        (await user.GetAsync(Holidays)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static byte[] Workbook(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("holidays");
        string[] keys = ["name_ar", "name_en", "start_date", "end_date", "all_day", "start_time", "end_time", "recurring", "business_hours"];
        for (var c = 0; c < keys.Length; c++)
        {
            sheet.Cell(1, c + 1).SetValue(HolidayImport.Columns[c].LabelEn); // as in the template: labels, then the hidden key row
            sheet.Cell(2, c + 1).SetValue(keys[c]);
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 3, c + 1).SetValue(rows[r][c]);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<HolidayImportDto> ImportAsync(HttpClient client, byte[] file, string mode)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", "holidays.xlsx");
        var response = await client.PostAsync($"/api/v1/calendar/holidays/import?year=2032&mode={mode}", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonSerializer.Deserialize<HolidayImportDto>(await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
    }
}
