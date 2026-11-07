using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Strategy.Platform.Access;

/// <summary>
/// System → Settings → Responsibilities (SPEC-ADDENDUM §D2, ADR 0168): who is responsible for updating records, reassigned singly or in bulk.
/// One source per (system, responsibility kind), implemented by the owning module; the platform service checks
/// <c>&lt;system&gt;.responsibilities.manage</c> for that system only and delegates. Everything runs as the acting user inside the module.
/// </summary>
public static class ResponsibilityPermissions
{
    /// <summary>The permission of one system: <c>&lt;system&gt;.responsibilities.manage</c>.</summary>
    public static string Manage(string systemCode) => $"{systemCode}.responsibilities.manage";

    /// <summary>The catalogue entry each module declares (AR / EN labels shown in the permission matrix).</summary>
    public static PermissionDefinition Definition(string systemCode) =>
        new(Manage(systemCode), "إدارة المسؤوليات وإعادة إسنادها", "Manage and reassign responsibilities");
}

/// <summary>What the new responsible party is: a person (employee) or an organisation unit.</summary>
public static class ResponsibilityTargets
{
    public const string Employee = "employee";
    public const string Unit = "unit";
}

/// <summary>Per-record outcome codes of a preview (allowed / change-request / blocked) and of a reassignment (moved / change-request / skipped).</summary>
public static class ResponsibilityOutcomes
{
    public const string Allowed = "allowed";
    public const string ChangeRequest = "change-request";
    public const string Blocked = "blocked";
    public const string Moved = "moved";
    public const string Skipped = "skipped";
}

/// <summary>Stable message keys of the platform checks (translated in <c>responsibilities.json</c>).</summary>
public static class ResponsibilityMessages
{
    public const string NotFound = "responsibilities.outcome.notFound";
    public const string ChangedMeanwhile = "responsibilities.outcome.changedMeanwhile";
    public const string AlreadyResponsible = "responsibilities.outcome.alreadyResponsible";
    public const string NotEligible = "responsibilities.outcome.notEligible";
    public const string NeedsChangeRequest = "responsibilities.outcome.needsChangeRequest";
    public const string ChangeRequestSubmitted = "responsibilities.outcome.changeRequestSubmitted";
    public const string NotEditable = "responsibilities.outcome.notEditable";
    public const string Forbidden = "responsibilities.outcome.forbidden";
}

public sealed record ResponsibilityStatusOption(string Code, string LabelAr, string LabelEn);

/// <summary>
/// One kind of responsibility of a system (e.g. KPI card responsible person).
/// <paramref name="RestrictedCandidates"/>: only the people <see cref="IResponsibilitySource.EligibleAsync"/> returns may take it over.
/// <paramref name="NoteKey"/>: a translatable remark shown with the kind (e.g. "goes through change requests").
/// </summary>
public sealed record ResponsibilityKindDefinition(
    string Code,
    string LabelAr,
    string LabelEn,
    string Target = ResponsibilityTargets.Employee,
    bool RestrictedCandidates = false,
    string? NoteKey = null,
    IReadOnlyList<ResponsibilityStatusOption>? Statuses = null);

/// <summary>List filters: current responsible person / unit, status code, free text; paging (page from 1, size ≤ 200).</summary>
public sealed record ResponsibilityQuery(Guid? PersonId, Guid? UnitId, string? Status, string? Text, int Page = 1, int PageSize = 50);

/// <summary>One record as the Responsibilities page lists it. <paramref name="PersonId"/>: the employee (or unit, for a unit kind) responsible now.</summary>
public sealed record ResponsibilityRow(
    Guid Id,
    uint Version,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string? Status,
    Guid? PersonId,
    Guid? UnitId,
    string? Link,
    string? ContextAr = null,
    string? ContextEn = null);

public sealed record ResponsibilityPage(IReadOnlyList<ResponsibilityRow> Items, int Total);

