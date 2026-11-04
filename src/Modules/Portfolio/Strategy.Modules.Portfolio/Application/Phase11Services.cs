using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Modules.Teams.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.AI.Application;
using Strategy.Platform.AI.Tools;
using Strategy.Platform.Identity.Wasel;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Scheduling;

namespace Strategy.Modules.Portfolio.Application;

// ---------------------------------------------------------------- access job (ADR 0115)

/// <summary>
/// Hourly, idempotent: marks expired vendor members and assignments, ends access of closed projects (when <c>endAtClosure</c>) and disables dormant
/// vendor accounts. Expiry itself needs no job: the record-role grants carry their end date.
/// </summary>
internal sealed class WaselAccessRunner(
    PortfolioDbContext db, RequestReader reader, IClock clock, IVendorAccounts accounts, WaselManagementService management, WaselGrants grants,
    IRoleHolderQuery roles, INotificationService notifications)
{
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var settings = (await reader.SettingsAsync(ct).ConfigureAwait(false)).Wasel;
        var now = clock.UtcNow;
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var ended = 0;
        var expired = 0;
        var dormant = 0;

        // 1. Assignments past their access date, or of closed projects.
        var active = await db.VendorAssignments.Where(a => a.Status == AssignmentStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        var projectIds = active.Select(a => a.ProjectId).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        foreach (var assignment in active)
        {
            var project = projects[assignment.ProjectId];
            var closed = project.Status is ProjectStatuses.Cancelled or ProjectStatuses.Completed || project.Stage is ProjectStages.Impact or ProjectStages.Completed;
            if (assignment.AccessUntil is { } until && until < today)
            {
                await management.EndAssignmentAsync(project, assignment, AssignmentStatuses.Ended, VendorMemberStatuses.Expired, "access-expired", ct).ConfigureAwait(false);
                ended++;
            }
            else if (closed && (settings.EndAtClosure || assignment.ExpiryRule == ExpiryRules.ProjectClosure))
            {
                await management.EndAssignmentAsync(project, assignment, AssignmentStatuses.Ended, VendorMemberStatuses.Expired, "project-closed", ct).ConfigureAwait(false);
                ended++;
            }
        }

        // 2. Members whose grant ended (validity reached) while the assignment stays (e.g. a shortened date).
        foreach (var member in await db.VendorMembers.Where(m => m.Status == VendorMemberStatuses.Active && m.ValidTo != null && m.ValidTo <= now).ToListAsync(ct)
                     .ConfigureAwait(false))
        {
            member.End(VendorMemberStatuses.Expired, "access-expired");
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await grants.RevokeAsync(member, ct).ConfigureAwait(false);
            expired++;
        }

        // 3. Dormant accounts.
        if (settings.DormantDays > 0)
        {
            var limit = now.AddDays(-settings.DormantDays);
            var candidates = await db.VendorUsers.Where(u => (u.Status == VendorUserStatuses.Active || u.Status == VendorUserStatuses.Invited) && u.InvitedAt < limit)
                .ToListAsync(ct).ConfigureAwait(false);
            var signIns = await accounts.GetManyAsync(candidates.Select(u => u.UserId).ToList(), ct).ConfigureAwait(false);
            foreach (var user in candidates)
            {
                var last = signIns.GetValueOrDefault(user.UserId)?.LastSignInAt ?? user.InvitedAt;
                if (last >= limit)
                {
                    continue;
                }

                user.Disable(VendorUserStatuses.Dormant, $"dormant {settings.DormantDays} days", now);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await accounts.SetActiveAsync(user.UserId, false, ct).ConfigureAwait(false);
                var holders = await roles.GetHoldersAsync(PortfolioPermissions.PmOfficeRoleKey, ct).ConfigureAwait(false);
                if (holders.UserIds.Count > 0)
                {
                    await notifications.SendAsync(new NotificationRequest(PortfolioNotifications.WaselDormant.Key, holders.UserIds.Select(NotificationRecipient.User).ToList(),
                        new Dictionary<string, string> { ["actor"] = user.Email, ["text"] = settings.DormantDays.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        $"/portfolio/vendors/{user.VendorId:D}"), null, ct).ConfigureAwait(false);
                }

                dormant++;
            }
        }

        return $"ended={ended} expired={expired} dormant={dormant}";
    }
}

internal sealed class WaselAccessJob(RequestContext requestContext, WaselAccessRunner runner) : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "portfolio.wasel-access", "صلاحيات بوابة واصل", "Wasel vendor access", "0 20 * * * ?", typeof(WaselAccessJob),
        "ينهي وصول الموردين عند انتهاء العقد أو إغلاق المشروع ويعطّل الحسابات الخاملة.",
        "Ends vendor access at the contract end or project closure and disables dormant vendor accounts.");

    protected override Task<string?> RunAsync(CancellationToken cancellationToken) => RunInnerAsync(cancellationToken);

    private async Task<string?> RunInnerAsync(CancellationToken ct) => await runner.RunAsync(ct).ConfigureAwait(false);
}

