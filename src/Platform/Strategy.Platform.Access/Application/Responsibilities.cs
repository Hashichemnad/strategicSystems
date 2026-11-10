using System.Globalization;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Notifications;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Access.Application;

public sealed record ResponsibilityNameDto(Guid Id, string? NameAr, string? NameEn);

public sealed record ResponsibilityKindDto(
    string Code, string LabelAr, string LabelEn, string Target, bool RestrictedCandidates, string? NoteKey, IReadOnlyList<ResponsibilityStatusOption> Statuses);

public sealed record ResponsibilityKindsDto(string SystemCode, string? NameAr, string? NameEn, IReadOnlyList<ResponsibilityKindDto> Kinds);

public sealed record ResponsibilityRowDto(
    Guid Id, uint Version, string? Number, string? TitleAr, string? TitleEn, string? Status, ResponsibilityNameDto? Responsible, ResponsibilityNameDto? Unit,
    string? Link, string? ContextAr, string? ContextEn);

public sealed record ResponsibilityListDto(IReadOnlyList<ResponsibilityRowDto> Items, int Total, int Page, int PageSize);

public sealed record ResponsibilityItemInput(Guid Id, uint Version);

public sealed record ResponsibilityChangeInput(IReadOnlyList<ResponsibilityItemInput>? Items, Guid? NewId, string? Reason);

public sealed record ResponsibilityOutcomeDto(
    Guid Id, string Outcome, string? MessageKey, string? Number, string? TitleAr, string? TitleEn, ResponsibilityNameDto? Previous, string? Reference);

public sealed record ResponsibilityResultDto(int Moved, int ChangeRequests, int Skipped, ResponsibilityNameDto New, IReadOnlyList<ResponsibilityOutcomeDto> Items);