/// <summary>A selected record and the row version the user saw (a record changed meanwhile is skipped, never overwritten).</summary>
public sealed record ResponsibilityItem(Guid Id, uint Version);

/// <summary>The requested change: the selected records, the new person (employee id) or unit, and the reason (required).</summary>
public sealed record ResponsibilityChange(IReadOnlyList<ResponsibilityItem> Items, Guid NewId, string Reason);

/// <summary>
/// Per-record result. Records outside the user's scope come back as blocked / skipped <see cref="ResponsibilityMessages.NotFound"/> without
/// number or title (existence does not leak). <paramref name="Reference"/>: e.g. the number of the change request created.
/// </summary>
public sealed record ResponsibilityOutcome(
    Guid Id,
    string Outcome,
    string? MessageKey = null,
    string? Number = null,
    string? TitleAr = null,
    string? TitleEn = null,
    Guid? PreviousId = null,
    string? Reference = null);

/// <summary>Implemented by the owning module per responsibility kind; registered with <see cref="ResponsibilityServiceCollectionExtensions.AddResponsibilitySource{T}"/>.</summary>
public interface IResponsibilitySource
{
    string SystemCode { get; }

    ResponsibilityKindDefinition Kind { get; }

    /// <summary>Records the user may manage (scope of <c>&lt;system&gt;.responsibilities.manage</c>, applied in the database query).</summary>
    Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>The employees who may take this responsibility over, or null when anyone may (checked again on preview and reassignment).</summary>
    Task<IReadOnlyCollection<Guid>?> EligibleAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ResponsibilityOutcome>> PreviewAsync(ResponsibilityChange change, ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>Moves the allowed records with the module's own commands / domain methods, in one transaction.</summary>
    Task<IReadOnlyList<ResponsibilityOutcome>> ReassignAsync(ResponsibilityChange change, ClaimsPrincipal user, CancellationToken cancellationToken);
}

/// <summary>A record that moved, for the notices to the previous and the new responsible party.</summary>
public sealed record ResponsibilityNoticeItem(Guid? PreviousId, string? Number, string? TitleAr, string? TitleEn);

public sealed record ResponsibilityNotice(
    string SystemCode, ResponsibilityKindDefinition Kind, IReadOnlyList<ResponsibilityNoticeItem> Items, Guid NewId, string Reason, string? Link);

/// <summary>
/// One bilingual notice to each previous responsible party and one to the new one (templates <c>access.responsibilityAssigned</c> /
/// <c>access.responsibilityRemoved</c>, editable in Settings → Notifications). Units notify their head. Queued in the caller's transaction.
/// </summary>
public interface IResponsibilityNotifier
{
    Task NotifyAsync(ResponsibilityNotice notice, object transactionContext, CancellationToken cancellationToken);
}

/// <summary>The common checks of a responsibility source (scope, row version, same person, eligibility) around the module's own change.</summary>
/// <typeparam name="TRecord">The module entity.</typeparam>
public abstract class ResponsibilitySource<TRecord>(IResponsibilityNotifier notifier) : IResponsibilitySource
    where TRecord : class
{
    public abstract string SystemCode { get; }

    public abstract ResponsibilityKindDefinition Kind { get; }

    public abstract Task<ResponsibilityPage> ListAsync(ResponsibilityQuery query, ClaimsPrincipal user, CancellationToken cancellationToken);

    public virtual Task<IReadOnlyCollection<Guid>?> EligibleAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<Guid>?>(null);

    public async Task<IReadOnlyList<ResponsibilityOutcome>> PreviewAsync(ResponsibilityChange change, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var (outcomes, _) = await CheckAllAsync(change, user, tracked: false, cancellationToken).ConfigureAwait(false);
        return outcomes;
    }

    public async Task<IReadOnlyList<ResponsibilityOutcome>> ReassignAsync(ResponsibilityChange change, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var (checks, records) = await CheckAllAsync(change, user, tracked: true, cancellationToken).ConfigureAwait(false);
        var outcomes = checks.Select(o => o.Outcome == ResponsibilityOutcomes.Blocked ? o with { Outcome = ResponsibilityOutcomes.Skipped } : o).ToList();
        var batch = new ResponsibilityBatch<TRecord>(this, records, checks.Where(c => c.Outcome != ResponsibilityOutcomes.Blocked).ToList(), change, notifier);
        if (batch.Records.Count == 0)
        {
            return outcomes;
        }

        var applied = await ApplyAsync(batch, user, cancellationToken).ConfigureAwait(false);
        var byId = (applied ?? batch.Outcomes.Select(o => o with
        {
            Outcome = ResponsibilityOutcomes.Skipped,
            MessageKey = ResponsibilityMessages.ChangedMeanwhile,
        }).ToList()).ToDictionary(o => o.Id);
        return outcomes.Select(o => byId.GetValueOrDefault(o.Id) ?? o).ToList();
    }

    /// <summary>The selected records within the user's scope (ids outside it are simply missing). Tracked for a reassignment.</summary>
    protected abstract Task<IReadOnlyList<TRecord>> LoadAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, bool tracked, CancellationToken cancellationToken);

