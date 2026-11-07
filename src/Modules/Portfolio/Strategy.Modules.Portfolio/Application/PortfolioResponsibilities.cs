using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>
/// Portfolio → Settings → Responsibilities (ADR 0168): project manager / project owner. SPEC §1.9: the PM is "changeable only by approved
/// Ownership change request" (the owner by PM Office / change request), so nothing moves directly: per project an Ownership change request is
/// created and submitted to its approval chain, with the reason; the PM applies it after approval as for any change request (ADR 0073).
/// </summary>
internal abstract class ProjectPersonResponsibility(PortfolioDbContext db, IAccessService access, ChangeRequestService changes, IResponsibilityNotifier notifier)
    : ResponsibilitySource<Project>(notifier)
{
    public override string SystemCode => PortfolioPermissions.SystemCode;

    protected static readonly IReadOnlyList<ResponsibilityStatusOption> ProjectStatusOptions =
    [
        new(ProjectStatuses.Active, "نشط", "Active"), new(ProjectStatuses.OnHold, "معلّق", "On hold"), new(ProjectStatuses.Completed, "مكتمل", "Completed"),
    ];

    protected abstract Guid? PersonOf(Project project);

    protected abstract bool ManagerKind { get; }

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var projects = (await ScopedAsync(user, false, cancellationToken).ConfigureAwait(false)).Where(p => p.Status != ProjectStatuses.Cancelled);
        projects = ManagerKind ? projects.Where(p => p.PmEmployeeId != null) : projects;
        if (query.PersonId is { } person)
        {
            projects = ManagerKind ? projects.Where(p => p.PmEmployeeId == person) : projects.Where(p => p.OwnerEmployeeId == person);
        }

        if (query.UnitId is { } unit)
        {
            projects = projects.Where(p => p.UnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            projects = projects.Where(p => p.Status == query.Status);
        }

        if (query.Text is { Length: > 0 } text)
        {
            var pattern = PortfolioLike.Pattern(text);
            projects = projects.Where(p => EF.Functions.ILike(p.Number + " " + (p.NameAr ?? "") + " " + (p.NameEn ?? ""), pattern, "\\"));
        }

        var total = await projects.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await projects.OrderBy(p => p.Number).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(rows.Select(Describe).ToList(), total);
    }

    protected override async Task<IReadOnlyList<Project>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken) =>
        await (await ScopedAsync(user, tracked, cancellationToken).ConfigureAwait(false)).Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    protected override ResponsibilityRow Describe(Project record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Number, record.NameAr, record.NameEn, record.Status, PersonOf(record), record.UnitId,
            $"/portfolio/projects/{record.Id:D}");
    }

    /// <summary>Live projects only (an approval running or a closed project cannot take a change request now).</summary>
    protected override Task<ResponsibilityOutcome> CheckAsync(Project record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Status != ProjectStatuses.Active || record.SubState == SubStates.Done || (ManagerKind && record.PmEmployeeId is null))
        {
            return Task.FromResult(Blocked(row, ResponsibilityMessages.NotEditable));
        }

        return Task.FromResult(record.ApprovalProcess is not null && record.ApprovalResult is null
            ? Blocked(row, "responsibilities.outcome.approvalRunning")
            : Outcome(row, ResponsibilityOutcomes.ChangeRequest, ResponsibilityMessages.NeedsChangeRequest));
    }

    /// <summary>One Ownership change request per project, created and submitted with the module's own commands (permission and stage checks included).</summary>
    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<Project> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var outcomes = new List<ResponsibilityOutcome>();
        foreach (var project in batch.Records)
        {
            var check = batch.OutcomeOf(project);
            var values = new ChangeRequestValues(ChangeTypes.Ownership, ["ownership"], batch.Reason, null, null, null, null, null, null, null, null, false,
                ManagerKind ? batch.NewId : null, ManagerKind ? null : batch.NewId, null, null);
            var created = await changes.CreateAsync(project.Id, new ChangeRequestInput(values), cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                outcomes.Add(Skipped(check, created.Error));
                continue;
            }

            var submitted = await changes.SubmitAsync(created.Value.Id, created.Value.Version, batch.Reason, cancellationToken).ConfigureAwait(false);
            if (submitted.IsFailure)
            {
                // The draft stays with the project for the PM Office to submit or cancel; nothing moved.
                outcomes.Add(Skipped(check, submitted.Error) with { Reference = created.Value.Number });
                db.ChangeTracker.Clear();
                continue;
            }

            outcomes.Add(check with { Outcome = ResponsibilityOutcomes.ChangeRequest, MessageKey = ResponsibilityMessages.ChangeRequestSubmitted, Reference = submitted.Value.Number });
        }

        return outcomes;
    }

    private static ResponsibilityOutcome Skipped(ResponsibilityOutcome check, Error error) =>
        check with { Outcome = ResponsibilityOutcomes.Skipped, MessageKey = error.Kind == ErrorKind.Forbidden ? ResponsibilityMessages.Forbidden : error.MessageKey };

    private async Task<IQueryable<Project>> ScopedAsync(ClaimsPrincipal user, bool tracked, CancellationToken ct) =>
        await access.ApplyScopeAsync(tracked ? db.Projects : db.Projects.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), ProjectScope.Map, ct)
            .ConfigureAwait(false);
}

