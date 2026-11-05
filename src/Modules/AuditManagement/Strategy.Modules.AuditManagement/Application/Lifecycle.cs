using Microsoft.EntityFrameworkCore;
using Strategy.Modules.AuditManagement.Domain;
using Strategy.Modules.AuditManagement.Infrastructure;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Teams.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.AuditManagement.Application;

public sealed record AuditEntityDto(Guid Id, AuditableEntityValues Values, AuditUnitDto? Unit, string? RiskNumber, string Origin, decimal RiskScore, uint Version);

public sealed record AuditQaipDto(decimal PlanCompletion, decimal ReportTimeliness, decimal ClosureRate, decimal? Satisfaction, int Engagements, int Completed,
    int ReportsIssued, int ReportsTimely);

public sealed record AuditEngagementListItemDto(Guid Id, string Number, string? TitleAr, string? TitleEn, string Stage, int? PlannedQuarter, DateOnly? PlannedStart,
    DateOnly? PlannedEnd, decimal RiskScore, string? EntityNameAr, string? EntityNameEn, AuditPersonDto? LeadAuditor, Guid? ReportId);

public sealed record AuditPlanDto(Guid Id, int Year, string? TitleAr, string? TitleEn, string Status, string? Note, DateTimeOffset? ApprovedAt,
    IReadOnlyList<AuditEngagementListItemDto> Engagements, AuditQaipDto Qaip, IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> QuarterStatus, bool CanEdit, uint Version);

public sealed record AuditTeamDto(Guid Id, string? Number, string? NameAr, string? NameEn, string Status, IReadOnlyList<AuditPersonDto> Members);

public sealed record AuditEngagementDto(Guid Id, string Number, Guid? PlanId, EngagementValues Values, string Stage, decimal RiskScore, DateOnly? FieldworkEndOn,
    Guid? ReportId, string? ReportNumber, string? ReportStatus, int? Satisfaction, string? SatisfactionComment, string? CancelReason, AuditTeamDto? Team,
    AuditPersonDto? LeadAuditor, string? EntityNameAr, string? EntityNameEn, int WorkPapers, int WorkPapersReviewed, IReadOnlyList<string> NextActions, uint Version);

public sealed record AuditWorkPaperDto(Guid Id, Guid EngagementId, WorkPaperValues Values, string Status, AuditPersonDto? PreparedBy, AuditPersonDto? ReviewedBy,
    DateTimeOffset? ReviewedAt, uint Version);

public sealed record AuditPlanInput(int Year, string? TitleAr, string? TitleEn);

public sealed record AuditPlanBody(string? TitleAr, string? TitleEn, uint Version);

public sealed record AuditEngagementInput(Guid? PlanId, EngagementValues Values);

public sealed record AuditEngagementBody(EngagementValues Values, uint Version);

public sealed record AuditTransitionInput(string Action, string? Reason);

public sealed record AuditSurveyInput(int Score, string? Comment);

public sealed record AuditEntityBody(AuditableEntityValues Values, uint Version);

public sealed record AuditWorkPaperBody(WorkPaperValues Values, uint Version);