    /// <summary>Id, row version, number, title and the responsible person / unit now.</summary>
    protected abstract ResponsibilityRow Describe(TRecord record);

    /// <summary>Module rules for one record: allowed (default), a change request, or blocked with a message key.</summary>
    protected virtual Task<ResponsibilityOutcome> CheckAsync(TRecord record, ResponsibilityRow row, Guid newId, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult(Allowed(row));

    /// <summary>
    /// Applies the change to <see cref="ResponsibilityBatch{TRecord}.Records"/> in one transaction, with the module's domain methods, record-role
    /// sync, re-routing and <see cref="ResponsibilityBatch{TRecord}.NotifyAsync"/>. Returns the outcome per record, or null when a record changed
    /// between loading and saving (concurrency: nothing is saved, every record is reported as skipped).
    /// </summary>
    protected abstract Task<IReadOnlyList<ResponsibilityOutcome>?> ApplyAsync(ResponsibilityBatch<TRecord> batch, ClaimsPrincipal user, CancellationToken cancellationToken);

    protected static ResponsibilityOutcome Allowed(ResponsibilityRow row) =>
        Outcome(row, ResponsibilityOutcomes.Allowed, null);

    protected static ResponsibilityOutcome Blocked(ResponsibilityRow row, string messageKey) =>
        Outcome(row, ResponsibilityOutcomes.Blocked, messageKey);

    protected static ResponsibilityOutcome Outcome(ResponsibilityRow row, string outcome, string? messageKey, string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new(row.Id, outcome, messageKey, row.Number, row.TitleAr, row.TitleEn, row.PersonId, reference);
    }

    internal ResponsibilityRow DescribeRecord(TRecord record) => Describe(record);

    private async Task<(List<ResponsibilityOutcome> Outcomes, List<TRecord> Records)> CheckAllAsync(
        ResponsibilityChange change, ClaimsPrincipal user, bool tracked, CancellationToken ct)
    {
        var ids = change.Items.Select(i => i.Id).Distinct().ToList();
        var loaded = (await LoadAsync(ids, user, tracked, ct).ConfigureAwait(false)).ToDictionary(r => Describe(r).Id);
        var eligible = Kind.RestrictedCandidates ? await EligibleAsync(ct).ConfigureAwait(false) : null;
        var outcomes = new List<ResponsibilityOutcome>();
        var records = new List<TRecord>();
        foreach (var item in change.Items.DistinctBy(i => i.Id))
        {
            if (!loaded.TryGetValue(item.Id, out var record))
            {
                outcomes.Add(new ResponsibilityOutcome(item.Id, ResponsibilityOutcomes.Blocked, ResponsibilityMessages.NotFound));
                continue;
            }

            var row = Describe(record);
            ResponsibilityOutcome outcome;
            if (row.Version != item.Version)
            {
                outcome = Blocked(row, ResponsibilityMessages.ChangedMeanwhile);
            }
            else if (row.PersonId == change.NewId)
            {
                outcome = Blocked(row, ResponsibilityMessages.AlreadyResponsible);
            }
            else if (eligible is not null && !eligible.Contains(change.NewId))
            {
                outcome = Blocked(row, ResponsibilityMessages.NotEligible);
            }
            else
            {
                outcome = await CheckAsync(record, row, change.NewId, user, ct).ConfigureAwait(false);
            }

            outcomes.Add(outcome);
            if (outcome.Outcome != ResponsibilityOutcomes.Blocked)
            {
                records.Add(record);
            }
        }

        return (outcomes, records);
    }
}

/// <summary>The records a reassignment moves, as they were before the change (previous person, number, title), and the notices.</summary>
public sealed class ResponsibilityBatch<TRecord>
    where TRecord : class
{
    private readonly ResponsibilitySource<TRecord> _source;
    private readonly IResponsibilityNotifier _notifier;

    internal ResponsibilityBatch(
        ResponsibilitySource<TRecord> source, IReadOnlyList<TRecord> records, IReadOnlyList<ResponsibilityOutcome> outcomes, ResponsibilityChange change,
        IResponsibilityNotifier notifier)
    {
        _source = source;
        _notifier = notifier;
        Records = records;
        Outcomes = outcomes;
        Change = change;
    }

    public IReadOnlyList<TRecord> Records { get; }

    /// <summary>The checks of <see cref="Records"/> (same order): allowed or change-request, with the previous person.</summary>
    public IReadOnlyList<ResponsibilityOutcome> Outcomes { get; }

    public ResponsibilityChange Change { get; }

    public Guid NewId => Change.NewId;

    public string Reason => Change.Reason.Trim();

    public ResponsibilityOutcome OutcomeOf(TRecord record) => Outcomes[IndexOf(record)];

    /// <summary>Every record of the batch as moved (the common case).</summary>
    public IReadOnlyList<ResponsibilityOutcome> AllMoved() =>
        Outcomes.Select(o => o with { Outcome = ResponsibilityOutcomes.Moved, MessageKey = null }).ToList();

    /// <summary>Notifies each previous person / unit and the new one about the moved records (all of the batch unless <paramref name="moved"/> narrows it).</summary>
    public Task NotifyAsync(object transactionContext, CancellationToken cancellationToken, IReadOnlyCollection<TRecord>? moved = null, string? link = null)
    {
        var items = (moved ?? Records).Select(r => _source.DescribeRecord(r)).ToList();
        var previous = Outcomes.ToDictionary(o => o.Id);
        var notice = new ResponsibilityNotice(_source.SystemCode, _source.Kind,
            items.Select(r => new ResponsibilityNoticeItem(previous.GetValueOrDefault(r.Id)?.PreviousId, r.Number, r.TitleAr, r.TitleEn)).ToList(), NewId, Reason,
            link ?? (items.Count == 1 ? items[0].Link : null));
        return items.Count == 0 ? Task.CompletedTask : _notifier.NotifyAsync(notice, transactionContext, cancellationToken);
    }

    private int IndexOf(TRecord record)
    {
        for (var i = 0; i < Records.Count; i++)
        {
            if (ReferenceEquals(Records[i], record))
            {
                return i;
            }
        }

        throw new ArgumentException("The record is not part of the batch.", nameof(record));
    }
}

public static class ResponsibilityServiceCollectionExtensions
{
    /// <summary>Registers a module's source (scoped: it uses the module's DbContext).</summary>
    public static IServiceCollection AddResponsibilitySource<TSource>(this IServiceCollection services)
        where TSource : class, IResponsibilitySource =>
        services.AddScoped<IResponsibilitySource, TSource>();
}