internal sealed class ProjectManagerResponsibility(PortfolioDbContext db, IAccessService access, ChangeRequestService changes, IResponsibilityNotifier notifier)
    : ProjectPersonResponsibility(db, access, changes, notifier)
{
    public override ResponsibilityKindDefinition Kind { get; } = new("project-manager", "مدير المشروع", "Project manager",
        NoteKey: "responsibilities.notes.changeRequest", Statuses: ProjectStatusOptions);

    protected override bool ManagerKind => true;

    protected override Guid? PersonOf(Project project) => project.PmEmployeeId;
}

internal sealed class ProjectOwnerResponsibility(PortfolioDbContext db, IAccessService access, ChangeRequestService changes, IResponsibilityNotifier notifier)
    : ProjectPersonResponsibility(db, access, changes, notifier)
{
    public override ResponsibilityKindDefinition Kind { get; } = new("project-owner", "مالك المشروع", "Project owner",
        NoteKey: "responsibilities.notes.changeRequest", Statuses: ProjectStatusOptions);

    protected override bool ManagerKind => false;

    protected override Guid? PersonOf(Project project) => project.OwnerEmployeeId;
}

/// <summary>User story assignees of Agile / Hybrid projects (open stories only): moved directly, they are work items, not a governed role.</summary>
internal sealed class StoryAssigneeResponsibility(PortfolioDbContext db, IAccessService access, IResponsibilityNotifier notifier) : ResponsibilitySource<UserStory>(notifier)
{
    public override string SystemCode => PortfolioPermissions.SystemCode;

    public override ResponsibilityKindDefinition Kind { get; } = new("story-assignee", "المسند إليه في قصة المستخدم", "User story assignee");

    public override async Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var projects = await ProjectsAsync(user, cancellationToken).ConfigureAwait(false);
        if (query.UnitId is { } unit)
        {
            projects = projects.Where(p => p.UnitId == unit);
        }

        var stories = db.Stories.AsNoTracking().Where(s => s.Status == ItemStatuses.Active && s.Category != BoardCategories.Done && projects.Any(p => p.Id == s.ProjectId));
        stories = query.PersonId is { } person ? stories.Where(s => s.AssigneeEmployeeId == person) : stories.Where(s => s.AssigneeEmployeeId != null);
        if (query.Text is { Length: > 0 } text)
        {
            var pattern = PortfolioLike.Pattern(text);
            stories = stories.Where(s => EF.Functions.ILike(s.Code + " " + (s.Title ?? ""), pattern, "\\"));
        }

        var total = await stories.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await stories.OrderBy(s => s.Code).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Join(db.Projects.AsNoTracking(), s => s.ProjectId, p => p.Id, (s, p) => new { s, p.Number, p.UnitId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ResponsibilityPage(page.Select(x => Describe(x.s) with { UnitId = x.UnitId, ContextAr = x.Number, ContextEn = x.Number }).ToList(), total);
    }

    protected override async Task<IReadOnlyList<UserStory>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken)
    {
        var projects = await ProjectsAsync(user, cancellationToken, tracked).ConfigureAwait(false);
        return await (tracked ? db.Stories : db.Stories.AsNoTracking()).Where(s => ids.Contains(s.Id) && projects.Any(p => p.Id == s.ProjectId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ResponsibilityRow Describe(UserStory record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(record.Id, record.Version, record.Code, record.Title, record.Title, record.Category, record.AssigneeEmployeeId, null,
            $"/portfolio/projects/{record.ProjectId:D}");
    }

    protected override Task<ResponsibilityOutcome> CheckAsync(UserStory record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(record.IsActive && !record.IsDone ? Allowed(row) : Blocked(row, ResponsibilityMessages.NotEditable));

    protected override async Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<UserStory> batch, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var story in batch.Records)
        {
            story.AssignTo(batch.NewId);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        await batch.NotifyAsync(db, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return batch.AllMoved();
    }

    /// <summary>Tracked with the outer query when loading for a change (AsNoTracking anywhere applies to the whole query).</summary>
    private async Task<IQueryable<Project>> ProjectsAsync(ClaimsPrincipal user, CancellationToken ct, bool tracked = false) =>
        await access.ApplyScopeAsync(tracked ? db.Projects : db.Projects.AsNoTracking(), user, ResponsibilityPermissions.Manage(SystemCode), ProjectScope.Map, ct).ConfigureAwait(false);
}

internal static class PortfolioLike
{
    public static string Pattern(string text) => "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
