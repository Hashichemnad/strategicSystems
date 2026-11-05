using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Benchmarking.Application;

/// <summary>
/// Moves studies with the office decisions (inside the engine's transaction, ADR 0023 / 0126): <c>bench.search</c>, <c>bench.analysis</c> (hand-off to
/// CAP, best practices, KPI benchmark — ADR 0128) and <c>bench.closure</c>.
/// </summary>
internal sealed class BenchApprovalHook(
    BenchDbContext db, BenchReader reader, BenchNotifier notifier, BenchCalendar calendar, IIntegrationEventPublisher events, IClock clock, IServiceProvider services)
    : IApprovalHook
{
    public IReadOnlyCollection<string> ProcessKeys { get; } = [BenchProcesses.Search, BenchProcesses.Analysis, BenchProcesses.Closure];

    public async Task<Result> OnEventAsync(ApprovalEvent approvalEvent, object transactionContext, CancellationToken cancellationToken)
    {
        if (approvalEvent.Kind is not (ApprovalEventKinds.Approved or ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned or ApprovalEventKinds.Cancelled))
        {
            return Result.Success();
        }

        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var study = await db.Studies.FirstOrDefaultAsync(s => s.Id == approvalEvent.ResourceId, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return Error.NotFound();
        }

        study.SetPending(null);
        var approved = approvalEvent.Kind == ApprovalEventKinds.Approved;
        var refused = approvalEvent.Kind is ApprovalEventKinds.Rejected or ApprovalEventKinds.Returned;
        Result result = Result.Success();
        switch (approvalEvent.ProcessKey)
        {
            case BenchProcesses.Search:
                result = approved ? study.Apply(StudyActions.SearchApproved, clock.UtcNow)
                    : refused ? study.Apply(StudyActions.SearchReturned, clock.UtcNow, approvalEvent.Comment)
                    : study.Apply(StudyActions.SearchReturned, clock.UtcNow);
                break;
            case BenchProcesses.Analysis:
                if (approved)
                {
                    result = study.Apply(StudyActions.AnalysisApproved, clock.UtcNow);
                    if (result.IsSuccess)
                    {
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        result = await HandOffAsync(study, transactionContext, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    result = study.Apply(StudyActions.AnalysisReturned, clock.UtcNow, approvalEvent.Comment);
                }

                break;
            default:
                if (approved)
                {
                    if (!await BenchClosure.ReadyAsync(services, study.Id, cancellationToken).ConfigureAwait(false))
                    {
                        return Error.Rule("bench.actionsOpen", "bench.errors.actionsOpen");
                    }

                    var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
                    DateOnly? impact = settings.ImpactFollowUpMonths > 0
                        ? (await calendar.TodayAsync(cancellationToken).ConfigureAwait(false)).AddMonths(settings.ImpactFollowUpMonths)
                        : null;
                    result = study.Apply(StudyActions.ClosureApproved, clock.UtcNow, impactDue: impact);
                    if (result.IsSuccess)
                    {
                        await events.PublishAsync(new LinkedRecordClosed(Study.EntityType, study.Id, "completed", clock.UtcNow), db, cancellationToken).ConfigureAwait(false);
                        await notifier.SendAsync(BenchNotifications.Closed, [study.ResponsibleEmployeeId, study.RequesterEmployeeId], BenchNotifier.Values(study),
                            BenchNotifier.Link(study), db, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    result = study.Apply(StudyActions.ClosureRefused, clock.UtcNow, approvalEvent.Comment);
                }

                break;
        }

        if (result.IsFailure)
        {
            return result;
        }

        if (approvalEvent.Kind != ApprovalEventKinds.Cancelled && (approvalEvent.ProcessKey != BenchProcesses.Closure || !approved))
        {
            var label = approved ? "Approved / معتمد" : approvalEvent.Kind == ApprovalEventKinds.Rejected ? "Rejected / مرفوض" : "Returned / معاد";
            await notifier.SendAsync(BenchNotifications.Decided, [study.ResponsibleEmployeeId], BenchNotifier.Values(study,
                text: string.IsNullOrWhiteSpace(approvalEvent.Comment) ? label : $"{label} — {approvalEvent.Comment}"), BenchNotifier.Link(study), db, cancellationToken)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>
    /// Analysis approved (ADR 0128): a CAP source for the study with one remark per development-plan item and its proposed action, best practices from
    /// the flagged rows, KPI benchmark fields from the flagged indicator rows — all in this transaction.
    /// </summary>
    private async Task<Result> HandOffAsync(Study study, object transactionContext, CancellationToken ct)
    {
        var topics = await db.Topics.AsNoTracking().Where(t => t.StudyId == study.Id && !t.Removed).ToListAsync(ct).ConfigureAwait(false);
        var items = await db.PlanItems.Where(p => p.StudyId == study.Id && !p.Removed).OrderBy(p => p.Sort).ToListAsync(ct).ConfigureAwait(false);
        if (services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap)
        {
            var viewers = topics.Where(t => t.OwnerEmployeeId is not null).Select(t => t.OwnerEmployeeId!.Value).ToList();
            if (study.ResponsibleEmployeeId is { } coordinator)
            {
                viewers.Add(coordinator);
            }

            var source = await cap.EnsureSourceAsync(new CapSourceRequest(Study.EntityType, study.Id, CapSourceTypes.Benchmarking,
                $"المقارنة المعيارية: {study.TitleAr ?? study.TitleEn} ({study.Number})", $"Benchmarking: {study.TitleEn ?? study.TitleAr} ({study.Number})", study.Scope,
                await calendar.TodayAsync(ct).ConfigureAwait(false), study.ResponsibleEmployeeId, study.ResponsibleUnitId, viewers, study.TeamId), transactionContext, ct)
                .ConfigureAwait(false);
            study.LinkCapSource(source.SourceId);
            foreach (var item in items)
            {
                var department = item.UnitId ?? study.ResponsibleUnitId;
                var text = (string? title) => string.IsNullOrWhiteSpace(item.Description) ? title : $"{title}\n{item.Description}";
                var remark = await cap.AddRemarkAsync(source.SourceId, new CapRemarkRequest(item.Id.ToString("D"), text(item.TitleAr ?? item.TitleEn),
                    text(item.TitleEn ?? item.TitleAr), CapRemarkTypes.Improvement, item.Priority, null, department,
                    department == study.ResponsibleUnitId ? study.SectionUnitId : null, null, null, Study.EntityType, study.Id,
                    [
                        new CapProposedAction(item.TitleAr ?? item.TitleEn, item.TitleEn ?? item.TitleAr, item.ActionTypeCode ?? CapActionTypes.Improvement, item.OwnerEmployeeId,
                            null, item.DueOn, item.BudgetRequired),
                    ]), transactionContext, ct).ConfigureAwait(false);
                if (remark.IsFailure)
                {
                    return remark.Error;
                }

                item.LinkRemark(remark.Value.RemarkId);
            }
        }

        var practices = await db.Practices.AsNoTracking().Where(p => p.StudyId == study.Id && p.AddToLibrary && !p.Removed).ToListAsync(ct).ConfigureAwait(false);
        var existing = await db.BestPractices.Where(b => b.StudyId == study.Id && b.PracticeRowId != null).Select(b => b.PracticeRowId).ToListAsync(ct).ConfigureAwait(false);
        var comparator = await db.StudyEntities.AsNoTracking().Where(e => e.StudyId == study.Id && e.Selected && !e.Removed).Select(e => (Guid?)e.ComparatorId).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        foreach (var p in practices.Where(p => !existing.Contains(p.Id)))
        {
            var entry = BestPractice.Create("analysis", study.Id, comparator, p.Id);
            entry.Update(p.Subject, p.Subject, p.TheirPractice ?? p.OurPractice, topics.FirstOrDefault(t => t.Id == p.TopicId)?.Title, [study.ComparisonTypeCode ?? "benchmark"],
                p.Applicable ? "applicable" : "not-applicable", null, true);
            db.BestPractices.Add(entry);
        }

        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        if (settings.KpiBenchmarkUpdate && services.GetService(typeof(IKpiBenchmarkWriter)) is IKpiBenchmarkWriter writer)
        {
            var rows = await db.Indicators.AsNoTracking().Where(i => i.StudyId == study.Id && i.UpdateKpiBenchmark && i.KpiCardId != null && !i.Removed).ToListAsync(ct).ConfigureAwait(false);
            foreach (var row in rows)
            {
                var value = row.TheirValue?.ToString("0.####", CultureInfo.InvariantCulture) ?? "—";
                await writer.UpdateBenchmarkAsync(row.KpiCardId!.Value, $"{study.Number}: {row.Indicator} = {value} ({row.Period}; {row.SourceText})".Trim(), row.TheirValue,
                    transactionContext, ct).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}

/// <summary>"Next action + who" for a study (Salesforce Current Status table, docs/design/benchmarking.md §8).</summary>
internal sealed class StudyCurrentStatus(BenchDbContext db, BenchReader reader, BenchViews views, BenchCalendar calendar, IApprovalEngine engine, IRequestContext context,
    IServiceProvider services) : ICurrentStatusProvider
{
    public string ResourceType => Study.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var study = await reader.StudyAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (study is null)
        {
            return null;
        }

        var can = await views.PermissionsAsync(study, context.User, cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync([study.ResponsibleEmployeeId, study.RequesterEmployeeId], cancellationToken).ConfigureAwait(false);
        PersonName[] coordinator = study.ResponsibleEmployeeId is { } c && people.GetValueOrDefault(c) is { } p ? [new PersonName(null, p.NameAr, p.NameEn)] : [];
        PersonName[] requester = study.RequesterEmployeeId is { } r && people.GetValueOrDefault(r) is { } q ? [new PersonName(null, q.NameAr, q.NameEn)] : [];
        CurrentStatus S(string key, string tone, string? actorKey, IReadOnlyList<PersonName> actors, Dictionary<string, BilingualText>? p = null, params CurrentStatusAction[] next) =>
            new(key, p ?? [], tone, actorKey, actors, next, study.Stage);
        if (study.Status == StudyStatuses.Cancelled)
        {
            return S("bench.currentStatus.cancelled", "neutral", null, []);
        }

        if (study.Status == StudyStatuses.Completed)
        {
            return S("bench.currentStatus.completed", "success", null, [], null, can.Impact ? [new CurrentStatusAction("impact", "bench.actions.recordImpact", "secondary")] : []);
        }

        if (study.PendingProcess is { } process)
        {
            var resource = process == BenchProcesses.Plan ? study.RequestId : study.Id;
            var view = await engine.GetStatusViewAsync(process, resource, context.User, cancellationToken).ConfigureAwait(false);
            var stage = process switch
            {
                BenchProcesses.Plan => new BilingualText("الخطة", "Plan"),
                BenchProcesses.Search => new BilingualText("البحث", "Search"),
                BenchProcesses.Analysis => new BilingualText("التحليل", "Analysis"),
                _ => new BilingualText("الإغلاق", "Closure"),
            };
            CurrentStatusAction[] next = view?.MyTaskId is { } task ? [new CurrentStatusAction("decide", "bench.actions.decide", "primary", task)] : [];
            return S("bench.currentStatus.waitingPmo", "info", "workflow.currentStatus.approvers", view?.Approvers ?? [], new() { ["stage"] = stage }, next);
        }

        switch (study.Stage)
        {
            case StudyStages.Plan:
                if (!await db.Topics.AnyAsync(t => t.StudyId == study.Id && !t.Removed, cancellationToken).ConfigureAwait(false))
                {
                    return S("bench.currentStatus.addTopic", "warning", "bench.currentStatus.requester", requester);
                }

                return S("bench.currentStatus.waitingSubmit", "warning", "bench.currentStatus.requester", requester);
            case StudyStages.Search:
                {
                    var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
                    var (day, _) = await calendar.SearchNoticeAsync(study.ResponsibleUnitId, study.TargetYear, study.TargetQuarter, settings.SearchNoticeDays,
                        settings.CountWorkingDays, cancellationToken).ConfigureAwait(false);
                    var date = new BilingualText(BenchCalendar.Date(day), BenchCalendar.Date(day));
                    return S(study.Returned ? "bench.currentStatus.correctSearch" : "bench.currentStatus.waitingSearch", study.Returned ? "warning" : "info",
                        "bench.currentStatus.coordinator", coordinator, new() { ["date"] = date },
                        can.SubmitSearch ? [new CurrentStatusAction("search", "bench.actions.fillSearch")] : []);
                }

            case StudyStages.Acquire:
                return S("bench.currentStatus.waitingVisit", "info", "bench.currentStatus.coordinator", coordinator, null,
                    can.EditVisits ? [new CurrentStatusAction("visit", "bench.actions.fillVisit")] : []);
            default:
                if (!study.AnalysisApproved)
                {
                    var items = await db.PlanItems.CountAsync(i => i.StudyId == study.Id && !i.Removed, cancellationToken).ConfigureAwait(false);
                    return items == 0
                        ? S("bench.currentStatus.addAction", "warning", "bench.currentStatus.coordinator", coordinator, null,
                            can.EditAnalysis ? [new CurrentStatusAction("analysis", "bench.actions.fillAnalysis")] : [])
                        : S(study.Returned ? "bench.currentStatus.correctAnalysis" : "bench.currentStatus.waitingAnalysis", study.Returned ? "warning" : "info",
                            "bench.currentStatus.coordinator", coordinator, null, can.SubmitAnalysis ? [new CurrentStatusAction("analysis", "bench.actions.fillAnalysis")] : []);
                }

                var progress = services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap
                    ? await cap.GetProgressAsync(Study.EntityType, study.Id, cancellationToken).ConfigureAwait(false)
                    : null;
                if (progress is { AllActionsCompleted: true })
                {
                    return S("bench.currentStatus.readyForClosure", "success", "bench.currentStatus.coordinator", coordinator, null,
                        can.RequestClosure ? [new CurrentStatusAction("closure", "bench.actions.requestClosure")] : []);
                }

                var n = (int x) => new BilingualText(x.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture));
                return S("bench.currentStatus.followActions", "info", "bench.currentStatus.coordinator", coordinator,
                    new() { ["done"] = n(progress?.CompletedCount ?? 0), ["total"] = n(progress?.ActionCount ?? 0) });
        }
    }
}

/// <summary>My Work (SPEC §0.4): studies needing my action (coordinator forms, returned plan) and the office's pending gates are approval tasks.</summary>
internal sealed class BenchMyWork(BenchReader reader) : IMyWorkSource
{
    public string SystemCode => BenchPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.EmployeeIdOf() is not { } me)
        {
            return [];
        }

        var studies = await (await reader.StudiesAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(s => s.Status == StudyStatuses.Active && s.PendingProcess == null
                && ((s.ResponsibleEmployeeId == me && (s.Stage == StudyStages.Acquire || (s.Stage == StudyStages.Search && s.SearchNotifiedAt != null)
                        || (s.Stage == StudyStages.Analysis && s.AnalysisApprovedAt == null) || s.ReadyNotifiedAt != null))
                    || (s.RequesterEmployeeId == me && s.Stage == StudyStages.Plan && s.Returned)))
            .OrderBy(s => s.TargetYear).ThenBy(s => s.TargetQuarter).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        return studies.Select(s => new MyWorkItem(BenchPermissions.SystemCode, Study.EntityType, s.Id, s.Number, s.TitleAr, s.TitleEn, s.Stage switch
        {
            StudyStages.Plan => "bench.myWork.resubmit",
            StudyStages.Search => "bench.myWork.search",
            StudyStages.Acquire => "bench.myWork.visit",
            _ => s.AnalysisApprovedAt is null ? "bench.myWork.analysis" : "bench.myWork.closure",
        }, s.Stage == StudyStages.Plan ? $"/requests/{s.RequestId:D}" : BenchNotifier.Link(s), null, Quarters.Start(s.TargetYear, Math.Clamp(s.TargetQuarter, 1, 4)))).ToList();
    }
}
