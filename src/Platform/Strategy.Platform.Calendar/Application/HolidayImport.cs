using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Calendar.Domain;
using Strategy.Platform.Calendar.Infrastructure;
using Strategy.Platform.Organisation.Import;

namespace Strategy.Platform.Calendar.Application;

public sealed record HolidayImportIssue(int Row, string? Column, string Code, IReadOnlyDictionary<string, object?>? Params);

public sealed record HolidayImportDto(int Year, bool Committed, int TotalRows, int Created, IReadOnlyList<HolidayImportIssue> Issues);

/// <summary>
/// Holidays of one year from Excel / CSV (ADR 0027). Every row is validated like the editor (including overlaps with existing holidays and with the
/// file's earlier rows); nothing is saved unless every row is valid.
/// </summary>
internal sealed class HolidayImport(CalendarDbContext db, CalendarAdministration administration)
{
    public static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("name_ar", "Name (Arabic)", "الاسم (عربي)"),
        new("name_en", "Name (English)", "الاسم (إنجليزي)"),
        new("start_date", "Start date (yyyy-MM-dd)", "تاريخ البداية", Required: true),
        new("end_date", "End date (empty = one day)", "تاريخ النهاية"),
        new("all_day", "All day (Y/N, default Y)", "طوال اليوم"),
        new("start_time", "From (HH:mm)", "من الساعة"),
        new("end_time", "To (HH:mm)", "إلى الساعة"),
        new("recurring", "Every year (Y/N, default N)", "سنوي"),
        new("business_hours", "Business hours (names separated by ;  — empty = all)", "ساعات العمل (أسماء مفصولة بـ ; — فارغ = الكل)"),
    ];

    public static byte[] Template(int year) => ImportTable.Template("holidays", Columns,
    [
        ["رأس السنة الميلادية", "New Year's Day", $"{year}-01-01", null, "Y", null, null, "Y", null],
        ["عيد الفطر", "Eid Al Fitr", $"{year}-03-20", $"{year}-03-22", "Y", null, null, "N", null],
        ["نصف يوم", "Half day", $"{year}-12-31", null, "N", "12:00", "15:30", "N", "Standard business hours"],
    ]);

    public async Task<HolidayImportDto> RunAsync(int year, IReadOnlyList<ImportRow> rows, bool commit, CancellationToken ct)
    {
        var issues = new List<HolidayImportIssue>();
        var records = await db.BusinessHours.AsNoTracking().Where(b => b.Active).ToListAsync(ct).ConfigureAwait(false);
        var accepted = new List<HolidayValues>();
        foreach (var row in rows)
        {
            var input = Parse(row, records, issues);
            if (input is null)
            {
                continue;
            }

            if (!input.Recurring && (input.StartDate!.Value.Year != year || (input.EndDate ?? input.StartDate).Value.Year != year))
            {
                issues.Add(new HolidayImportIssue(row.Number, "start_date", "calendar.errors.importOtherYear", new Dictionary<string, object?> { ["year"] = year }));
                continue;
            }

            var values = await administration.ValidateHolidayAsync(input, null, ct, accepted).ConfigureAwait(false);
            if (values.IsFailure)
            {
                issues.AddRange((values.Error.Fields ?? []).Select(f => new HolidayImportIssue(row.Number, ColumnOf(f.Field), f.MessageKey, f.Params)));
                continue;
            }

            accepted.Add(values.Value);
        }

        var created = 0;
        if (commit && issues.Count == 0 && accepted.Count > 0)
        {
            db.Holidays.AddRange(accepted.Select(Holiday.Create));
            await administration.SaveAsync(ct).ConfigureAwait(false);
            created = accepted.Count;
        }

        return new HolidayImportDto(year, created > 0, rows.Count, created, issues);
    }

    private static HolidayInput? Parse(ImportRow row, IReadOnlyList<BusinessHours> records, List<HolidayImportIssue> issues)
    {
        DateOnly? start = null, end = null;
        bool? allDay = null, recurring = null;
        var failed = false;
        void Bad(string column, string code)
        {
            issues.Add(new HolidayImportIssue(row.Number, column, code, null));
            failed = true;
        }

        try
        {
            start = ImportTable.ParseDate(row["start_date"]);
        }
        catch (FormatException)
        {
            Bad("start_date", "import.errors.date");
        }

        try
        {
            end = ImportTable.ParseDate(row["end_date"]);
        }
        catch (FormatException)
        {
            Bad("end_date", "import.errors.date");
        }

        try
        {
            allDay = ImportTable.ParseYesNo(row["all_day"]);
        }
        catch (FormatException)
        {
            Bad("all_day", "import.errors.yesNo");
        }

        try
        {
            recurring = ImportTable.ParseYesNo(row["recurring"]);
        }
        catch (FormatException)
        {
            Bad("recurring", "import.errors.yesNo");
        }

        if (start is null && !failed)
        {
            Bad("start_date", "validation.notEmpty");
        }

        var ids = new List<Guid>();
        var names = row["business_hours"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        foreach (var name in names)
        {
            var match = records.FirstOrDefault(r => string.Equals(r.NameEn, name, StringComparison.OrdinalIgnoreCase) || r.NameAr == name);
            if (match is null)
            {
                Bad("business_hours", "validation.unknownReference");
            }
            else
            {
                ids.Add(match.Id);
            }
        }

        return failed
            ? null
            : new HolidayInput(row["name_ar"], row["name_en"], start, end, allDay ?? true, row["start_time"], row["end_time"], recurring ?? false, ids.Count == 0, ids);
    }

    private static string ColumnOf(string field) => field switch
    {
        "nameAr" => "name_ar",
        "nameEn" => "name_en",
        "startDate" => "start_date",
        "endDate" => "end_date",
        "startTime" => "start_time",
        "endTime" => "end_time",
        "businessHoursIds" => "business_hours",
        _ => field,
    };
}
