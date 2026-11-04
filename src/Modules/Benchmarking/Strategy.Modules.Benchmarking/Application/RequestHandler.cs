using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Benchmarking.Application;

/// <summary>Record roles of a study (ADR 0126): coordinator = responsible employee, topic owners, visit participants.</summary>
internal sealed class BenchRoles(BenchDbContext db, IRecordRoleService recordRoles)
{
    public async Task SyncAsync(Study study, CancellationToken ct)
    {
        var topicOwners = await db.Topics.AsNoTracking().Where(t => t.StudyId == study.Id && !t.Removed && t.OwnerEmployeeId != null).Select(t => t.OwnerEmployeeId!.Value)
            .Distinct().ToListAsync(ct).ConfigureAwait(false);
        var participants = (await db.Visits.AsNoTracking().Where(v => v.StudyId == study.Id && !v.Removed).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(v => v.ParticipantIds).Distinct().ToList();
        var holders = await recordRoles.GetHoldersAsync(Study.EntityType, study.Id, ct).ConfigureAwait(false);
        await SyncRoleAsync(study.Id, BenchPermissions.CoordinatorRole, study.ResponsibleEmployeeId is { } c ? [c] : [], holders, ct).ConfigureAwait(false);
        await SyncRoleAsync(study.Id, BenchPermissions.TopicOwnerRole, topicOwners, holders, ct).ConfigureAwait(false);
        await SyncRoleAsync(study.Id, BenchPermissions.ParticipantRole, participants, holders, ct).ConfigureAwait(false);
    }

    private async Task SyncRoleAsync(Guid studyId, string role, IReadOnlyList<Guid> wanted, IReadOnlyList<RecordRoleHolder> holders, CancellationToken ct)
    {
        var current = holders.Where(h => h.RoleCode == role && h.PrincipalKind == RecordPrincipalKinds.Employee).Select(h => h.PrincipalId).ToHashSet();
        foreach (var employee in wanted.Where(e => !current.Contains(e)))
        {
            await recordRoles.GrantInTransactionAsync(db, Study.EntityType, studyId, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }

        foreach (var employee in current.Where(e => !wanted.Contains(e)))
        {
            await recordRoles.RevokeInTransactionAsync(db, Study.EntityType, studyId, RecordPrincipalKinds.Employee, employee, role, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The Plan stage on the Request Framework (ADR 0126, inside the framework's transaction): the study is created on submission (the request's
/// subject), moves to Search on approval, shows "waiting user to submit" when returned and is cancelled on rejection / cancellation.
/// </summary>
internal sealed class BenchRequestHandler(
    BenchDbContext db, INumberingService numbering, BenchRoles roles, BenchNotifier notifier, BenchCalendar calendar, IClock clock) : IRequestTypeHandler
{
    public string TypeCode => BenchRequestTypes.Request;

    public async Task<IReadOnlyList<FieldError>> ValidateAsync(RequestHandlerContext context, bool submitting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var errors = new List<FieldError>();
        var data = context.Request.Data;
        if (submitting && data.Number("targetYear") is { } year && data.Number("targetQuarter") is { } quarter)
        {
            var today = await calendar.TodayAsync(cancellationToken).ConfigureAwait(false);
            if (Quarters.End((int)year, (int)quarter) < today)
            {
                errors.Add(new FieldError("fields.targetQuarter", "bench.errors.quarterInPast", "bench.errors.quarterInPast"));
            }
        }

        var rows = data.Rows("topics");
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Text("topic") is null)
            {
                errors.Add(new FieldError($"fields.topics[{i}].topic", "validation.required", "validation.required"));
            }
        }

        return errors;
    }

    public Task<IReadOnlyCollection<string>?> AvailableActionsAsync(RequestHandlerContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>?>(null);

    public async Task<Result<RequestTransitionOutcome>> OnTransitionAsync(RequestTransition transition, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await db.JoinTransactionAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        var request = transition.Request;
        var study = await db.Studies.FirstOrDefaultAsync(s => s.RequestId == request.Id, cancellationToken).ConfigureAwait(false);
        switch (transition.Kind)
        {
            case TransitionKinds.Submitted:
                {
                    if (study is null)
                    {
                        var number = await numbering.NextAsync(db, BenchPermissions.StudyNumbering, new NumberingContext(clock.Today), cancellationToken).ConfigureAwait(false);
                        study = Study.Create(number, request.Id, request.Number);
                        db.Studies.Add(study);
                    }

                    study.Describe(Plan(request));
                    study.SetPlanReturned(false, null);
                    study.SetPending(BenchProcesses.Plan);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await SyncTopicsAsync(study, request.Data, cancellationToken).ConfigureAwait(false);
                    await roles.SyncAsync(study, cancellationToken).ConfigureAwait(false);
                    return new RequestTransitionOutcome(Study.EntityType, study.Id, TitleAr: study.TitleAr, TitleEn: study.TitleEn);
                }

            case TransitionKinds.Approved when study is not null:
                {
                    study.SetPending(null);
                    var applied = study.Apply(StudyActions.PlanApproved, clock.UtcNow);
                    if (applied.IsFailure)
                    {
                        return applied.Error;
                    }

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await notifier.SendAsync(BenchNotifications.PlanApproved, [study.ResponsibleEmployeeId, study.RequesterEmployeeId],
                        BenchNotifier.Values(study, text: $"Q{study.TargetQuarter} {study.TargetYear}"), BenchNotifier.Link(study), db, cancellationToken).ConfigureAwait(false);
                    return new RequestTransitionOutcome(Outcome: "approved");
                }

            case TransitionKinds.Returned when study is not null:
                study.SetPending(null);
                study.SetPlanReturned(true, transition.Comment);
                break;
            case TransitionKinds.Rejected when study is not null:
            case TransitionKinds.Cancelled when study is not null:
                study.Cancel(transition.Comment ?? transition.Kind);
                break;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RequestTransitionOutcome.None;
    }

    private static StudyPlan Plan(RequestSnapshot request)
    {
        var d = request.Data;
        return new StudyPlan(d.Text("titleAr"), d.Text("titleEn"), d.Id("responsibleUnit") ?? request.UnitId ?? Guid.Empty, d.Id("section"), d.Id("responsibleEmployee"),
            request.RequesterEmployeeId, (int)(d.Number("targetYear") ?? 0), (int)(d.Number("targetQuarter") ?? 1), d.Text("comparisonType"), d.Text("purpose"), d.Text("scope"));
    }

    /// <summary>Topics follow the request rows (matched by position; titles and owners refreshed on resubmission).</summary>
    private async Task SyncTopicsAsync(Study study, FormValues data, CancellationToken ct)
    {
        var rows = data.Rows("topics");
        var topics = await db.Topics.Where(t => t.StudyId == study.Id && !t.Removed).OrderBy(t => t.Sort).ToListAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < rows.Count; i++)
        {
            var title = rows[i].Text("topic") ?? $"#{i + 1}";
            if (i < topics.Count)
            {
                topics[i].Update(title, rows[i].Id("owner"), rows[i].Text("description"), i);
            }
            else
            {
                db.Topics.Add(Topic.Create(study.Id, title, rows[i].Id("owner"), rows[i].Text("description"), i));
            }
        }

        foreach (var extra in topics.Skip(rows.Count))
        {
            extra.Remove();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
