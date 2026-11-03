using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Calendar.Application;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Import;

namespace Strategy.Platform.Calendar.Api;

/// <summary><c>/api/v1/calendar</c>: business hours, unit assignments, holidays and their import (ADR 0027).</summary>
internal static class CalendarEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxImportBytes = 5 * 1024 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        var hours = group.MapGroup("/business-hours");
        hours.MapGet("/", async (CalendarAdministration a, CancellationToken ct) => TypedResults.Ok(await a.ListAsync(ct)))
            .WithName("ListBusinessHours").RequirePermission(CalendarPermissions.Read);
        hours.MapGet("/{id:guid}", async (Guid id, CalendarAdministration a, CancellationToken ct) => (await a.GetAsync(id, ct)).ToOk())
            .WithName("GetBusinessHours").RequirePermission(CalendarPermissions.Read);
        hours.MapPost("/", async (BusinessHoursInput body, CalendarAdministration a, CancellationToken ct) => (await a.CreateAsync(body, ct)).ToOk())
            .WithName("CreateBusinessHours").RequirePermission(CalendarPermissions.Manage);
        hours.MapPut("/{id:guid}", async (Guid id, BusinessHoursInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.UpdateAsync(id, body, ct)).ToOk())
            .WithName("UpdateBusinessHours").RequirePermission(CalendarPermissions.Manage);
        hours.MapPost("/{id:guid}/make-default", async (Guid id, VersionInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.MakeDefaultAsync(id, body, ct)).ToOk())
            .WithName("MakeBusinessHoursDefault").RequirePermission(CalendarPermissions.Manage);
        hours.MapPost("/{id:guid}/activate", async (Guid id, VersionInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.SetActiveAsync(id, true, body, ct)).ToOk())
            .WithName("ActivateBusinessHours").RequirePermission(CalendarPermissions.Manage);
        hours.MapPost("/{id:guid}/deactivate", async (Guid id, VersionInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.SetActiveAsync(id, false, body, ct)).ToOk())
            .WithName("DeactivateBusinessHours").RequirePermission(CalendarPermissions.Manage);
        hours.MapPost("/{id:guid}/calculate", async (Guid id, CalculateInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.CalculateAsync(id, body, ct)).ToOk())
            .WithName("CalculateWorkingTime").RequirePermission(CalendarPermissions.Read);

        var units = group.MapGroup("/units");
        units.MapGet("/", async (CalendarAdministration a, CancellationToken ct) => TypedResults.Ok(await a.UnitsAsync(ct)))
            .WithName("ListUnitBusinessHours").RequirePermission(CalendarPermissions.Read);
        units.MapGet("/{unitId:guid}/effective", async (Guid unitId, CalendarAdministration a, CancellationToken ct) => (await a.EffectiveAsync(unitId, ct)).ToOk())
            .WithName("GetEffectiveBusinessHours").RequirePermission(CalendarPermissions.Read);
        units.MapPut("/{unitId:guid}", async (Guid unitId, UnitAssignmentInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.SetUnitAsync(unitId, body, ct)).ToOk())
            .WithName("SetUnitBusinessHours").RequirePermission(CalendarPermissions.Manage);

        var holidays = group.MapGroup("/holidays");
        holidays.MapGet("/", async (int? year, bool? includeRemoved, CalendarAdministration a, CancellationToken ct) =>
                TypedResults.Ok(await a.HolidaysAsync(year, includeRemoved ?? false, ct)))
            .WithName("ListHolidays").RequirePermission(CalendarPermissions.Read);
        holidays.MapPost("/", async (HolidayInput body, CalendarAdministration a, CancellationToken ct) => (await a.CreateHolidayAsync(body, ct)).ToOk())
            .WithName("CreateHoliday").RequirePermission(CalendarPermissions.Manage);
        holidays.MapPut("/{id:guid}", async (Guid id, HolidayInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.UpdateHolidayAsync(id, body, ct)).ToOk())
            .WithName("UpdateHoliday").RequirePermission(CalendarPermissions.Manage);
        holidays.MapPost("/{id:guid}/remove", async (Guid id, VersionInput body, CalendarAdministration a, CancellationToken ct) =>
                (await a.RemoveHolidayAsync(id, body, ct)).ToOk())
            .WithName("RemoveHoliday").RequirePermission(CalendarPermissions.Manage);
        holidays.MapGet("/import/template", (int year) => TypedResults.File(HolidayImport.Template(year), XlsxContentType, $"holidays-{year}-template.xlsx"))
            .WithName("GetHolidayImportTemplate").RequirePermission(CalendarPermissions.Manage);
        holidays.MapPost("/import", ImportAsync).WithName("ImportHolidays").RequirePermission(CalendarPermissions.Manage).DisableAntiforgery();
        holidays.MapPost("/import/report", ReportAsync).WithName("GetHolidayImportReport").RequirePermission(CalendarPermissions.Manage).DisableAntiforgery();
    }

    private static async Task<Results<Ok<HolidayImportDto>, ProblemHttpResult>> ImportAsync(
        int year, string? mode, IFormFile file, HolidayImport import, IRequestContext context, CancellationToken ct)
    {
        var rows = ReadRows(year, file);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        if (context is RequestContext mutable)
        {
            mutable.SetChannel(Channels.Import);
        }

        return TypedResults.Ok(await import.RunAsync(year, rows.Value, mode == "commit", ct));
    }

    private static async Task<IResult> ReportAsync(int year, IFormFile file, HolidayImport import, CancellationToken ct)
    {
        var rows = ReadRows(year, file);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        var report = await import.RunAsync(year, rows.Value, commit: false, ct);
        var errors = report.Issues.ToLookup(i => i.Row, i => i.Column is null ? i.Code : $"{i.Column}: {i.Code}");
        return TypedResults.File(ImportTable.Report("holidays", HolidayImport.Columns, rows.Value, errors), XlsxContentType, $"holidays-{year}-report.xlsx");
    }

    private static Result<IReadOnlyList<ImportRow>> ReadRows(int year, IFormFile file)
    {
        if (year is < 2000 or > 2100)
        {
            return Error.Validation([new FieldError("year", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 2000, ["to"] = 2100 })]);
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".csv"))
        {
            return Error.Rule("import.fileType", "import.errors.fileType");
        }

        if (file.Length is 0 or > MaxImportBytes)
        {
            return Error.Rule("import.fileSize", "import.errors.fileSize", new Dictionary<string, object?> { ["maxMb"] = MaxImportBytes / 1024 / 1024 });
        }

        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(ImportTable.Read(stream, file.FileName, HolidayImport.Columns));
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or ArgumentException)
        {
            return Error.Rule("import.unreadable", "import.errors.unreadable");
        }
    }
}
