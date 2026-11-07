using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Calendar.Domain;
using Strategy.Platform.Calendar.Infrastructure;
using Strategy.Platform.Integration;
using Strategy.Platform.Organisation.Import;

namespace Strategy.Platform.Calendar.Application;

public sealed record HolidayImportIssue(int Row, string? Column, string Code, IReadOnlyDictionary<string, object?>? Params);

public sealed record HolidayImportDto(int Year, bool Committed, int TotalRows, int Created, IReadOnlyList<HolidayImportIssue> Issues, int Updated = 0);

/// <summary>
/// Holidays of one year from Excel / CSV (ADR 0027). Every row is validated like the editor (including overlaps with existing holidays and with the
/// file's earlier rows); nothing is saved unless every row is valid. Rows with a Source system + External ID update the holiday that reference points
/// at (the overlap check then excludes it), others create one; references are linked in the same transaction (ADR 0165).
/// </summary>
internal sealed class HolidayImport(CalendarDbContext db, CalendarAdministration administration, IExternalReferenceStore references)
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
        new(ExternalImport.Source.Key, ExternalImport.Source.LabelEn, ExternalImport.Source.LabelAr),
        new(ExternalImport.Id.Key, ExternalImport.Id.LabelEn, ExternalImport.Id.LabelAr),
    ];

    public static byte[] Template(int year) => ImportTable.Template("holidays", Columns,
    [
        ["رأس السنة الميلادية", "New Year's Day", $"{year}-01-01", null, "Y", null, null, "Y", null, null, null],
        ["عيد الفطر", "Eid Al Fitr", $"{year}-03-20", $"{year}-03-22", "Y", null, null, "N", null, null, null],
        ["نصف يوم", "Half day", $"{year}-12-31", null, "N", "12:00", "15:30", "N", "Standard business hours", null, null],
    ]);

    public async Task<HolidayImportDto> RunAsync(int year, IReadOnlyList<ImportRow> rows, bool commit, CancellationToken ct)
    {
        var issues = new List<HolidayImportIssue>();
        var records = await db.BusinessHours.AsNoTracking().Where(b => b.Active).ToListAsync(ct).ConfigureAwait(false);
        var accepted = new List<HolidayValues>();
        var targets = new List<(int Row, Guid? UpdateId, ExternalKey? Key)>();
        var external = new ExternalImport(references, Holiday.EntityType);
        var externalIssues = new List<ExternalImportIssue>();
        foreach (var row in rows)
        {
            var input = Parse(row, records, issues);
            var key = await external.ReadAsync(row.Number, row[ExternalImport.SourceColumn], row[ExternalImport.IdColumn], externalIssues, ct).ConfigureAwait(false);
            if (input is null)
            {
                continue;
            }

            Guid? updateId = null;
            if (key is not null && await external.FindAsync(key, ct).ConfigureAwait(false) is { } linked)
            {
                if (!await db.Holidays.AnyAsync(h => h.Id == linked && h.Active, ct).ConfigureAwait(false))
                {
                    issues.Add(new HolidayImportIssue(row.Number, ExternalImport.IdColumn, ExternalReferenceErrors.ExternalIdTaken, null));
                    continue;
                }

                updateId = linked;
            }

            if (!input.Recurring && (input.StartDate!.Value.Year != year || (input.EndDate ?? input.StartDate).Value.Year != year))
            {
                issues.Add(new HolidayImportIssue(row.Number, "start_date", "calendar.errors.importOtherYear", new Dictionary<string, object?> { ["year"] = year }));
                continue;
            }

            var values = await administration.ValidateHolidayAsync(input, updateId, ct, accepted).ConfigureAwait(false);
            if (values.IsFailure)
            {
                issues.AddRange((values.Error.Fields ?? []).Select(f => new HolidayImportIssue(row.Number, ColumnOf(f.Field), f.MessageKey, f.Params)));
                continue;
            }

            accepted.Add(values.Value);
            targets.Add((row.Number, updateId, key));
        }

        issues.AddRange(externalIssues.Select(i => new HolidayImportIssue(i.Row, i.Column, i.MessageKey, null)));
        issues.Sort((a, b) => a.Row.CompareTo(b.Row));
        var updated = targets.Count(t => t.UpdateId is not null);
        var created = accepted.Count - updated;
        if (!commit || issues.Count > 0 || accepted.Count == 0)
        {
            return new HolidayImportDto(year, false, rows.Count, created, issues, updated);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var saved = new List<(int Row, Guid Id, bool Created, ExternalKey? Key)>();
        foreach (var (values, target) in accepted.Zip(targets))
        {
            if (target.UpdateId is { } id)
            {
                var holiday = await db.Holidays.FirstAsync(h => h.Id == id, ct).ConfigureAwait(false);
                holiday.Update(values);
                saved.Add((target.Row, id, false, target.Key));
            }
            else
            {
                var holiday = Holiday.Create(values);
                db.Holidays.Add(holiday);
                saved.Add((target.Row, holiday.Id, true, target.Key));
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var (row, id, isNew, key) in saved.Where(s => s.Key is not null))
        {
            var linked = await external.LinkAsync(key!, id, isNew, db, ct).ConfigureAwait(false);
            if (linked.IsFailure)
            {
                issues.AddRange((linked.Error.Fields ?? []).Select(f => new HolidayImportIssue(row, ExternalImport.IdColumn, f.MessageKey, f.Params)));
            }
        }

        if (issues.Count > 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return new HolidayImportDto(year, false, rows.Count, created, issues, updated);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        CalendarChangeSignal.Raise();
        return new HolidayImportDto(year, true, rows.Count, created, issues, updated);
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

/// <summary>Holidays carry external references (imports, Phase 23 sync) although they are not a linkable type (ADR 0164).</summary>
internal sealed class HolidayReferences(CalendarDbContext db) : IReferenceableRecordSource
{
    public static readonly ReferenceableTypeDefinition Definition = new(
        Holiday.EntityType, CalendarPermissions.SystemCode, "عطلة", "Holiday", CalendarPermissions.Read, "/settings/holidays");

    public string ResourceType => Holiday.EntityType;

    /// <remarks>Holidays have no data scope: the read permission (checked by the resolver) shows them all.</remarks>
    public async Task<IReadOnlyDictionary<Guid, ReferencedRecord>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await db.Holidays.AsNoTracking().Where(h => ids.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => new ReferencedRecord(h.Id, h.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), h.NameAr, h.NameEn),
                cancellationToken).ConfigureAwait(false);
}
