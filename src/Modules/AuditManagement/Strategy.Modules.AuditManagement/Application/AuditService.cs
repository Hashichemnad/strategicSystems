using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Contracts;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.AuditManagement.Application;

/// <summary>A recommendation entered together with its finding (form, import, AI confirmation).</summary>
public sealed record NewFindingInput(FindingValues Finding, IReadOnlyList<RecommendationValues>? Recommendations);

/// <summary>
/// Operations shared by handlers, the import, the approval hook and the monitor (docs/design/audit.md §4). Every method expects an open
/// transaction on <see cref="AuditMgmtDbContext"/> (ADR 0023) and saves its changes.
/// </summary>
internal sealed class AuditService(
    AuditMgmtDbContext db, AuditReader reader, AuditDeadlines deadlines, AuditNotifier notifier, INumberingService numbering, IRecordRoleService recordRoles,
    ILookupService lookups, IIntegrationEventPublisher events, IServiceProvider services, IClock clock)
{
    // ---- Validation (field paths for the dialogs) ----

    public async Task<List<FieldError>> ValidateReportAsync(AuditReportValues v, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            Add("titleEn", "validation.atLeastOneLanguage");
        }

        if (v.TitleAr is { Length: > AuditReport.TitleMaxLength } || v.TitleEn is { Length: > AuditReport.TitleMaxLength })
        {
            Add("titleEn", "validation.maximumLength");
        }

        if (string.IsNullOrWhiteSpace(v.TypeCode) || !await lookups.IsActiveCodeAsync(AuditPermissions.ReportTypeLookup, v.TypeCode, ct).ConfigureAwait(false))
        {
            Add("typeCode", "validation.invalid");
        }

        if (v.PeriodFrom is { } from && v.PeriodTo is { } to && to < from)
        {
            Add("periodTo", "auditmgmt.errors.endBeforeStart");
        }

        if (v.LeadAuditorEmployeeId is { } lead && await reader.EmployeeAsync(lead, ct).ConfigureAwait(false) is null)
        {
            Add("leadAuditorEmployeeId", "validation.invalid");
        }

        foreach (var unit in v.AuditedUnitIds ?? [])
        {
            if (await reader.UnitAsync(unit, ct).ConfigureAwait(false) is null)
            {
                Add("auditedUnitIds", "validation.invalid");
                break;
            }
        }

        if ((v.AuditorBody?.Length ?? 0) > AuditReport.TitleMaxLength || (v.Summary?.Length ?? 0) > AuditReport.TextMaxLength)
        {
            Add("summary", "validation.maximumLength");
        }

        return errors;
    }

    public async Task<List<FieldError>> ValidateFindingAsync(FindingValues v, string prefix, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(prefix + field, key, key));
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            Add("titleEn", "validation.atLeastOneLanguage");
        }

        if (v.TitleAr is { Length: > AuditReport.TitleMaxLength } || v.TitleEn is { Length: > AuditReport.TitleMaxLength })
        {
            Add("titleEn", "validation.maximumLength");
        }

        if (string.IsNullOrWhiteSpace(v.Condition))
        {
            Add("condition", "validation.required");
        }

        foreach (var (field, value) in new[] { ("condition", v.Condition), ("criteria", v.Criteria), ("cause", v.Cause), ("effect", v.Effect) })
        {
            if (value is { Length: > AuditReport.TextMaxLength })
            {
                Add(field, "validation.maximumLength");
            }
        }

        if (string.IsNullOrWhiteSpace(v.TypeCode) || !await lookups.IsActiveCodeAsync(AuditPermissions.FindingTypeLookup, v.TypeCode, ct).ConfigureAwait(false))
        {
            Add("typeCode", "validation.invalid");
        }

        if (string.IsNullOrWhiteSpace(v.Priority) || !await lookups.IsActiveCodeAsync(AuditPermissions.PriorityLookup, v.Priority, ct).ConfigureAwait(false))
        {
            Add("priority", "validation.invalid");
        }

        if (v.ResponsibleUnitId == Guid.Empty || await reader.UnitAsync(v.ResponsibleUnitId, ct).ConfigureAwait(false) is null)
        {
            Add("responsibleUnitId", v.ResponsibleUnitId == Guid.Empty ? "validation.required" : "validation.invalid");
        }

        if (v.FindingDate == default)
        {
            Add("findingDate", "validation.required");
        }

        if (v.ExpectedClosingOn is { } e && e < v.FindingDate)
        {
            Add("expectedClosingOn", "auditmgmt.errors.endBeforeStart");
        }

        if (v.RepeatOfFindingId is { } repeat && !await db.Findings.AnyAsync(f => f.Id == repeat, ct).ConfigureAwait(false))
        {
            Add("repeatOfFindingId", "validation.invalid");
        }

        if (v.AuditorEmployeeId is { } auditor && await reader.EmployeeAsync(auditor, ct).ConfigureAwait(false) is null)
        {
            Add("auditorEmployeeId", "validation.invalid");
        }

        return errors;
    }

    public async Task<List<FieldError>> ValidateRecommendationAsync(RecommendationValues v, string prefix, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(prefix + field, key, key));
        if (string.IsNullOrWhiteSpace(v.ActionAr) && string.IsNullOrWhiteSpace(v.ActionEn))
        {
            Add("actionEn", "validation.atLeastOneLanguage");
        }

        foreach (var (field, value) in new[]
                 {
                     ("actionAr", v.ActionAr), ("actionEn", v.ActionEn), ("implementationPlan", v.ImplementationPlan), ("managementComment", v.ManagementComment),
                     ("notes", v.Notes),
                 })
        {
            if (value is { Length: > AuditReport.TextMaxLength })
            {
                Add(field, "validation.maximumLength");
            }
        }

        // SPEC §10.8: no default due date — each recommendation carries its own.
        if (v.DueOn == default)
        {
            Add("dueOn", "auditmgmt.errors.dueDateRequired");
        }

        if (v.ManagementResponseCode is { } response && !await lookups.IsActiveCodeAsync(AuditPermissions.ResponseLookup, response, ct).ConfigureAwait(false))
        {
            Add("managementResponseCode", "validation.invalid");
        }

        if (v.ContactEmployeeId is { } contact && await reader.EmployeeAsync(contact, ct).ConfigureAwait(false) is null)
        {
            Add("contactEmployeeId", "validation.invalid");
        }

        return errors;
    }

    // ---- Creation ----

    public async Task<AuditReport> CreateReportAsync(AuditReportValues values, Guid? engagementId, CancellationToken ct)
    {
        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, AuditPermissions.ReportNumbering, new NumberingContext(values.ReportDate ?? today), ct).ConfigureAwait(false);
        var report = AuditReport.Create(number, values, engagementId);
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return report;
    }

    public async Task<Finding> CreateFindingAsync(AuditReport report, NewFindingInput input, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var values = input.Finding with { AuditorEmployeeId = input.Finding.AuditorEmployeeId ?? report.LeadAuditorEmployeeId };
        var department = await reader.DepartmentOfAsync(values.ResponsibleUnitId, settings.DepartmentLevelCode, ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, AuditPermissions.FindingNumbering, new NumberingContext(values.FindingDate), ct).ConfigureAwait(false);
        var finding = Finding.Create(number, report.Id, values, department);
        db.Findings.Add(finding);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var recommendation in input.Recommendations ?? [])
        {
            await AddRecommendationAsync(finding, recommendation, ct).ConfigureAwait(false);
        }

        await SyncDepartmentGrantAsync(finding, settings, ct).ConfigureAwait(false);
        await RecomputeAsync(finding, ct, notify: false).ConfigureAwait(false);
        if (report.Status != ReportStatuses.Draft)
        {
            await NotifyDepartmentAsync(finding, AuditNotifications.FindingCreated, null, ct).ConfigureAwait(false);
        }

        return finding;
    }

    public async Task<Recommendation> AddRecommendationAsync(Finding finding, RecommendationValues values, CancellationToken ct)
    {
        var number = await numbering.NextAsync(db, AuditPermissions.RecommendationNumbering, new NumberingContext(finding.FindingDate), ct).ConfigureAwait(false);
        var recommendation = Recommendation.Create(number, finding.Id, values);
        recommendation.Refresh(await deadlines.TodayAsync(ct).ConfigureAwait(false));
        db.Recommendations.Add(recommendation);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return recommendation;
    }

    // ---- Follow-ups ----

    /// <summary>Records a follow-up; applies it at once, or starts the review when the setting is on (ADR 0133).</summary>
    public async Task<Result<FollowUp>> RecordFollowUpAsync(Recommendation recommendation, Finding finding, FollowUpValues values, Guid? recordedBy, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, AuditPermissions.FollowUpNumbering, new NumberingContext(values.FollowUpOn), ct).ConfigureAwait(false);
        var followUp = FollowUp.Create(number, recommendation.Id, values, recordedBy, settings.FollowUpReview);
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (!settings.FollowUpReview)
        {
            await ApplyFollowUpAsync(followUp, recommendation, finding, ct).ConfigureAwait(false);
            return followUp;
        }

        // Resolved here: the engine depends on the approval hooks, and the audit hook depends on this service.
        var approvals = (IApprovalEngine)services.GetService(typeof(IApprovalEngine))!;
        var submitted = await approvals.SubmitAsync(new ApprovalSubmission(
            AuditPermissions.FollowUpReviewProcess,
            new ApprovalSubject(Recommendation.EntityType, recommendation.Id, followUp.Number, AuditNotifier.Short(recommendation.ActionAr ?? recommendation.ActionEn),
                AuditNotifier.Short(recommendation.ActionEn ?? recommendation.ActionAr), recordedBy, null, finding.ResponsibleUnitId),
            new Dictionary<string, object?>(), values.Comment), db, ct).ConfigureAwait(false);
        return submitted.IsFailure ? submitted.Error : followUp;
    }

    public async Task ApplyFollowUpAsync(FollowUp followUp, Recommendation recommendation, Finding finding, CancellationToken ct)
    {
        recommendation.ApplyFollowUp(followUp.Pct, followUp.FollowUpOn, followUp.AchievedOn, followUp.NextFollowUpOn, followUp.Comment);
        followUp.MarkApplied();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RecomputeAsync(finding, ct, updateText: $"{Math.Round(followUp.Pct * 100, 0)} %").ConfigureAwait(false);
    }

    // ---- Derivation (ADR 0131) ----

    /// <summary>Re-derives recommendation, finding and report statuses with today's date; notifies on closure. Saves.</summary>
    public async Task RecomputeAsync(Finding finding, CancellationToken ct, bool notify = true, string? updateText = null)
    {
        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var recommendations = await db.Recommendations.Where(r => r.FindingId == finding.Id).ToListAsync(ct).ConfigureAwait(false);
        foreach (var recommendation in recommendations)
        {
            recommendation.Refresh(today);
        }

        var before = finding.Status;
        var changed = finding.Refresh(recommendations.Select(r => new FindingStatus.RecommendationState(r.Status, r.LatestPct)).ToList(), today);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (changed && AuditFindingStatuses.IsFinal(finding.Status) && !AuditFindingStatuses.IsFinal(before))
        {
            await events.PublishAsync(new AuditFindingClosed(finding.Id, finding.Number, finding.Status, finding.ResponsibleUnitId, clock.UtcNow), db, ct).ConfigureAwait(false);
            if (notify)
            {
                await NotifyDepartmentAsync(finding, AuditNotifications.FindingClosed, finding.Status == AuditFindingStatuses.RiskAccepted ? "Risk accepted / قبول المخاطر" : "Closed / مغلقة",
                    ct).ConfigureAwait(false);
            }
        }
        else if (notify && updateText is not null)
        {
            await NotifyDepartmentAsync(finding, AuditNotifications.FindingUpdated, updateText, ct).ConfigureAwait(false);
        }

        await RecomputeReportAsync(finding.ReportId, ct).ConfigureAwait(false);
    }

    public async Task RecomputeReportAsync(Guid reportId, CancellationToken ct)
    {
        var report = await db.Reports.FirstAsync(r => r.Id == reportId, ct).ConfigureAwait(false);
        var statuses = await db.Findings.Where(f => f.ReportId == reportId).Select(f => f.Status).ToListAsync(ct).ConfigureAwait(false);
        if (report.Refresh(statuses, clock.UtcNow))
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    // ---- Department visibility (ADR 0130) ----

    /// <summary>Grants (department mode) or revokes (section mode) the unit record role of the finding's department.</summary>
    public async Task SyncDepartmentGrantAsync(Finding finding, AuditMgmtSettings settings, CancellationToken ct)
    {
        var holders = await recordRoles.GetHoldersAsync(Finding.EntityType, finding.Id, ct).ConfigureAwait(false);
        var current = holders.Where(h => h.RoleCode == AuditPermissions.DepartmentReaderRole && h.PrincipalKind == RecordPrincipalKinds.Unit).Select(h => h.PrincipalId).ToList();
        IReadOnlyList<Guid> wanted = settings.DepartmentScope == DepartmentScopes.Department ? [finding.DepartmentUnitId] : [];
        foreach (var unit in wanted.Where(u => !current.Contains(u)))
        {
            await recordRoles.GrantInTransactionAsync(db, Finding.EntityType, finding.Id, RecordPrincipalKinds.Unit, unit, AuditPermissions.DepartmentReaderRole, ct)
                .ConfigureAwait(false);
        }

        foreach (var unit in current.Where(u => !wanted.Contains(u)))
        {
            await recordRoles.RevokeInTransactionAsync(db, Finding.EntityType, finding.Id, RecordPrincipalKinds.Unit, unit, AuditPermissions.DepartmentReaderRole, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>After a settings change: recomputes every finding's department and its grant.</summary>
    public async Task ResyncDepartmentsAsync(AuditMgmtSettings settings, CancellationToken ct)
    {
        var findings = await db.Findings.ToListAsync(ct).ConfigureAwait(false);
        var cache = new Dictionary<Guid, Guid>();
        foreach (var finding in findings)
        {
            if (!cache.TryGetValue(finding.ResponsibleUnitId, out var department))
            {
                cache[finding.ResponsibleUnitId] = department =
                    await reader.DepartmentOfAsync(finding.ResponsibleUnitId, settings.DepartmentLevelCode, ct).ConfigureAwait(false);
            }

            finding.SetDepartment(department);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var finding in findings)
        {
            await SyncDepartmentGrantAsync(finding, settings, ct).ConfigureAwait(false);
        }
    }

    // ---- Notices ----

    /// <summary>Optional department notices (setting <c>notifyDepartments</c>): head of the responsible unit and the recommendation contacts.</summary>
    public async Task NotifyDepartmentAsync(Finding finding, Platform.Notifications.NotificationTemplateDefinition template, string? text, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (!settings.NotifyDepartments)
        {
            return;
        }

        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == finding.ReportId, ct).ConfigureAwait(false);
        if (report.Status == ReportStatuses.Draft)
        {
            return;
        }

        var contacts = await db.Recommendations.AsNoTracking().Where(r => r.FindingId == finding.Id && r.ContactEmployeeId != null)
            .Select(r => r.ContactEmployeeId).ToListAsync(ct).ConfigureAwait(false);
        var head = await reader.HeadOfAsync(finding.ResponsibleUnitId, ct).ConfigureAwait(false);
        await notifier.SendAsync(template, contacts.Append(head), AuditNotifier.Values(finding.Number, finding.TitleAr, finding.TitleEn, text: text), Link(finding), db, ct)
            .ConfigureAwait(false);
    }

    public static string Link(Finding finding) => $"/audit-management/findings/{finding.Id:D}";

    public static string Link(AuditReport report) => $"/audit-management/reports/{report.Id:D}";
}
