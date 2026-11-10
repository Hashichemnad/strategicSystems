using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Integration;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Organisation.Import;
using Strategy.Platform.Settings;

namespace Strategy.Modules.AuditManagement.Application;

public sealed record AuditImportIssueDto(int Row, string? Column, string Code);

/// <remarks>
/// <c>Updated</c>: findings found by their external reference and updated (ADR 0165); <c>SkippedRecommendations</c>: recommendation rows of those
/// findings, not imported (recommendations of an existing finding keep their own lifecycle).
/// </remarks>
public sealed record AuditImportReportDto(bool Committed, int TotalRows, int Findings, int Recommendations, IReadOnlyList<AuditImportIssueDto> Issues,
    IReadOnlyList<string> CreatedNumbers, int Updated = 0, int SkippedRecommendations = 0, IReadOnlyList<string>? UpdatedNumbers = null);

/// <summary>
/// Excel / CSV import of a report's findings (ADR 0134): one row per recommendation, grouped into findings by <c>finding_ref</c>. Every row is
/// validated first; commit is all-or-nothing through the same create path as the form. A finding whose first row carries a Source system + External ID
/// that is already linked is updated (same rules as the edit form) instead of created; the reference is linked in the same transaction (ADR 0165).
/// </summary>
internal sealed class AuditImporter(
    AuditMgmtDbContext db, AuditReader reader, AuditService service, ILookupService lookups, IOrgDirectory org, IExternalReferenceStore references)
{
    public const string SheetName = "findings";

    public static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("finding_ref", "Finding ref", "مرجع الملاحظة", Required: true),
        new("title_ar", "Title (Arabic)", "العنوان بالعربية"),
        new("title_en", "Title (English)", "العنوان بالإنجليزية"),
        new("type", "Type (finding / improvement)", "النوع"),
        new("condition", "Condition", "الوضع القائم (الملاحظة)", Required: true),
        new("criteria", "Criteria", "المعيار"),
        new("cause", "Cause", "السبب"),
        new("effect", "Effect / impact", "الأثر"),
        new("finding_date", "Finding date (yyyy-mm-dd)", "تاريخ الملاحظة", Required: true),
        new("responsible_unit_code", "Responsible unit code", "رمز الإدارة المسؤولة", Required: true),
        new("priority", "Priority", "الأولوية", Required: true),
        new("repeat_of", "Repeat of (AR number)", "تكرار للملاحظة رقم"),
        new("expected_closing_date", "Expected closing date", "تاريخ الإغلاق المتوقع"),
        new("recommendation_ar", "Recommendation (Arabic)", "التوصية بالعربية"),
        new("recommendation_en", "Recommendation (English)", "التوصية بالإنجليزية"),
        new("implementation_plan", "Implementation plan", "خطة التنفيذ"),
        new("due_date", "Due date (yyyy-mm-dd)", "تاريخ التنفيذ"),
        new("contact_employee_no", "Department contact (employee no.)", "مسؤول التواصل (الرقم الوظيفي)"),
        new("management_response", "Management response", "رد الإدارة"),
        new(ExternalImport.Source.Key, ExternalImport.Source.LabelEn, ExternalImport.Source.LabelAr),
        new(ExternalImport.Id.Key, ExternalImport.Id.LabelEn, ExternalImport.Id.LabelAr),
    ];

    public static byte[] Template() => ImportTable.Template(SheetName, Columns,
    [
        [
            "F1", "ضعف في مطابقة الإيرادات", "Weak revenue reconciliation", "finding", "Monthly revenue reconciliations are not reviewed.",
            "Financial procedures manual §4.2", "No reviewer assigned", "Undetected revenue losses", "2026-09-15", "FIN", "high", null, "2026-12-31",
            "اعتماد مراجعة شهرية للمطابقات", "Introduce a monthly documented review of reconciliations", "Assign a reviewer and keep a checklist", "2026-12-15", null, "agree", null, null,
        ],
    ]);

    public async Task<Result<(AuditImportReportDto Report, IReadOnlyList<ImportRow> Rows)>> RunAsync(Guid reportId, Stream content, string fileName, bool commit,
        CancellationToken ct)
    {
        IReadOnlyList<ImportRow> rows;
        try
        {
            rows = ImportTable.Read(content, fileName, Columns);
        }
        catch (InvalidDataException)
        {
            return Error.Rule("import.fileType", "import.errors.fileType");
        }

        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct).ConfigureAwait(false);
        if (report is null)
        {
            return Error.NotFound();
        }

        if (!report.IsOpen)
        {
            return Error.Rule("auditmgmt.reportClosed", "auditmgmt.errors.reportClosed");
        }

        var issues = new List<AuditImportIssueDto>();
        void Issue(int row, string? column, string code) => issues.Add(new AuditImportIssueDto(row, column, code));
        var groups = new List<(string Ref, int FirstRow, FindingValues? Finding, List<(int Row, RecommendationValues Values)> Recs)>();
        var external = new ExternalImport(references, Finding.EntityType);
        var externalIssues = new List<ExternalImportIssue>();
        var keys = new Dictionary<string, (ExternalKey Key, Finding? Target)>(StringComparer.Ordinal);
        var unitCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var column in Columns.Where(c => c.Required && row[c.Key] is null))
            {
                Issue(row.Number, column.Key, "required");
            }

            var reference = row["finding_ref"] ?? $"row-{row.Number}";
            var group = groups.FirstOrDefault(g => g.Ref == reference);
            if (group.Ref is null)
            {
                var finding = await FindingAsync(row, unitCache, report, Issue, ct).ConfigureAwait(false);
                group = (reference, row.Number, finding, []);
                groups.Add(group);
                if (await external.ReadAsync(row.Number, row[ExternalImport.SourceColumn], row[ExternalImport.IdColumn], externalIssues, ct).ConfigureAwait(false) is { } key)
                {
                    Finding? target = null;
                    if (await external.FindAsync(key, ct).ConfigureAwait(false) is { } linked)
                    {
                        target = await db.Findings.FirstOrDefaultAsync(f => f.Id == linked, ct).ConfigureAwait(false);
                        if (target is null || target.ReportId != reportId || target.Status == AuditFindingStatuses.Cancelled)
                        {
                            Issue(row.Number, ExternalImport.IdColumn, "integration.keyMismatch");
                            target = null;
                        }
                    }

                    keys[reference] = (key, target);
                }
            }
            else if (row[ExternalImport.IdColumn] is not null && (!keys.TryGetValue(reference, out var groupKey) || groupKey.Key.ExternalId != row[ExternalImport.IdColumn]))
            {
                Issue(row.Number, ExternalImport.IdColumn, "integration.keyMismatch");
            }

            if (row["recommendation_ar"] is not null || row["recommendation_en"] is not null)
            {
                if (await RecommendationAsync(row, Issue, ct).ConfigureAwait(false) is { } recommendation)
                {
                    group.Recs.Add((row.Number, recommendation));
                }
            }
        }

        // The same validation as the form, reported against the first row of each finding / the row of each recommendation.
        foreach (var group in groups.Where(g => g.Finding is not null))
        {
            foreach (var error in await service.ValidateFindingAsync(group.Finding!, string.Empty, ct).ConfigureAwait(false))
            {
                Issue(group.FirstRow, error.Field, error.Code);
            }

            foreach (var (row, values) in group.Recs)
            {
                foreach (var error in await service.ValidateRecommendationAsync(values, string.Empty, ct).ConfigureAwait(false))
                {
                    Issue(row, error.Field, error.Code);
                }
            }
        }

        issues.AddRange(externalIssues.Select(i => new AuditImportIssueDto(i.Row, i.Column, i.Code)));
        var distinct = issues.DistinctBy(i => (i.Row, i.Column, i.Code)).OrderBy(i => i.Row).ToList();
        var created = new List<string>();
        var updatedNumbers = new List<string>();
        var updates = groups.Where(g => keys.TryGetValue(g.Ref, out var k) && k.Target is not null).ToList();
        var skipped = updates.Sum(g => g.Recs.Count);
        var committed = false;
        if (commit && distinct.Count == 0 && groups.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
            var links = new List<(int Row, ExternalKey Key, Guid Id, bool Created)>();
            foreach (var group in groups)
            {
                var keyed = keys.TryGetValue(group.Ref, out var k) ? k : default;
                if (keyed.Target is { } target)
                {
                    // The same rules as the edit form (UpdateAuditFinding): department, grant and status recomputed.
                    var department = target.ResponsibleUnitId == group.Finding!.ResponsibleUnitId
                        ? target.DepartmentUnitId
                        : await reader.DepartmentOfAsync(group.Finding.ResponsibleUnitId, settings.DepartmentLevelCode, ct).ConfigureAwait(false);
                    target.Edit(group.Finding with { RepeatOfFindingId = group.Finding.RepeatOfFindingId == target.Id ? null : group.Finding.RepeatOfFindingId }, department);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    await service.SyncDepartmentGrantAsync(target, settings, ct).ConfigureAwait(false);
                    await service.RecomputeAsync(target, ct, updateText: "Details updated / تحديث التفاصيل").ConfigureAwait(false);
                    updatedNumbers.Add(target.Number);
                    links.Add((group.FirstRow, keyed.Key, target.Id, false));
                    continue;
                }

                var finding = await service.CreateFindingAsync(report, new NewFindingInput(group.Finding!, group.Recs.Select(r => r.Values).ToList()), ct).ConfigureAwait(false);
                created.Add(finding.Number);
                if (keyed.Key is not null)
                {
                    links.Add((group.FirstRow, keyed.Key, finding.Id, true));
                }
            }

            foreach (var (row, key, id, isNew) in links)
            {
                var linked = await external.LinkAsync(key, id, isNew, db, ct).ConfigureAwait(false);
                if (linked.IsFailure)
                {
                    distinct.AddRange((linked.Error.Fields ?? []).Select(f => new AuditImportIssueDto(row, ExternalImport.IdColumn, f.Code)));
                }
            }

            if (distinct.Count > 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                return (new AuditImportReportDto(false, rows.Count, groups.Count - updates.Count, groups.Sum(g => g.Recs.Count) - skipped, distinct, [], updates.Count, skipped), rows);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            committed = true;
        }

        return (new AuditImportReportDto(committed, rows.Count, groups.Count - updates.Count, groups.Sum(g => g.Recs.Count) - skipped, distinct, created, updates.Count, skipped,
            updatedNumbers), rows);
    }

    private async Task<FindingValues?> FindingAsync(ImportRow row, Dictionary<string, Guid?> unitCache, AuditReport report, Action<int, string?, string> issue, CancellationToken ct)
    {
        var date = Date(row["finding_date"]);
        if (row["finding_date"] is not null && date is null)
        {
            issue(row.Number, "finding_date", "invalidDate");
        }

        var expected = Date(row["expected_closing_date"]);
        if (row["expected_closing_date"] is not null && expected is null)
        {
            issue(row.Number, "expected_closing_date", "invalidDate");
        }

        Guid? unit = null;
        if (row["responsible_unit_code"] is { } code)
        {
            if (!unitCache.TryGetValue(code, out unit))
            {
                unit = unitCache[code] = (await org.GetUnitByCodeAsync(code, ct).ConfigureAwait(false))?.Id;
            }

            if (unit is null)
            {
                issue(row.Number, "responsible_unit_code", "unknownUnit");
            }
        }

        Guid? repeatOf = null;
        if (row["repeat_of"] is { } repeat)
        {
            repeatOf = await db.Findings.Where(f => f.Number == repeat).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (repeatOf is null)
            {
                issue(row.Number, "repeat_of", "unknownFinding");
            }
        }

        var priority = row["priority"]?.ToLowerInvariant();
        var type = row["type"]?.ToLowerInvariant() ?? FindingTypes.Finding;
        if (priority is not null && !await lookups.IsActiveCodeAsync(AuditPermissions.PriorityLookup, priority, ct).ConfigureAwait(false))
        {
            issue(row.Number, "priority", "invalid");
        }

        if (date is null || unit is null || priority is null)
        {
            return null;
        }

        return new FindingValues(row["title_ar"], row["title_en"], type, row["condition"], row["criteria"], null, row["cause"], row["effect"], date.Value, unit.Value, priority,
            repeatOf is not null, repeatOf, expected, report.LeadAuditorEmployeeId);
    }

    private async Task<RecommendationValues?> RecommendationAsync(ImportRow row, Action<int, string?, string> issue, CancellationToken ct)
    {
        var due = Date(row["due_date"]);
        if (due is null)
        {
            // No default due date (SPEC §10.8): every imported recommendation carries its own.
            issue(row.Number, "due_date", row["due_date"] is null ? "required" : "invalidDate");
        }

        Guid? contact = null;
        if (row["contact_employee_no"] is { } number)
        {
            contact = (await reader.EmployeeByNoAsync(number, ct).ConfigureAwait(false))?.Id;
            if (contact is null)
            {
                issue(row.Number, "contact_employee_no", "unknownEmployee");
            }
        }

        return due is null
            ? null
            : new RecommendationValues(row["recommendation_ar"], row["recommendation_en"], row["management_response"]?.ToLowerInvariant(), null, row["implementation_plan"],
                contact, due.Value, null);
    }

    private static DateOnly? Date(string? value) =>
        value is null ? null
        : DateOnly.TryParseExact(value, ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? DateOnly.FromDateTime(dt) : null;
}