/// <summary>
/// Layer 2 — audit lifecycle (SPEC §10.4, ADR 0136): universe, risk-based plan with approval, engagements, programmes and working papers, QAIP.
/// The endpoints sit behind the <c>auditmgmt.lifecycle</c> switch; every method also requires <c>auditmgmt.lifecycle.manage</c> (work papers
/// <c>auditmgmt.workpaper.manage</c>), so non-audit users never see them.
/// </summary>
internal sealed class AuditLifecycle(
    AuditMgmtDbContext db, AuditReader reader, AuditService service, AuditDeadlines deadlines, INumberingService numbering, IApprovalEngine approvals, IOrgDirectory org,
    IRequestContext context, IServiceProvider services, IAccessService access, Platform.Kernel.Time.IClock clock)
{
    // ---- Universe ----

    public async Task<Result<IReadOnlyList<AuditEntityDto>>> UniverseAsync(CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var rows = await db.Universe.AsNoTracking().OrderByDescending(e => e.Active).ThenBy(e => e.NameEn ?? e.NameAr).ToListAsync(ct).ConfigureAwait(false);
        return Result.Success(await EntitiesAsync(rows, ct).ConfigureAwait(false));
    }

    public async Task<Result<AuditEntityDto>> SaveEntityAsync(Guid? id, AuditableEntityValues values, uint version, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(values.NameAr) && string.IsNullOrWhiteSpace(values.NameEn))
        {
            return Error.Validation([new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage")]);
        }

        AuditableEntityRecord entity;
        if (id is { } existing)
        {
            entity = await db.Universe.FirstOrDefaultAsync(e => e.Id == existing, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException();
            db.Entry(entity).Property(e => e.Version).OriginalValue = version;
            entity.Edit(values);
        }
        else
        {
            entity = AuditableEntityRecord.Create(values, "manual");
            db.Universe.Add(entity);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await EntitiesAsync([entity], ct).ConfigureAwait(false))[0];
    }

    /// <summary>Pulls units at the department level from the organisation and the risks the auditor may read (ADR 0136). Idempotent.</summary>
    public async Task<Result<IReadOnlyList<AuditEntityDto>>> SyncUniverseAsync(CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var existing = await db.Universe.ToListAsync(ct).ConfigureAwait(false);
        var lastAudits = await db.Findings.AsNoTracking().GroupBy(f => f.DepartmentUnitId).Select(g => new { g.Key, Last = g.Max(f => f.FindingDate) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct).ConfigureAwait(false);
        foreach (var unit in await org.ListActiveUnitsAtLevelAsync(settings.DepartmentLevelCode, ct).ConfigureAwait(false))
        {
            if (existing.All(e => e.UnitId != unit.Id || e.KindCode != "unit"))
            {
                db.Universe.Add(AuditableEntityRecord.Create(new AuditableEntityValues("unit", unit.NameAr, unit.NameEn, unit.Id, null, null,
                    lastAudits.TryGetValue(unit.Id, out var last) ? last : null, true), "org"));
            }
        }

        if (services.GetService(typeof(IRiskDirectory)) is IRiskDirectory risks)
        {
            foreach (var risk in await risks.SearchAsync(null, context.User, 200, ct).ConfigureAwait(false))
            {
                var rating = risk.ResidualBand is { } band && Contracts.AuditPriorities.Defaults.Contains(band) ? band : null;
                var known = existing.FirstOrDefault(e => e.RiskId == risk.Id);
                if (known is null)
                {
                    db.Universe.Add(AuditableEntityRecord.Create(new AuditableEntityValues("risk", risk.TitleAr, risk.TitleEn, risk.UnitId, risk.Id, rating, null, true), "risk"));
                }
                else if (known.Origin == "risk" && rating is not null && known.InherentRating != rating)
                {
                    known.Edit(known.Values() with { InherentRating = rating });
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await UniverseAsync(ct).ConfigureAwait(false);
    }

    // ---- Plans ----

    public async Task<Result<IReadOnlyList<AuditPlanDto>>> PlansAsync(CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.PlanRead, ct).ConfigureAwait(false) && !await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var plans = await db.Plans.AsNoTracking().OrderByDescending(p => p.Year).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<AuditPlanDto>();
        foreach (var plan in plans)
        {
            result.Add(await PlanDtoAsync(plan, ct).ConfigureAwait(false));
        }

        return result;
    }

    public async Task<Result<AuditPlanDto>> PlanAsync(Guid id, CancellationToken ct)
    {
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (plan is null || !await CanReadPlanAsync(plan.Id, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        return await PlanDtoAsync(plan, ct).ConfigureAwait(false);
    }

    public async Task<Result<AuditPlanDto>> CreatePlanAsync(AuditPlanInput input, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (input.Year is < 2000 or > 2100)
        {
            return Error.Validation([new FieldError("year", "validation.invalid", "validation.invalid")]);
        }

        if (await db.Plans.AnyAsync(p => p.Year == input.Year, ct).ConfigureAwait(false))
        {
            return Error.Rule("auditmgmt.planExists", "auditmgmt.errors.planExists");
        }

        var plan = AuditPlan.Create(input.Year, input.TitleAr, input.TitleEn);
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await PlanDtoAsync(plan, ct).ConfigureAwait(false);
    }

    public async Task<Result<AuditPlanDto>> UpdatePlanAsync(Guid id, AuditPlanBody body, CancellationToken ct)
    {
        var (plan, error) = await EditablePlanAsync(id, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return error!;
        }

        db.Entry(plan).Property(p => p.Version).OriginalValue = body.Version;
        plan.Edit(body.TitleAr, body.TitleEn);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await PlanDtoAsync(plan, ct).ConfigureAwait(false);
    }

    /// <summary>Submits the plan for approval (process <c>auditmgmt.plan</c>, default step: audit committee).</summary>
    public async Task<Result<AuditPlanDto>> SubmitPlanAsync(Guid id, string? comment, CancellationToken ct)
    {
        var (plan, error) = await EditablePlanAsync(id, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return error!;
        }

        if (!await db.Engagements.AnyAsync(e => e.PlanId == plan.Id && e.Stage != EngagementStages.Cancelled, ct).ConfigureAwait(false))
        {
            return Error.Rule("auditmgmt.planEmpty", "auditmgmt.errors.planEmpty");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        plan.Submit();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var submitted = await approvals.SubmitAsync(new ApprovalSubmission(AuditPermissions.PlanProcess,
            new ApprovalSubject(AuditPlan.EntityType, plan.Id, plan.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), plan.TitleAr ?? plan.TitleEn,
                plan.TitleEn ?? plan.TitleAr, context.User.EmployeeIdOf(), null, null), new Dictionary<string, object?>(), comment), db, ct).ConfigureAwait(false);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await PlanDtoAsync(plan, ct).ConfigureAwait(false);
    }

    // ---- Engagements ----

    /// <summary>
    /// Engagement team picker: teams the current user may read (Teams contract, filtered by the Teams read scope), searched by name or number.
    /// Empty when the Teams module is not installed.
    /// </summary>
    public async Task<Result<IReadOnlyList<TeamListEntry>>> TeamsAsync(string? search, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return services.GetService(typeof(ITeamsDirectory)) is ITeamsDirectory teams
            ? Result<IReadOnlyList<TeamListEntry>>.Success(await teams.SearchTeamsAsync(search, context.User, 20, cancellationToken: ct).ConfigureAwait(false))
            : Result<IReadOnlyList<TeamListEntry>>.Success([]);
    }

    public async Task<Result<IReadOnlyList<AuditEngagementListItemDto>>> EngagementsAsync(Guid? planId, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var query = db.Engagements.AsNoTracking();
        if (planId is { } p)
        {
            query = query.Where(e => e.PlanId == p);
        }

        return Result.Success(await ListAsync(await query.OrderByDescending(e => e.RiskScore).ThenBy(e => e.Number).ToListAsync(ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false));
    }

    public async Task<Result<AuditEngagementDto>> EngagementAsync(Guid id, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var engagement = await db.Engagements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        return engagement is null ? Error.NotFound() : await EngagementDtoAsync(engagement, ct).ConfigureAwait(false);
    }

    public async Task<Result<AuditEngagementDto>> CreateEngagementAsync(AuditEngagementInput input, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = await ValidateEngagementAsync(input.Values, ct).ConfigureAwait(false);
        if (input.PlanId is { } planId && await db.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct).ConfigureAwait(false) is not { Editable: true })
        {
            errors.Add(new FieldError("planId", "auditmgmt.errors.planNotEditable", "auditmgmt.errors.planNotEditable"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, AuditPermissions.EngagementNumbering, new NumberingContext(input.Values.PlannedStart ?? today), ct).ConfigureAwait(false);
        var engagement = Engagement.Create(number, input.PlanId, input.Values, await ScoreAsync(input.Values.EntityId, today, ct).ConfigureAwait(false));
        db.Engagements.Add(engagement);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await EngagementDtoAsync(engagement, ct).ConfigureAwait(false);
    }

    public async Task<Result<AuditEngagementDto>> UpdateEngagementAsync(Guid id, AuditEngagementBody body, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var engagement = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (engagement is null)
        {
            return Error.NotFound();
        }

        var errors = await ValidateEngagementAsync(body.Values, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(engagement).Property(e => e.Version).OriginalValue = body.Version;
        engagement.Edit(body.Values);
        engagement.SetRiskScore(await ScoreAsync(body.Values.EntityId, await deadlines.TodayAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await EngagementDtoAsync(engagement, ct).ConfigureAwait(false);
    }

    /// <summary>Stage transitions (§4.6): Reporting creates the draft report; Follow-up needs the report issued; close needs it closed.</summary>
    public async Task<Result<AuditEngagementDto>> TransitionAsync(Guid id, AuditTransitionInput input, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var engagement = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (engagement is null)
        {
            return Error.NotFound();
        }

        if (input.Action == EngagementActions.Cancel && string.IsNullOrWhiteSpace(input.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var report = engagement.ReportId is { } reportId ? await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct).ConfigureAwait(false) : null;
        if (input.Action == EngagementActions.ToFollowUp && report is not { Status: not (ReportStatuses.Draft or ReportStatuses.Cancelled) })
        {
            return Error.Rule("auditmgmt.reportNotIssued", "auditmgmt.errors.reportNotIssued");
        }

        if (input.Action == EngagementActions.Close && report is not null && report.Status != ReportStatuses.Closed)
        {
            return Error.Rule("auditmgmt.findingsOpen", "auditmgmt.errors.findingsOpen");
        }

        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var applied = engagement.Apply(input.Action, today, input.Reason);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        if (input.Action == EngagementActions.ToReporting && engagement.ReportId is null)
        {
            var entity = engagement.EntityId is { } entityId ? await db.Universe.FirstOrDefaultAsync(e => e.Id == entityId, ct).ConfigureAwait(false) : null;
            var created = await service.CreateReportAsync(new AuditReportValues(engagement.TitleAr, engagement.TitleEn, ReportTypes.InternalAudit, null,
                engagement.LeadAuditorEmployeeId, entity?.UnitId is { } unit ? [unit] : [], engagement.PlannedStart, today, today, [], null, engagement.Scope), engagement.Id, ct)
                .ConfigureAwait(false);
            engagement.LinkReport(created.Id);
        }

        if (input.Action == EngagementActions.ToFollowUp && engagement.EntityId is { } audited
            && await db.Universe.FirstOrDefaultAsync(e => e.Id == audited, ct).ConfigureAwait(false) is { } universe)
        {
            universe.Audited(today);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await EngagementDtoAsync(engagement, ct).ConfigureAwait(false);
    }

    public async Task<Result<AuditEngagementDto>> SurveyAsync(Guid id, AuditSurveyInput input, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var engagement = await db.Engagements.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
        if (engagement is null)
        {
            return Error.NotFound();
        }

        if (input.Score is < 1 or > 5)
        {
            return Error.Validation([new FieldError("score", "validation.inclusiveBetween", "validation.inclusiveBetween",
                new Dictionary<string, object?> { ["from"] = 1, ["to"] = 5 })]);
        }

        engagement.Survey(input.Score, input.Comment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await EngagementDtoAsync(engagement, ct).ConfigureAwait(false);
    }

    // ---- Working papers (Audit Office only) ----

    public async Task<Result<IReadOnlyList<AuditWorkPaperDto>>> WorkPapersAsync(Guid engagementId, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.WorkPapers, ct).ConfigureAwait(false) || !await db.Engagements.AnyAsync(e => e.Id == engagementId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var rows = await db.WorkPapers.AsNoTracking().Where(w => w.EngagementId == engagementId).OrderBy(w => w.Reference).ThenBy(w => w.CreatedAt).ToListAsync(ct)
            .ConfigureAwait(false);
        return Result.Success(await PapersAsync(rows, ct).ConfigureAwait(false));
    }

    public async Task<Result<AuditWorkPaperDto>> SaveWorkPaperAsync(Guid engagementId, Guid? id, WorkPaperValues values, uint version, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.WorkPapers, ct).ConfigureAwait(false) || !await db.Engagements.AnyAsync(e => e.Id == engagementId, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (string.IsNullOrWhiteSpace(values.Title))
        {
            return Error.Validation([new FieldError("title", "validation.required", "validation.required")]);
        }

        WorkPaper paper;
        if (id is { } existing)
        {
            paper = await db.WorkPapers.FirstOrDefaultAsync(w => w.Id == existing && w.EngagementId == engagementId, ct).ConfigureAwait(false) ?? throw new KeyNotFoundException();
            db.Entry(paper).Property(w => w.Version).OriginalValue = version;
            paper.Edit(values);
        }
        else
        {
            paper = WorkPaper.Create(engagementId, values);
            db.WorkPapers.Add(paper);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await PapersAsync([paper], ct).ConfigureAwait(false))[0];
    }

    public async Task<Result<AuditWorkPaperDto>> WorkPaperActionAsync(Guid engagementId, Guid id, string action, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.WorkPapers, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var paper = await db.WorkPapers.FirstOrDefaultAsync(w => w.Id == id && w.EngagementId == engagementId, ct).ConfigureAwait(false);
        if (paper is null)
        {
            return Error.NotFound();
        }

        var me = context.User.EmployeeIdOf();
        if (action == "prepare")
        {
            paper.Prepare(me);
        }
        else if (action == "sign-off")
        {
            var signed = paper.SignOff(me, clock.UtcNow);
            if (signed.IsFailure)
            {
                return signed.Error;
            }
        }
        else
        {
            return Error.NotFound();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await PapersAsync([paper], ct).ConfigureAwait(false))[0];
    }

    // ---- QAIP (also used by the KPI publishing in the monitor) ----

    public async Task<AuditQaipDto> QaipAsync(int year, Guid? planId, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var engagements = await db.Engagements.AsNoTracking().Where(e => planId == null ? e.PlannedStart == null || e.PlannedStart.Value.Year == year : e.PlanId == planId)
            .Where(e => e.Stage != EngagementStages.Cancelled).ToListAsync(ct).ConfigureAwait(false);
        var completed = engagements.Count(e => e.Stage is EngagementStages.FollowUp or EngagementStages.Closed);
        var reportIds = engagements.Where(e => e.ReportId != null).Select(e => e.ReportId!.Value).ToList();
        var reports = await db.Reports.AsNoTracking().Where(r => reportIds.Contains(r.Id) && r.IssuedAt != null).ToListAsync(ct).ConfigureAwait(false);
        var timely = reports.Count(r => engagements.FirstOrDefault(e => e.ReportId == r.Id)?.FieldworkEndOn is { } end
            && DateOnly.FromDateTime(r.IssuedAt!.Value.UtcDateTime).DayNumber - end.DayNumber <= settings.ReportTimelinessDays);
        var findings = await db.Findings.AsNoTracking().Where(f => f.Year == year && f.Status != Contracts.AuditFindingStatuses.Cancelled).Select(f => f.Status)
            .ToListAsync(ct).ConfigureAwait(false);
        var closed = findings.Count(s => s is Contracts.AuditFindingStatuses.Closed or Contracts.AuditFindingStatuses.RiskAccepted);
        var scores = engagements.Where(e => e.Satisfaction != null).Select(e => (decimal)e.Satisfaction!.Value).ToList();
        return new AuditQaipDto(engagements.Count == 0 ? 0m : Math.Round((decimal)completed / engagements.Count, 4),
            reports.Count == 0 ? 0m : Math.Round((decimal)timely / reports.Count, 4), findings.Count == 0 ? 0m : Math.Round((decimal)closed / findings.Count, 4),
            scores.Count == 0 ? null : Math.Round(scores.Average(), 2), engagements.Count, completed, reports.Count, timely);
    }

    // ---- Helpers ----

    private Task<bool> CanAsync(string permission, CancellationToken ct) => reader.CanAsync(context.User, permission, ct);

    private async Task<bool> CanReadPlanAsync(Guid planId, CancellationToken ct) =>
        await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false)
        || await access.CanAsync(context.User, AuditPermissions.PlanRead, new ResourceRef(AuditPlan.EntityType, planId), ct)
            .ConfigureAwait(false);

    private async Task<(AuditPlan? Plan, Error? Error)> EditablePlanAsync(Guid id, CancellationToken ct)
    {
        if (!await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false))
        {
            return (null, Error.Forbidden());
        }

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return (null, Error.NotFound());
        }

        return plan.Editable ? (plan, null) : (null, Error.Rule("auditmgmt.planNotEditable", "auditmgmt.errors.planNotEditable"));
    }

    private async Task<decimal> ScoreAsync(Guid? entityId, DateOnly today, CancellationToken ct)
    {
        if (entityId is not { } id || await db.Universe.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false) is not { } entity)
        {
            return AuditRiskScore.Of(null, null, today);
        }

        return AuditRiskScore.Of(entity.InherentRating, entity.LastAuditedOn, today);
    }

    private async Task<List<FieldError>> ValidateEngagementAsync(EngagementValues v, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (v.PlannedQuarter is { } q && q is < 1 or > 4)
        {
            errors.Add(new FieldError("plannedQuarter", "validation.invalid", "validation.invalid"));
        }

        if (v.PlannedStart is { } s && v.PlannedEnd is { } e && e < s)
        {
            errors.Add(new FieldError("plannedEnd", "auditmgmt.errors.endBeforeStart", "auditmgmt.errors.endBeforeStart"));
        }

        if (v.BudgetHours is < 0 or > 100000)
        {
            errors.Add(new FieldError("budgetHours", "validation.invalid", "validation.invalid"));
        }

        if (v.EntityId is { } entity && !await db.Universe.AnyAsync(x => x.Id == entity, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("entityId", "validation.invalid", "validation.invalid"));
        }

        if (v.TeamId is { } team && services.GetService(typeof(ITeamsDirectory)) is ITeamsDirectory teams
            && await teams.GetTeamAsync(team, context.User, ct).ConfigureAwait(false) is null)
        {
            errors.Add(new FieldError("teamId", "validation.invalid", "validation.invalid"));
        }

        return errors;
    }

    private async Task<IReadOnlyList<AuditEntityDto>> EntitiesAsync(IReadOnlyList<AuditableEntityRecord> rows, CancellationToken ct)
    {
        var units = await reader.UnitsAsync(rows.Select(r => r.UnitId), ct).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        return rows.Select(e => new AuditEntityDto(e.Id, e.Values(), AuditReader.Unit(e.UnitId, units), null, e.Origin,
            AuditRiskScore.Of(e.InherentRating, e.LastAuditedOn, today), e.Version)).ToList();
    }

    private async Task<AuditPlanDto> PlanDtoAsync(AuditPlan plan, CancellationToken ct)
    {
        var engagements = await db.Engagements.AsNoTracking().Where(e => e.PlanId == plan.Id).OrderByDescending(e => e.RiskScore).ThenBy(e => e.Number).ToListAsync(ct)
            .ConfigureAwait(false);
        var quarters = engagements.Where(e => e.PlannedQuarter != null).GroupBy(e => e.PlannedQuarter!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.GroupBy(e => e.Stage).ToDictionary(s => s.Key, s => s.Count()));
        return new AuditPlanDto(plan.Id, plan.Year, plan.TitleAr, plan.TitleEn, plan.Status, plan.Note, plan.ApprovedAt, await ListAsync(engagements, ct).ConfigureAwait(false),
            await QaipAsync(plan.Year, plan.Id, ct).ConfigureAwait(false), quarters, plan.Editable && await CanAsync(AuditPermissions.Lifecycle, ct).ConfigureAwait(false),
            plan.Version);
    }

    private async Task<IReadOnlyList<AuditEngagementListItemDto>> ListAsync(IReadOnlyList<Engagement> rows, CancellationToken ct)
    {
        var entityIds = rows.Where(r => r.EntityId != null).Select(r => r.EntityId!.Value).ToList();
        var entities = await db.Universe.AsNoTracking().Where(e => entityIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync(rows.Select(r => r.LeadAuditorEmployeeId), ct).ConfigureAwait(false);
        return rows.Select(e => new AuditEngagementListItemDto(e.Id, e.Number, e.TitleAr, e.TitleEn, e.Stage, e.PlannedQuarter, e.PlannedStart, e.PlannedEnd, e.RiskScore,
            e.EntityId is { } id ? entities.GetValueOrDefault(id)?.NameAr : null, e.EntityId is { } id2 ? entities.GetValueOrDefault(id2)?.NameEn : null,
            AuditReader.Person(e.LeadAuditorEmployeeId, people), e.ReportId)).ToList();
    }

    private async Task<AuditEngagementDto> EngagementDtoAsync(Engagement e, CancellationToken ct)
    {
        var report = e.ReportId is { } reportId ? await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct).ConfigureAwait(false) : null;
        var entity = e.EntityId is { } entityId ? await db.Universe.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entityId, ct).ConfigureAwait(false) : null;
        AuditTeamDto? team = null;
        if (e.TeamId is { } teamId && services.GetService(typeof(ITeamsDirectory)) is ITeamsDirectory teams
            && await teams.GetTeamAsync(teamId, context.User, ct).ConfigureAwait(false) is { } t)
        {
            var members = await reader.PeopleAsync(t.ActiveMemberEmployeeIds.Select(m => (Guid?)m), ct).ConfigureAwait(false);
            team = new AuditTeamDto(t.Id, t.Number, t.NameAr, t.NameEn, t.Status, t.ActiveMemberEmployeeIds.Select(m => AuditReader.Person(m, members)!).ToList());
        }

        var papers = await db.WorkPapers.AsNoTracking().Where(w => w.EngagementId == e.Id).Select(w => w.Status).ToListAsync(ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([e.LeadAuditorEmployeeId], ct).ConfigureAwait(false);
        var next = EngagementActions.All.Where(a => EngagementLifecycle.Next(e.Stage, a) is not null).ToList();
        return new AuditEngagementDto(e.Id, e.Number, e.PlanId, e.Values(), e.Stage, e.RiskScore, e.FieldworkEndOn, e.ReportId, report?.Number, report?.Status,
            e.Satisfaction, e.SatisfactionComment, e.CancelReason, team, AuditReader.Person(e.LeadAuditorEmployeeId, people), entity?.NameAr, entity?.NameEn, papers.Count,
            papers.Count(s => s == WorkPaperStatuses.Reviewed), next, e.Version);
    }

    private async Task<IReadOnlyList<AuditWorkPaperDto>> PapersAsync(IReadOnlyList<WorkPaper> rows, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(rows.SelectMany(r => new[] { r.PreparedByEmployeeId, r.ReviewedByEmployeeId }), ct).ConfigureAwait(false);
        return rows.Select(w => new AuditWorkPaperDto(w.Id, w.EngagementId, w.Values(), w.Status, AuditReader.Person(w.PreparedByEmployeeId, people),
            AuditReader.Person(w.ReviewedByEmployeeId, people), w.ReviewedAt, w.Version)).ToList();
    }
}