// ---------------------------------------------------------------- AI review of a vendor submission (PM side, ADR 0117)

internal sealed class VendorReviewAiService(AiGateway gateway, WaselManagementService management, ProjectLoader loader, IRequestContext context, PortfolioDbContext db)
{
    public const string Feature = "portfolio-vendor-review";

    public static readonly AiFeatureDefinition Definition = new(Feature, "مراجعة تحديث المورد", "Vendor submission review", PortfolioPermissions.AiAssist);

    private static readonly JsonElement Schema = ToolRegistrationExtensions.Schema("""
        {"type":"object","required":["summary","returnCommentAr","returnCommentEn"],"properties":{"summary":{"type":"string","maxLength":3000},
          "returnCommentAr":{"type":"string","maxLength":2000},"returnCommentEn":{"type":"string","maxLength":2000}}}
        """);

    public async Task<Result<VendorReviewAiDto>> ReviewAsync(Guid submissionId, CancellationToken ct)
    {
        var facts = await management.ReviewFactsAsync(submissionId, ct).ConfigureAwait(false);
        if (facts.IsFailure)
        {
            return facts.Error;
        }

        var (submission, flags) = facts.Value;
        var projectId = await db.VendorSubmissions.AsNoTracking().Where(s => s.Id == submissionId).Select(s => s.ProjectId).FirstAsync(ct).ConfigureAwait(false);
        var project = await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false);
        if (project is null || !await loader.CanAsync(context.User, PortfolioPermissions.AiAssist, project, ct).ConfigureAwait(false))
        {
            return new VendorReviewAiDto(flags, null, null, null, false);
        }

        var data = new StringBuilder();
        data.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Project {project.Number}: {project.NameEn ?? project.NameAr}. Period {submission.Period}. Vendor: {submission.VendorNameEn ?? submission.VendorNameAr}.");
        foreach (var item in submission.Items)
        {
            data.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"- {item.Code} {item.TitleEn ?? item.TitleAr}: weight {item.Weight:0.####}, last approved {item.Previous?.Approved ?? 0:0.####}, vendor {item.Vendor.Achieve:0.####}, " +
                $"actual start {item.Vendor.ActualStart}, actual end {item.Vendor.ActualEnd}, evidence files {item.Evidence.Count}, note: {item.Vendor.ProgressNote}, " +
                $"causality: {item.Vendor.Causality}, flags: {string.Join(", ", item.Flags)}").AppendLine();
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(Feature, new Dictionary<string, string>(), [new AiDocument("submission", data.ToString())], Schema,
            PortfolioPermissions.SystemCode, ResourceType: PortfolioPermissions.ProjectResource, ResourceId: projectId.ToString("D")), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            // AI is optional: the server-computed flags are still returned (CLAUDE.md §12, never block on AI).
            return new VendorReviewAiDto(flags, null, null, null, false);
        }

        var o = call.Value.Output;
        return new VendorReviewAiDto(flags, Str(o, "summary"), Str(o, "returnCommentAr"), Str(o, "returnCommentEn"), true);
    }

    private static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

// ---------------------------------------------------------------- capacity view (SPEC §1.12, ADR 0116)

public sealed record CapacityAllocationDto(
    string Source, Guid? TeamId, Guid? ProjectId, string? Number, string? NameAr, string? NameEn, decimal? AllocationPercent, string? ProjectNumber);