/// <summary>
/// The Responsibilities page of a system (SPEC-ADDENDUM §D2, ADR 0168): checks <c>&lt;system&gt;.responsibilities.manage</c> of THAT system and
/// delegates to the module's <see cref="IResponsibilitySource"/>. Vendors are refused. The reason is the ambient audit reason of every change.
/// </summary>
internal sealed class ResponsibilityService(
    IEnumerable<IResponsibilitySource> sources,
    IEnumerable<IAccessCatalogueSource> catalogues,
    IAccessService access,
    IRequestContext context,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    IAuditReason auditReason,
    IAuditLog audit)
{
    public const int MaxItems = 200;
    public const int ReasonMaxLength = 1000;

    public async Task<Result<ResponsibilityKindsDto>> KindsAsync(string systemCode, CancellationToken ct)
    {
        var allowed = await AuthoriseAsync(systemCode, ct).ConfigureAwait(false);
        if (allowed is not null)
        {
            return allowed;
        }

        var system = catalogues.FirstOrDefault(c => c.System.Code == systemCode)?.System;
        var kinds = SourcesOf(systemCode).Select(s => new ResponsibilityKindDto(s.Kind.Code, s.Kind.LabelAr, s.Kind.LabelEn, s.Kind.Target, s.Kind.RestrictedCandidates,
            s.Kind.NoteKey, s.Kind.Statuses ?? [])).ToList();
        return new ResponsibilityKindsDto(systemCode, system?.NameAr, system?.NameEn, kinds);
    }

    public async Task<Result<ResponsibilityListDto>> ListAsync(string systemCode, string kind, ResponsibilityQuery query, CancellationToken ct)
    {
        var (source, error) = await SourceAsync(systemCode, kind, ct).ConfigureAwait(false);
        if (source is null)
        {
            return error!;
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxItems);
        var result = await source.ListAsync(query with { Page = page, PageSize = size, Text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim() },
            context.User, ct).ConfigureAwait(false);
        var unitKind = source.Kind.Target == ResponsibilityTargets.Unit;
        var people = unitKind ? new Dictionary<Guid, EmployeeReference>() : await PeopleAsync(result.Items.Select(r => r.PersonId), ct).ConfigureAwait(false);
        var units = await UnitsAsync(result.Items.Select(r => r.UnitId).Concat(unitKind ? result.Items.Select(r => r.PersonId) : []), ct).ConfigureAwait(false);
        var items = result.Items.Select(r => new ResponsibilityRowDto(r.Id, r.Version, r.Number, r.TitleAr, r.TitleEn, r.Status,
            unitKind ? Name(r.PersonId, units) : Name(r.PersonId, people), Name(r.UnitId, units), r.Link, r.ContextAr, r.ContextEn)).ToList();
        return new ResponsibilityListDto(items, result.Total, page, size);
    }

    /// <summary>The people who may take a restricted responsibility over (e.g. Audit Office members), filtered by name; empty when unrestricted.</summary>
    public async Task<Result<IReadOnlyList<ResponsibilityNameDto>>> CandidatesAsync(string systemCode, string kind, string? search, CancellationToken ct)
    {
        var (source, error) = await SourceAsync(systemCode, kind, ct).ConfigureAwait(false);
        if (source is null)
        {
            return error!;
        }

        if (!source.Kind.RestrictedCandidates || await source.EligibleAsync(ct).ConfigureAwait(false) is not { } eligible)
        {
            return Result.Success<IReadOnlyList<ResponsibilityNameDto>>([]);
        }

        var people = await employees.GetManyAsync(eligible.ToList(), ct).ConfigureAwait(false);
        var text = search?.Trim();
        return Result.Success<IReadOnlyList<ResponsibilityNameDto>>(people.Values
            .Where(e => e.Status != "left")
            .Where(e => string.IsNullOrEmpty(text) || Contains(e.NameAr, text) || Contains(e.NameEn, text) || Contains(e.Email, text) || Contains(e.EmployeeNo, text))
            .OrderBy(e => e.NameEn ?? e.NameAr, StringComparer.OrdinalIgnoreCase).Take(50)
            .Select(e => new ResponsibilityNameDto(e.Id, e.NameAr, e.NameEn)).ToList());
    }

    public async Task<Result<IReadOnlyList<ResponsibilityOutcomeDto>>> PreviewAsync(string systemCode, string kind, ResponsibilityChangeInput input, CancellationToken ct)
    {
        var (source, error) = await SourceAsync(systemCode, kind, ct).ConfigureAwait(false);
        if (source is null)
        {
            return error!;
        }

        var (change, invalid) = await ValidateAsync(source, input, ct).ConfigureAwait(false);
        if (change is null)
        {
            return invalid!;
        }

        var outcomes = await source.PreviewAsync(change, context.User, ct).ConfigureAwait(false);
        return Result.Success(await ToDtosAsync(source, outcomes, ct).ConfigureAwait(false));
    }

    public async Task<Result<ResponsibilityResultDto>> ReassignAsync(string systemCode, string kind, ResponsibilityChangeInput input, CancellationToken ct)
    {
        var (source, error) = await SourceAsync(systemCode, kind, ct).ConfigureAwait(false);
        if (source is null)
        {
            return error!;
        }

        var (change, invalid) = await ValidateAsync(source, input, ct).ConfigureAwait(false);
        if (change is null)
        {
            return invalid!;
        }

        // Every field change the module saves carries the reason (audit trail, CLAUDE.md §8).
        auditReason.Reason = change.Reason;
        IReadOnlyList<ResponsibilityOutcome> outcomes;
        try
        {
            outcomes = await source.ReassignAsync(change, context.User, ct).ConfigureAwait(false);
        }
        finally
        {
            auditReason.Reason = null;
        }

        var items = await ToDtosAsync(source, outcomes, ct).ConfigureAwait(false);
        var moved = outcomes.Count(o => o.Outcome == ResponsibilityOutcomes.Moved);
        var requested = outcomes.Count(o => o.Outcome == ResponsibilityOutcomes.ChangeRequest);
        await audit.WriteAsync(new AuditEvent("access.responsibilities.reassign", moved + requested > 0, systemCode, null, null, new
        {
            kind = source.Kind.Code,
            newId = change.NewId,
            moved = outcomes.Where(o => o.Outcome == ResponsibilityOutcomes.Moved).Select(o => new { id = o.Id, previous = o.PreviousId }),
            changeRequests = outcomes.Where(o => o.Outcome == ResponsibilityOutcomes.ChangeRequest).Select(o => new { id = o.Id, reference = o.Reference }),
            skipped = outcomes.Where(o => o.Outcome == ResponsibilityOutcomes.Skipped).Select(o => new { id = o.Id, o.MessageKey }),
        }, change.Reason), ct).ConfigureAwait(false);
        var target = await NameOfTargetAsync(source, change.NewId, ct).ConfigureAwait(false);
        return new ResponsibilityResultDto(moved, requested, outcomes.Count - moved - requested, target, items);
    }

    private IEnumerable<IResponsibilitySource> SourcesOf(string systemCode) => sources.Where(s => s.SystemCode == systemCode);

    /// <summary>Internal principals holding the system's permission only (403); a system without sources is unknown (404).</summary>
    private async Task<Error?> AuthoriseAsync(string systemCode, CancellationToken ct)
    {
        if (!SourcesOf(systemCode).Any())
        {
            return Error.NotFound();
        }

        if (!context.User.IsInternal() || !await access.CanAsync(context.User, ResponsibilityPermissions.Manage(systemCode), cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return null;
    }

    private async Task<(IResponsibilitySource? Source, Error? Error)> SourceAsync(string systemCode, string kind, CancellationToken ct)
    {
        if (await AuthoriseAsync(systemCode, ct).ConfigureAwait(false) is { } error)
        {
            return (null, error);
        }

        var source = SourcesOf(systemCode).FirstOrDefault(s => s.Kind.Code == kind);
        return source is null ? (null, Error.NotFound()) : (source, null);
    }

    private async Task<(ResponsibilityChange? Change, Error? Error)> ValidateAsync(IResponsibilitySource source, ResponsibilityChangeInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var items = input.Items ?? [];
        if (items.Count == 0)
        {
            errors.Add(new FieldError("items", "validation.notEmpty", "validation.notEmpty"));
        }
        else if (items.Count > MaxItems)
        {
            errors.Add(new FieldError("items", "validation.maximumItems", "validation.maximumItems", new Dictionary<string, object?> { ["max"] = MaxItems }));
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            errors.Add(new FieldError("reason", "validation.notEmpty", "validation.notEmpty"));
        }
        else if (input.Reason.Trim().Length > ReasonMaxLength)
        {
            errors.Add(new FieldError("reason", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = ReasonMaxLength }));
        }

        if (input.NewId is not { } newId)
        {
            errors.Add(new FieldError("newId", "validation.notEmpty", "validation.notEmpty"));
        }
        else if (source.Kind.Target == ResponsibilityTargets.Unit
            ? await org.GetUnitAsync(newId, ct).ConfigureAwait(false) is not { Active: true }
            : await employees.GetAsync(newId, ct).ConfigureAwait(false) is not { Status: not "left" })
        {
            errors.Add(new FieldError("newId", "validation.unknownReference", "validation.unknownReference"));
        }
        else if (source.Kind.RestrictedCandidates && await source.EligibleAsync(ct).ConfigureAwait(false) is { } eligible && !eligible.Contains(newId))
        {
            // Server-side eligibility (e.g. Audit Office members only): a validation error before anything is checked or moved.
            errors.Add(new FieldError("newId", ResponsibilityMessages.NotEligible, ResponsibilityMessages.NotEligible));
        }

        return errors.Count > 0
            ? (null, Error.Validation(errors))
            : (new ResponsibilityChange(items.Select(i => new ResponsibilityItem(i.Id, i.Version)).ToList(), input.NewId!.Value, input.Reason!.Trim()), null);
    }

    private async Task<IReadOnlyList<ResponsibilityOutcomeDto>> ToDtosAsync(IResponsibilitySource source, IReadOnlyList<ResponsibilityOutcome> outcomes, CancellationToken ct)
    {
        var unitKind = source.Kind.Target == ResponsibilityTargets.Unit;
        var people = unitKind ? new Dictionary<Guid, EmployeeReference>() : await PeopleAsync(outcomes.Select(o => o.PreviousId), ct).ConfigureAwait(false);
        var units = unitKind ? await UnitsAsync(outcomes.Select(o => o.PreviousId), ct).ConfigureAwait(false) : [];
        return outcomes.Select(o => new ResponsibilityOutcomeDto(o.Id, o.Outcome, o.MessageKey, o.Number, o.TitleAr, o.TitleEn,
            unitKind ? Name(o.PreviousId, units) : Name(o.PreviousId, people), o.Reference)).ToList();
    }

    private async Task<ResponsibilityNameDto> NameOfTargetAsync(IResponsibilitySource source, Guid id, CancellationToken ct)
    {
        if (source.Kind.Target == ResponsibilityTargets.Unit)
        {
            var unit = await org.GetUnitAsync(id, ct).ConfigureAwait(false);
            return new ResponsibilityNameDto(id, unit?.NameAr, unit?.NameEn);
        }

        var employee = await employees.GetAsync(id, ct).ConfigureAwait(false);
        return new ResponsibilityNameDto(id, employee?.NameAr, employee?.NameEn);
    }

    private async Task<IReadOnlyDictionary<Guid, EmployeeReference>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return list.Count == 0 ? new Dictionary<Guid, EmployeeReference>() : await employees.GetManyAsync(list, ct).ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, UnitReference>> UnitsAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, UnitReference>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } unit)
            {
                result[id] = unit;
            }
        }

        return result;
    }

    private static ResponsibilityNameDto? Name(Guid? id, IReadOnlyDictionary<Guid, EmployeeReference> people) =>
        id is { } i ? new ResponsibilityNameDto(i, people.GetValueOrDefault(i)?.NameAr, people.GetValueOrDefault(i)?.NameEn) : null;

    private static ResponsibilityNameDto? Name(Guid? id, IReadOnlyDictionary<Guid, UnitReference> units) =>
        id is { } i ? new ResponsibilityNameDto(i, units.GetValueOrDefault(i)?.NameAr, units.GetValueOrDefault(i)?.NameEn) : null;

    private static bool Contains(string? value, string text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// The notices of a reassignment (ADR 0168): one to each previous responsible party and one to the new one, each in the recipient's language
/// (bilingual values, the template chooses), queued in the module's transaction. A unit is notified through its head.
/// </summary>
internal sealed class ResponsibilityNotifier(
    INotificationService notifications, IUserDirectory users, IEmployeeDirectory employees, IOrgDirectory org, IEnumerable<IAccessCatalogueSource> catalogues,
    IRequestContext context) : IResponsibilityNotifier
{
    public static readonly NotificationTemplateDefinition Assigned = new("access.responsibilityAssigned", "access",
        "إسناد مسؤولية", "Responsibility assigned",
        "أُسندت إليك مسؤولية: {{kindAr}} ({{count}})", "You are now responsible: {{kindEn}} ({{count}})",
        "أصبحت مسؤولاً في {{systemAr}} عن «{{kindAr}}» للسجلات التالية: {{recordsAr}}.\nالسابق: {{previousAr}}.\nالسبب: {{reason}}\nبواسطة: {{actor}}\n{{button}}",
        "You are now responsible in {{systemEn}} for “{{kindEn}}” of: {{recordsEn}}.\nPreviously: {{previousEn}}.\nReason: {{reason}}\nBy: {{actor}}\n{{button}}",
        "فتح", "Open", ["kindAr", "kindEn", "systemAr", "systemEn", "count", "recordsAr", "recordsEn", "previousAr", "previousEn", "reason", "actor"]);

    public static readonly NotificationTemplateDefinition Removed = new("access.responsibilityRemoved", "access",
        "نقل مسؤولية", "Responsibility moved",
        "نُقلت مسؤوليتك: {{kindAr}} ({{count}})", "Your responsibility moved: {{kindEn}} ({{count}})",
        "نُقلت مسؤوليتك في {{systemAr}} عن «{{kindAr}}» للسجلات التالية إلى {{newAr}}: {{recordsAr}}.\nالسبب: {{reason}}\nبواسطة: {{actor}}",
        "Your responsibility in {{systemEn}} for “{{kindEn}}” of the following records moved to {{newEn}}: {{recordsEn}}.\nReason: {{reason}}\nBy: {{actor}}",
        Placeholders: ["kindAr", "kindEn", "systemAr", "systemEn", "count", "recordsAr", "recordsEn", "newAr", "newEn", "reason", "actor"]);

    private const int MaxListed = 20;

    public async Task NotifyAsync(ResponsibilityNotice notice, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var unitKind = notice.Kind.Target == ResponsibilityTargets.Unit;
        var system = catalogues.FirstOrDefault(c => c.System.Code == notice.SystemCode)?.System;
        var names = await NamesAsync(notice.Items.Select(i => i.PreviousId).Append(notice.NewId), unitKind, cancellationToken).ConfigureAwait(false);
        var actor = context.User.EmployeeIdOf() is { } me && await employees.GetAsync(me, cancellationToken).ConfigureAwait(false) is { } e
            ? e.NameEn ?? e.NameAr ?? string.Empty
            : context.User.Identity?.Name ?? string.Empty;

        Dictionary<string, string> Values(IReadOnlyList<ResponsibilityNoticeItem> items) => new()
        {
            ["kindAr"] = notice.Kind.LabelAr,
            ["kindEn"] = notice.Kind.LabelEn,
            ["systemAr"] = system?.NameAr ?? notice.SystemCode,
            ["systemEn"] = system?.NameEn ?? notice.SystemCode,
            ["count"] = items.Count.ToString(CultureInfo.InvariantCulture),
            ["recordsAr"] = List(items, true),
            ["recordsEn"] = List(items, false),
            ["previousAr"] = string.Join("، ", items.Select(i => Name(i.PreviousId, names, true)).Distinct().Where(n => n.Length > 0)),
            ["previousEn"] = string.Join(", ", items.Select(i => Name(i.PreviousId, names, false)).Distinct().Where(n => n.Length > 0)),
            ["newAr"] = Name(notice.NewId, names, true),
            ["newEn"] = Name(notice.NewId, names, false),
            ["reason"] = notice.Reason,
            ["actor"] = actor,
        };

        await SendAsync(Assigned, notice.NewId, unitKind, Values(notice.Items), notice.Link, transactionContext, cancellationToken).ConfigureAwait(false);
        foreach (var group in notice.Items.Where(i => i.PreviousId is not null && i.PreviousId != notice.NewId).GroupBy(i => i.PreviousId!.Value))
        {
            await SendAsync(Removed, group.Key, unitKind, Values(group.ToList()), notice.Link, transactionContext, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(
        NotificationTemplateDefinition template, Guid id, bool unit, Dictionary<string, string> values, string? link, object transactionContext, CancellationToken ct)
    {
        var employee = unit ? await org.GetHeadOfUnitAsync(id, cancellationToken: ct).ConfigureAwait(false) : id;
        if (employee is null)
        {
            return;
        }

        var recipients = (await users.GetByEmployeesAsync([employee.Value], ct).ConfigureAwait(false)).Where(u => u.Active).Select(u => NotificationRecipient.User(u.Id))
            .ToList();
        if (recipients.Count > 0)
        {
            await notifications.SendAsync(new NotificationRequest(template.Key, recipients, values, link), transactionContext, ct).ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<Guid, (string? Ar, string? En)>> NamesAsync(IEnumerable<Guid?> ids, bool unit, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        var result = new Dictionary<Guid, (string? Ar, string? En)>();
        if (unit)
        {
            foreach (var id in list)
            {
                if (await org.GetUnitAsync(id, ct).ConfigureAwait(false) is { } u)
                {
                    result[id] = (u.NameAr, u.NameEn);
                }
            }

            return result;
        }

        foreach (var (id, e) in await employees.GetManyAsync(list, ct).ConfigureAwait(false))
        {
            result[id] = (e.NameAr, e.NameEn);
        }

        return result;
    }

    private static string Name(Guid? id, Dictionary<Guid, (string? Ar, string? En)> names, bool ar) =>
        id is { } i && names.TryGetValue(i, out var n) ? (ar ? n.Ar ?? n.En : n.En ?? n.Ar) ?? string.Empty : string.Empty;

    private static string List(IReadOnlyList<ResponsibilityNoticeItem> items, bool ar)
    {
        var shown = items.Take(MaxListed).Select(i =>
        {
            var title = ar ? i.TitleAr ?? i.TitleEn : i.TitleEn ?? i.TitleAr;
            return string.Join(" — ", new[] { i.Number, title }.Where(s => !string.IsNullOrWhiteSpace(s)));
        });
        var text = string.Join(ar ? "، " : ", ", shown);
        return items.Count > MaxListed ? text + (ar ? $" (+{items.Count - MaxListed})" : $" (+{items.Count - MaxListed} more)") : text;
    }
}