public sealed record CapacityRowDto(
    Guid EmployeeId, string? NameAr, string? NameEn, Guid? UnitId, string? UnitNameAr, string? UnitNameEn, decimal Total, bool OverAllocated, int NotSet,
    IReadOnlyList<CapacityAllocationDto> Allocations);

public sealed record CapacityDto(Guid? UnitId, bool IncludeBelow, decimal Threshold, int Employees, int OverAllocated, IReadOnlyList<CapacityRowDto> Rows);

internal sealed class CapacityService(
    PortfolioDbContext db, RequestReader reader, IOrgDirectory org, IEmployeeDirectory employees, IServiceProvider services, IRequestContext context)
{
    public async Task<Result<CapacityDto>> GetAsync(Guid? unitId, bool below, bool overOnly, CancellationToken ct)
    {
        var unit = unitId;
        if (unit is null && context.User.EmployeeIdOf() is { } me)
        {
            unit = (await employees.GetAsync(me, ct).ConfigureAwait(false))?.UnitId;
        }

        if (unit is null)
        {
            return Error.Validation([new FieldError("unitId", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (await org.GetUnitAsync(unit.Value, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        var units = below ? await org.GetSelfAndDescendantIdsAsync(unit.Value, ct).ConfigureAwait(false) : [unit.Value];
        var employeeIds = await employees.GetEmployeeIdsInUnitsAsync(units, ct).ConfigureAwait(false);
        var people = await employees.GetManyAsync(employeeIds, ct).ConfigureAwait(false);
        var threshold = (await reader.SettingsAsync(ct).ConfigureAwait(false)).CapacityThreshold;

        // (a) Teams: active memberships of active teams; project teams are labelled with their project.
        var teamAllocations = services.GetService(typeof(ITeamAllocationDirectory)) is ITeamAllocationDirectory teams
            ? await teams.ForEmployeesAsync(employeeIds, ct).ConfigureAwait(false)
            : [];
        var teamIds = teamAllocations.Select(t => t.TeamId).Distinct().ToList();
        var projectsByTeam = await db.Projects.AsNoTracking().Where(p => p.TeamId != null && teamIds.Contains(p.TeamId.Value))
            .Select(p => new { TeamId = p.TeamId!.Value, p.Id, p.Number }).ToListAsync(ct).ConfigureAwait(false);

        // (b) Proposed charter members of active projects without a formed team (no double count).
        var proposed = await db.Members.AsNoTracking().Where(m => employeeIds.Contains(m.EmployeeId) && m.Status == ItemStatuses.Active)
            .Join(db.Projects.AsNoTracking().Where(p => p.Status == ProjectStatuses.Active && p.TeamId == null), m => m.ProjectId, p => p.Id,
                (m, p) => new { m.EmployeeId, p.Id, p.Number, p.NameAr, p.NameEn, m.AllocationPercent }).ToListAsync(ct).ConfigureAwait(false);

        var unitNames = await reader.UnitsAsync(people.Values.Select(p => (Guid?)p.UnitId), ct).ConfigureAwait(false);
        var rows = new List<CapacityRowDto>();
        foreach (var id in employeeIds)
        {
            var allocations = teamAllocations.Where(t => t.EmployeeId == id).Select(t =>
                {
                    var project = projectsByTeam.FirstOrDefault(p => p.TeamId == t.TeamId);
                    return new CapacityAllocationDto("team", t.TeamId, project?.Id, t.TeamNumber, t.TeamNameAr, t.TeamNameEn, t.AllocationPercent, project?.Number);
                })
                .Concat(proposed.Where(p => p.EmployeeId == id && CapacityCalculator.CountProposedMember(null))
                    .Select(p => new CapacityAllocationDto("project", null, p.Id, p.Number, p.NameAr, p.NameEn, p.AllocationPercent, p.Number)))
                .ToList();
            var total = CapacityCalculator.Total(allocations.Select(a => a.AllocationPercent));
            var over = CapacityCalculator.OverAllocated(total, threshold);
            if (overOnly && !over)
            {
                continue;
            }

            var person = people.GetValueOrDefault(id);
            var unitDto = person is null ? null : unitNames.GetValueOrDefault(person.UnitId);
            rows.Add(new CapacityRowDto(id, person?.NameAr, person?.NameEn, person?.UnitId, unitDto?.NameAr, unitDto?.NameEn, total, over,
                allocations.Count(a => a.AllocationPercent is null), allocations));
        }

        var ordered = rows.OrderByDescending(r => r.Total).ThenBy(r => r.NameEn ?? r.NameAr, StringComparer.Ordinal).ToList();
        return new CapacityDto(unit, below, threshold, ordered.Count, ordered.Count(r => r.OverAllocated), ordered);
    }
}

// ---------------------------------------------------------------- quarterly portfolio review (SPEC §1.12, ADR 0116)

public sealed record ReviewListItemDto(Guid Id, string Period, string? Title, DateOnly? ReviewDate, string Status, int Decisions, DateTimeOffset? CompletedAt);

public sealed record ReviewProjectDto(
    Guid ProjectId, string Number, string? NameAr, string? NameEn, string Stage, string Status, decimal Performance, string ProgressStatus, decimal? Budget, string? Decision,
    string? Comment, Guid? ChangeRequestId, string? ChangeRequestNumber, string? ChangeRequestStatus, uint? Version);

public sealed record ReviewDto(
    Guid Id, string Period, string? Title, DateOnly? ReviewDate, string? Notes, string Status, DateTimeOffset? CompletedAt, IReadOnlyList<ReviewProjectDto> Projects,
    bool CanManage, uint Version);

public sealed record ReviewInput(string Period, string? Title, DateOnly? ReviewDate, string? Notes);

public sealed record ReviewDecisionInput(string Decision, string? Comment);

internal sealed class PortfolioReviewService(
    PortfolioDbContext db, ProjectLoader loader, ChangeRequestService changes, CostVisibility costs, IAccessService access, IRequestContext context, IClock clock)
{
    public async Task<IReadOnlyList<ReviewListItemDto>> ListAsync(CancellationToken ct) =>
        await db.Reviews.AsNoTracking().OrderByDescending(r => r.Period).ThenByDescending(r => r.CreatedAt)
            .Select(r => new ReviewListItemDto(r.Id, r.Period, r.Title, r.ReviewDate, r.Status, db.ReviewDecisions.Count(d => d.ReviewId == r.Id), r.CompletedAt))
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<Result<ReviewDto>> CreateAsync(ReviewInput input, CancellationToken ct)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var review = PortfolioReview.Create(input.Period, input.Title, input.ReviewDate, input.Notes);
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(review.Id, ct).ConfigureAwait(false);
    }

    public async Task<Result<ReviewDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var review = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (review is null)
        {
            return Error.NotFound();
        }

        var decisions = await db.ReviewDecisions.AsNoTracking().Where(d => d.ReviewId == id).ToListAsync(ct).ConfigureAwait(false);
        var decided = decisions.Select(d => d.ProjectId).ToList();
        var visible = await loader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        var projects = await visible.Where(p => (p.Status == ProjectStatuses.Active || p.Status == ProjectStatuses.OnHold) || decided.Contains(p.Id))
            .OrderBy(p => p.Number).ToListAsync(ct).ConfigureAwait(false);
        var crIds = decisions.Where(d => d.ChangeRequestId != null).Select(d => d.ChangeRequestId!.Value).ToList();
        var crs = await db.ChangeRequests.AsNoTracking().Where(c => crIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var costVisible = await costs.VisibleSetAsync(context.User, projects.Select(p => p.Id).ToList(), ct).ConfigureAwait(false);
        var rows = projects.Select(p =>
        {
            var d = decisions.FirstOrDefault(x => x.ProjectId == p.Id);
            var cr = d?.ChangeRequestId is { } cid ? crs.GetValueOrDefault(cid) : null;
            return new ReviewProjectDto(p.Id, p.Number, p.NameAr, p.NameEn, p.Stage, p.Status, p.Performance, p.ProgressStatus, costVisible.Contains(p.Id) ? p.Budget : null,
                d?.Decision, d?.Comment, d?.ChangeRequestId, cr?.Number, cr?.Status, d?.Version);
        }).ToList();
        var canManage = await access.CanAsync(context.User, PortfolioPermissions.ReviewManage, cancellationToken: ct).ConfigureAwait(false);
        return new ReviewDto(review.Id, review.Period, review.Title, review.ReviewDate, review.Notes, review.Status, review.CompletedAt, rows, canManage, review.Version);
    }

    public async Task<Result<ReviewDto>> DecideAsync(Guid id, Guid projectId, ReviewDecisionInput input, CancellationToken ct)
    {
        var review = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (review is null || await loader.FindAsync(projectId, context.User, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound();
        }

        if (review.Status != ReviewStatuses.Draft)
        {
            return Error.Rule("portfolio.review.completed", "portfolio.errors.reviewCompleted");
        }

        if (!ReviewDecisions.All.Contains(input.Decision))
        {
            return Error.Validation([new FieldError("decision", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (input.Comment is { Length: > 4000 } || (input.Decision != ReviewDecisions.Continue && string.IsNullOrWhiteSpace(input.Comment)))
        {
            return Error.Validation([new FieldError("comment", "validation.notEmpty", "validation.notEmpty")]);
        }

        var decision = await db.ReviewDecisions.FirstOrDefaultAsync(d => d.ReviewId == id && d.ProjectId == projectId, ct).ConfigureAwait(false);
        if (decision is null)
        {
            decision = ReviewDecision.Create(id, projectId);
            db.ReviewDecisions.Add(decision);
        }

        decision.Set(input.Decision, input.Comment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Completing never changes a project (ADR 0116): hold / cancel start a change request submitted into the normal approval (left as draft when another
    /// is already under review); re-scope opens a draft details change request for the PM.
    /// </summary>
    public async Task<Result<ReviewDto>> CompleteAsync(Guid id, CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (review is null)
        {
            return Error.NotFound();
        }

        if (review.Status != ReviewStatuses.Draft)
        {
            return Error.Rule("portfolio.review.completed", "portfolio.errors.reviewCompleted");
        }

        var decisions = await db.ReviewDecisions.Where(d => d.ReviewId == id && d.ChangeRequestId == null).ToListAsync(ct).ConfigureAwait(false);
        foreach (var decision in decisions)
        {
            if (ReviewDecisions.ChangeTypeFor(decision.Decision) is not { } type)
            {
                continue;
            }

            var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == decision.ProjectId, ct).ConfigureAwait(false);
            if (project.Status is not (ProjectStatuses.Active or ProjectStatuses.OnHold) || (type == ChangeTypes.Hold && project.Status == ProjectStatuses.OnHold))
            {
                continue;
            }

            var reason = $"{review.Period} — {decision.Comment}";
            var values = new ChangeRequestValues(type, type == ChangeTypes.Details ? ["scope"] : null, reason, null, null, null, null,
                type == ChangeTypes.Details ? $"{project.ScopeOfWork}\n[{review.Period}] {decision.Comment}".Trim() : null, null, null, null, false, null, null, null, null);
            var created = await changes.CreateAsync(project.Id, new ChangeRequestInput(values), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return created.Error;
            }

            decision.LinkChangeRequest(created.Value.Id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (ReviewDecisions.SubmitsAtOnce(decision.Decision)
                && !await db.ChangeRequests.AnyAsync(c => c.ProjectId == project.Id && c.Status == ChangeStatuses.UnderReview, ct).ConfigureAwait(false))
            {
                var submitted = await changes.SubmitAsync(created.Value.Id, created.Value.Version, reason, ct).ConfigureAwait(false);
                if (submitted.IsFailure && submitted.Error.Kind is not (ErrorKind.BusinessRule or ErrorKind.Validation))
                {
                    return submitted.Error;
                }
            }
        }

        var tracked = await db.Reviews.FirstAsync(r => r.Id == id, ct).ConfigureAwait(false);
        tracked.Complete(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    private static List<FieldError> Validate(ReviewInput input)
    {
        var errors = new List<FieldError>();
        if (!PortfolioReview.ValidPeriod(input.Period))
        {
            errors.Add(new FieldError("period", "portfolio.errors.reviewPeriod", "portfolio.errors.reviewPeriod"));
        }

        if (input.Title is { Length: > 300 } || input.Notes is { Length: > 4000 })
        {
            errors.Add(new FieldError("title", "validation.maximumLength", "validation.maximumLength"));
        }

        return errors;
    }
}
