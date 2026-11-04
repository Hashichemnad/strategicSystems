using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Risk.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Cap.Application;

/// <summary>The source-system contract (ADR 0123) — used by Benchmarking now, Audit Management later.</summary>
internal sealed class CapRemarkSources(CapDbContext db, CapService service, CapDeadlines deadlines, ILookupService lookups, IFileService fileService) : ICapRemarkSources
{
    public async Task<CapSourceRef> EnsureSourceAsync(CapSourceRequest request, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var own = await BeginAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await db.Sources.FirstOrDefaultAsync(s => s.LinkedType == request.LinkedType && s.LinkedId == request.LinkedId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                existing.SetViewers(existing.ViewerEmployeeIds.Concat(request.ViewerEmployeeIds ?? []));
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await CommitAsync(own, cancellationToken).ConfigureAwait(false);
                return new CapSourceRef(existing.Id, existing.Number, false);
            }

            var type = await lookups.IsActiveCodeAsync(CapPermissions.SourceTypeLookup, request.SourceTypeCode, cancellationToken).ConfigureAwait(false)
                ? request.SourceTypeCode
                : CapSourceTypes.Other;
            var source = await service.CreateSourceAsync(new CapSourceValues(Cut(request.TitleAr, CapSource.TitleMaxLength), Cut(request.TitleEn, CapSource.TitleMaxLength), type,
                Cut(request.Description, CapSource.TextMaxLength), request.ReceivedOn, request.OwnerEmployeeId, request.UnitId, request.TeamId), "system", request.LinkedType,
                request.LinkedId, cancellationToken).ConfigureAwait(false);
            source.SetViewers(request.ViewerEmployeeIds ?? []);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await CommitAsync(own, cancellationToken).ConfigureAwait(false);
            return new CapSourceRef(source.Id, source.Number, true);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<Result<CapRemarkRef>> AddRemarkAsync(Guid sourceId, CapRemarkRequest request, object? transactionContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var own = await BeginAsync(transactionContext, cancellationToken).ConfigureAwait(false);
        try
        {
            var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return Error.NotFound();
            }

            var existing = await db.Remarks.FirstOrDefaultAsync(r => r.SourceId == sourceId && r.ExternalKey == request.ExternalKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return new CapRemarkRef(existing.Id, existing.Number, false);
            }

            var values = new CapRemarkValues(Cut(request.TextAr, CapSource.TextMaxLength), Cut(request.TextEn, CapSource.TextMaxLength), request.CriterionCode,
                CapRemarkTypes.All.Contains(request.TypeCode) ? request.TypeCode : CapRemarkTypes.Observation,
                CapPriorities.All.Contains(request.Priority) ? request.Priority : CapPriorities.Medium, request.DepartmentUnitId, request.SectionUnitId, request.OwnerEmployeeId);
            var errors = await service.ValidateRemarkAsync(values, lookups, cancellationToken).ConfigureAwait(false);
            if (errors.Count > 0)
            {
                return Error.Validation(errors);
            }

            var assigned = request.AssignedOn ?? await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
            var remark = await service.CreateRemarkAsync(source, values, assigned, request.ExternalKey, request.LinkedType, request.LinkedId, cancellationToken)
                .ConfigureAwait(false);
            var n = 0;
            foreach (var proposed in request.ProposedActions ?? [])
            {
                await service.NewActionAsync(remark, new CapActionValues(Cut(proposed.DetailsAr, CapSource.TextMaxLength), Cut(proposed.DetailsEn, CapSource.TextMaxLength),
                    proposed.TypeCode is { } t && CapActionTypes.All.Contains(t) ? t : CapActionTypes.Improvement, proposed.BudgetRequired, proposed.BudgetAvailable,
                    proposed.EstimatedCost, null, proposed.AssignedEmployeeId, proposed.PlannedStart, proposed.PlannedEnd), $"{request.ExternalKey}#{++n}", cancellationToken)
                    .ConfigureAwait(false);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await CommitAsync(own, cancellationToken).ConfigureAwait(false);
            return new CapRemarkRef(remark.Id, remark.Number, true);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<CapSourceProgress?> GetProgressAsync(string linkedType, Guid linkedId, CancellationToken cancellationToken = default)
    {
        var source = await db.Sources.AsNoTracking().FirstOrDefaultAsync(s => s.LinkedType == linkedType && s.LinkedId == linkedId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        var remarks = await db.Remarks.AsNoTracking().Where(r => r.SourceId == source.Id).OrderBy(r => r.Number).ToListAsync(cancellationToken).ConfigureAwait(false);
        var remarkIds = remarks.Select(r => r.Id).ToList();
        var links = await db.RemarkActions.AsNoTracking().Where(l => remarkIds.Contains(l.RemarkId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var actionIds = links.Select(l => l.ActionId).Distinct().ToList();
        var actions = await db.Actions.AsNoTracking().Where(a => actionIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken).ConfigureAwait(false);
        var files = await FileOwnersAsync(actionIds, cancellationToken).ConfigureAwait(false);
        var list = remarks.Select(r => new CapRemarkProgress(r.Id, r.Number, r.ExternalKey, r.TextAr, r.TextEn, r.Status, r.Stage, r.OwnerEmployeeId, r.DueOn,
            links.Where(l => l.RemarkId == r.Id).Select(l => actions[l.ActionId]).OrderBy(a => a.Number).Select(a => new CapActionProgress(a.Id, a.Number, a.DetailsAr,
                a.DetailsEn, a.ValidationStatus, a.Status, a.AchievedPct, files.Contains(a.Id.ToString("D")), a.PlannedEnd, a.RevisedEnd, a.AssignedEmployeeId,
                CapService.Link(a))).ToList(), CapService.Link(r))).ToList();
        var counted = actions.Values.Where(a => a.Status != ActionStatuses.Cancelled && a.ValidationStatus != ValidationStatuses.Rejected).ToList();
        var completed = counted.Count(a => a.Status == ActionStatuses.Completed);
        var all = completed > 0 && remarks.All(r => CapRemarkStatuses.IsFinal(r.Status) || r.Status == CapRemarkStatuses.Verification)
            && counted.Where(a => a.ValidationStatus == ValidationStatuses.Approved).All(a => a.Status == ActionStatuses.Completed);
        return new CapSourceProgress(source.Id, source.Number, list, counted.Count, completed, all, $"/cap/sources/{source.Id:D}");
    }

    private async Task<IReadOnlySet<string>> FileOwnersAsync(List<Guid> actionIds, CancellationToken ct)
    {
        return actionIds.Count == 0
            ? new HashSet<string>()
            : await fileService.OwnersWithFilesAsync(CapFilePurposes.Evidence.Key, actionIds.Select(a => a.ToString("D")).ToList(), ct).ConfigureAwait(false);
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(object? transactionContext, CancellationToken ct)
    {
        if (transactionContext is not null)
        {
            await db.JoinTransactionAsync(transactionContext, ct).ConfigureAwait(false);
            return null;
        }

        return db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
    }

    private static async Task CommitAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? own, CancellationToken ct)
    {
        if (own is not null)
        {
            await own.CommitAsync(ct).ConfigureAwait(false);
        }
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Raises a remark from a suggestion (one click or automatic, ADR 0123): source per parent record, remark per suggestion (idempotent).</summary>
internal sealed class SuggestionRaiser(CapDbContext db, CapService service, CapDeadlines deadlines, CapReader reader, IServiceProvider services)
{
    public async Task<Result<Remark>> RaiseAsync(Suggestion suggestion, Guid? unitOverride, CancellationToken ct)
    {
        if (suggestion.Status == SuggestionStatuses.Raised && suggestion.RemarkId is { } raised)
        {
            return await db.Remarks.FirstAsync(r => r.Id == raised, ct).ConfigureAwait(false);
        }

        var unit = unitOverride ?? suggestion.UnitId;
        if (unit is null || await reader.UnitAsync(unit.Value, ct).ConfigureAwait(false) is null)
        {
            return Error.Validation([new FieldError("unitId", "cap.errors.unitRequired", "cap.errors.unitRequired")]);
        }

        var today = await deadlines.TodayAsync(ct).ConfigureAwait(false);
        var (type, typeAr, typeEn) = suggestion.Kind switch
        {
            SuggestionKinds.KpiMiss => (CapSourceTypes.KpiPerformance, "أداء المؤشر", "KPI performance"),
            _ => (CapSourceTypes.RiskEvent, "الخطر", "Risk"),
        };
        var source = await db.Sources.FirstOrDefaultAsync(s => s.LinkedType == suggestion.ParentType && s.LinkedId == suggestion.ParentId, ct).ConfigureAwait(false)
            ?? await service.CreateSourceAsync(new CapSourceValues($"{typeAr}: {suggestion.ParentNumber}", $"{typeEn}: {suggestion.ParentNumber}", type, null, today, null, unit,
                null), "system", suggestion.ParentType, suggestion.ParentId, ct).ConfigureAwait(false);
        var existing = await db.Remarks.FirstOrDefaultAsync(r => r.SourceId == source.Id && r.ExternalKey == suggestion.ResourceId.ToString("D"), ct).ConfigureAwait(false);
        var remark = existing ?? await service.CreateRemarkAsync(source, new CapRemarkValues(suggestion.TitleAr, suggestion.TitleEn, null,
                suggestion.Kind == SuggestionKinds.KpiMiss ? CapRemarkTypes.Nonconformity : CapRemarkTypes.Observation, suggestion.Priority, unit.Value, null,
                suggestion.OwnerEmployeeId), today, suggestion.ResourceId.ToString("D"), suggestion.ResourceType, suggestion.ResourceId, ct).ConfigureAwait(false);
        suggestion.Raised(remark.Id);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return remark;
    }

    /// <summary>After commit: the risk event shows the remark (Phase 13 contract).</summary>
    public async Task BackLinkAsync(Suggestion suggestion, CancellationToken ct)
    {
        if (suggestion.Kind == SuggestionKinds.RiskEvent && suggestion.RemarkId is { } remarkId && services.GetService(typeof(IRiskDirectory)) is IRiskDirectory risks)
        {
            await risks.LinkCapRemarkAsync(suggestion.ResourceId, remarkId, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Records a suggestion from another system's event and raises it when the mode is automatic (ADR 0123). Idempotent per (kind, resource).</summary>
internal static class SuggestionIntake
{
    public static async Task ReceiveAsync(IServiceScopeFactory scopes, Func<CapSettings, string> mode, Func<CapSettings, bool> eligible, Func<Suggestion> create,
        CancellationToken ct)
    {
        await using var scope = Jobs.CapJobScope.Create(scopes);
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<CapDbContext>();
        var settings = await sp.GetRequiredService<CapReader>().SettingsAsync(ct).ConfigureAwait(false);
        var m = mode(settings);
        if (m == RaiseModes.Off || !eligible(settings))
        {
            return;
        }

        var suggestion = create();
        if (await db.Suggestions.AnyAsync(s => s.Kind == suggestion.Kind && s.ResourceId == suggestion.ResourceId, ct).ConfigureAwait(false))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Suggestions.Add(suggestion);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var raiser = sp.GetRequiredService<SuggestionRaiser>();
        if (m == RaiseModes.Automatic && suggestion.UnitId is not null)
        {
            var raised = await raiser.RaiseAsync(suggestion, null, ct).ConfigureAwait(false);
            if (raised.IsFailure)
            {
                return;
            }
        }
        else
        {
            await sp.GetRequiredService<CapNotifier>().SendAsync(CapNotifications.SuggestionRaised, [], new Dictionary<string, string>
            {
                ["number"] = suggestion.ParentNumber ?? string.Empty,
                ["titleAr"] = suggestion.TitleAr ?? string.Empty,
                ["titleEn"] = suggestion.TitleEn ?? string.Empty,
                ["date"] = string.Empty,
                ["actor"] = string.Empty,
                ["text"] = suggestion.ParentNumber ?? string.Empty,
                ["count"] = string.Empty,
            }, "/cap/suggestions", db, ct, [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await raiser.BackLinkAsync(suggestion, ct).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Details(object value) => JsonSerializer.Serialize(value, Json);
}

/// <summary>KPI result in the red band → CAP (SPEC §2.8 / §9.5, ADR 0065 / 0123).</summary>
internal sealed class KpiTargetMissedHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<KpiTargetMissed>
{
    public Task HandleAsync(KpiTargetMissed e, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        var period = $"{e.PeriodCode.ToUpperInvariant()} {e.Year.ToString(CultureInfo.InvariantCulture)}";
        string N(decimal? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
        return SuggestionIntake.ReceiveAsync(scopes, s => s.KpiMissMode, s => e.ConsecutiveMissed >= s.KpiMissConsecutive, () => Suggestion.Create(
            SuggestionKinds.KpiMiss, "kpi.result", e.ResultId, "kpi.card", e.CardId, e.CardCode,
            $"لم يحقق المؤشر «{e.CardNameAr ?? e.CardNameEn ?? e.CardCode}» المستهدف في {period} (المحقق {N(e.Achieve)} مقابل المستهدف {N(e.Target)})",
            $"KPI “{e.CardNameEn ?? e.CardNameAr ?? e.CardCode}” missed its target in {period} (achieved {N(e.Achieve)} vs target {N(e.Target)})",
            SuggestionIntake.Details(new { e.CardCode, period, e.Achieve, e.Target, e.Capped, e.Rag, e.ConsecutiveMissed }), e.ResponsibleUnitId, e.ResponsibleEmployeeId,
            e.ConsecutiveMissed >= 2 ? CapPriorities.High : CapPriorities.Medium), cancellationToken);
    }
}

/// <summary>A logged risk event → CAP (SPEC §3.8 / §9.5).</summary>
internal sealed class RiskEventLoggedHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<RiskEventLogged>
{
    public Task HandleAsync(RiskEventLogged e, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.RiskIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        var text = e.Description ?? e.Number;
        return SuggestionIntake.ReceiveAsync(scopes, s => s.RiskEventMode, _ => true, () => Suggestion.Create(
            SuggestionKinds.RiskEvent, "risk.event", e.EventId, "risk.risk", e.RiskIds[0], e.Number,
            $"حدث خطر {e.Number}: {text}", $"Risk event {e.Number}: {text}",
            SuggestionIntake.Details(new { e.Number, e.OccurredOn, e.NearMiss, e.FinancialImpact }), e.UnitId, null,
            e.NearMiss ? CapPriorities.Medium : CapPriorities.High), cancellationToken);
    }
}

/// <summary>A control rated ineffective → CAP (SPEC §3.8 / §9.5).</summary>
internal sealed class RiskControlIneffectiveHandler(IServiceScopeFactory scopes) : IIntegrationEventHandler<RiskControlRatedIneffective>
{
    public Task HandleAsync(RiskControlRatedIneffective e, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(e);
        var name = e.ControlNameEn ?? e.ControlNameAr ?? e.RiskNumber;
        return SuggestionIntake.ReceiveAsync(scopes, s => s.RiskControlMode, _ => true, () => Suggestion.Create(
            SuggestionKinds.RiskControl, "risk.control", e.ControlId, "risk.risk", e.RiskId, e.RiskNumber,
            $"ضابط غير فعال «{e.ControlNameAr ?? name}» للخطر {e.RiskNumber}", $"Ineffective control “{name}” of risk {e.RiskNumber}",
            SuggestionIntake.Details(new { e.RiskNumber, control = name }), e.UnitId, e.OwnerEmployeeId, CapPriorities.High), cancellationToken);
    }
}

public sealed record CapSuggestionDto(
    Guid Id, string Kind, string ResourceType, Guid ResourceId, string ParentType, Guid ParentId, string? ParentNumber, string? TitleAr, string? TitleEn,
    JsonElement Details, CapUnitDto? Unit, string Priority, string Status, Guid? RemarkId, string? DismissReason, DateTimeOffset CreatedAt);

public sealed record ListSuggestions(PageRequest Page, string? Status, string? Kind) : IQuery<PagedResult<CapSuggestionDto>>;

public sealed record RaiseSuggestion(Guid Id, Guid? UnitId) : ICommand<CapSuggestionDto>;

public sealed record DismissSuggestion(Guid Id, string? Reason) : ICommand<CapSuggestionDto>;

/// <summary>Suggestions inbox (PM Office): one-click raise or dismiss with a reason.</summary>
internal sealed class SuggestionHandlers(CapDbContext db, CapReader reader, SuggestionRaiser raiser, IRequestContext context)
    : IQueryHandler<ListSuggestions, PagedResult<CapSuggestionDto>>, ICommandHandler<RaiseSuggestion, CapSuggestionDto>, ICommandHandler<DismissSuggestion, CapSuggestionDto>
{
    public async Task<Result<PagedResult<CapSuggestionDto>>> HandleAsync(ListSuggestions query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAllAsync(context.User, CapPermissions.Office, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var source = db.Suggestions.AsNoTracking();
        source = source.Where(s => s.Status == (query.Status ?? SuggestionStatuses.Open));
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            source = source.Where(s => s.Kind == query.Kind);
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await source.OrderByDescending(s => s.CreatedAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var units = await reader.UnitsAsync(rows.Select(r => r.UnitId), cancellationToken).ConfigureAwait(false);
        return new PagedResult<CapSuggestionDto>(rows.Select(s => ToDto(s, units)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<CapSuggestionDto>> HandleAsync(RaiseSuggestion command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAllAsync(context.User, CapPermissions.Office, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var suggestion = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (suggestion is null)
        {
            return Error.NotFound();
        }

        if (suggestion.Status == SuggestionStatuses.Dismissed)
        {
            return Error.Rule("cap.suggestionDismissed", "cap.errors.suggestionDismissed");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            var raised = await raiser.RaiseAsync(suggestion, command.UnitId, cancellationToken).ConfigureAwait(false);
            if (raised.IsFailure)
            {
                return raised.Error;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await raiser.BackLinkAsync(suggestion, cancellationToken).ConfigureAwait(false);
        return ToDto(suggestion, await reader.UnitsAsync([suggestion.UnitId], cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<CapSuggestionDto>> HandleAsync(DismissSuggestion command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAllAsync(context.User, CapPermissions.Office, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var suggestion = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (suggestion is null)
        {
            return Error.NotFound();
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        if (suggestion.Status != SuggestionStatuses.Open)
        {
            return Error.Rule("cap.suggestionClosed", "cap.errors.suggestionClosed");
        }

        suggestion.Dismiss(command.Reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(suggestion, await reader.UnitsAsync([suggestion.UnitId], cancellationToken).ConfigureAwait(false));
    }

    private static CapSuggestionDto ToDto(Suggestion s, IReadOnlyDictionary<Guid, CapUnitDto> units) => new(s.Id, s.Kind, s.ResourceType, s.ResourceId, s.ParentType, s.ParentId,
        s.ParentNumber, s.TitleAr, s.TitleEn, JsonDocument.Parse(s.DetailsJson).RootElement.Clone(), CapReader.Unit(s.UnitId, units), s.Priority, s.Status, s.RemarkId,
        s.DismissReason, s.CreatedAt);
}

/// <summary>Sources, remarks and actions in the Links registry (task-able; remarks are also remark sources for follow-ups).</summary>
internal sealed class SourceLinkTargets(CapReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        CapSource.EntityType, CapPermissions.SystemCode, "مصدر ملاحظات", "Remarks source", [LinkCapabilities.TaskAble], CapPermissions.Read, "/cap/sources/{id}",
        CapPermissions.SourceManage);

    public string ResourceType => CapSource.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.SourcesAsync(user, cancellationToken).ConfigureAwait(false)).Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(s => s.Id, s => new LinkTarget(s.Id, s.Number, s.TitleAr, s.TitleEn, s.Status, null, null, s.Status == "active" ? "info" : "neutral",
            s.Status != "active"));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.SourceAsync(id, user, cancellationToken).ConfigureAwait(false) is not null && await reader.CanAsync(user, permission, cancellationToken).ConfigureAwait(false);
}

internal sealed class RemarkLinkTargets(CapReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        Remark.EntityType, CapPermissions.SystemCode, "ملاحظة خطة العمل", "CAP remark", [LinkCapabilities.TaskAble, LinkCapabilities.RemarkSource], CapPermissions.Read,
        "/cap/remarks/{id}", CapPermissions.Respond);

    public string ResourceType => Remark.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false)).Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => new LinkTarget(r.Id, r.Number, CapNotifier.Short(r.TextAr ?? r.TextEn), CapNotifier.Short(r.TextEn ?? r.TextAr), r.Status,
            null, null, Tone(r.Status), CapRemarkStatuses.IsFinal(r.Status)));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.RemarkAsync(id, user, cancellationToken).ConfigureAwait(false) is { } remark
        && await reader.CanAsync(user, permission, remark, cancellationToken).ConfigureAwait(false);

    public static string Tone(string status) => status switch
    {
        CapRemarkStatuses.Completed => "success",
        CapRemarkStatuses.Rejected or CapRemarkStatuses.Cancelled => "neutral",
        CapRemarkStatuses.Correction or CapRemarkStatuses.RejectionPending => "warning",
        _ => "info",
    };
}

internal sealed class ActionLinkTargets(CapReader reader) : ILinkTargetSource
{
    public static readonly LinkableTypeDefinition Definition = new(
        CapAction.EntityType, CapPermissions.SystemCode, "إجراء تصحيحي", "Corrective action", [LinkCapabilities.TaskAble], CapPermissions.Read, "/cap/actions/{id}",
        CapPermissions.ActionUpdate);

    public string ResourceType => CapAction.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var rows = await (await reader.ActionsAsync(user, cancellationToken).ConfigureAwait(false)).Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(a => a.Id, a => new LinkTarget(a.Id, a.Number, CapNotifier.Short(a.DetailsAr ?? a.DetailsEn), CapNotifier.Short(a.DetailsEn ?? a.DetailsAr),
            a.Status, null, null, a.Status switch
            {
                ActionStatuses.Completed => "success",
                ActionStatuses.Late => "danger",
                ActionStatuses.Cancelled => "neutral",
                _ => "info",
            }, !a.IsOpen));
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (await reader.ActionAsync(id, user, cancellationToken).ConfigureAwait(false) is not { } action)
        {
            return false;
        }

        return permission == CapPermissions.Read || (permission == CapPermissions.ActionUpdate && user.EmployeeIdOf() == action.AssignedEmployeeId)
            || await reader.CanAllAsync(user, CapPermissions.Office, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Source documents: read with the source, written by the PM Office.</summary>
internal sealed class SourceFiles(CapReader reader) : IFileOwnerAccess
{
    public string OwnerType => CapSource.EntityType;

    public string AttachmentPurpose => CapFilePurposes.SourceDocument.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.SourceAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        await CanReadAsync(user, ownerId, cancellationToken).ConfigureAwait(false) && await reader.CanAllAsync(user, CapPermissions.SourceManage, cancellationToken).ConfigureAwait(false);
}

/// <summary>Root-cause attachments: read with the remark, written by whoever may edit the root cause.</summary>
internal sealed class RemarkFiles(CapReader reader, CapViews views) : IFileOwnerAccess
{
    public string OwnerType => Remark.EntityType;

    public string AttachmentPurpose => CapFilePurposes.RootCause.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.RemarkAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.RemarkAsync(id, user, cancellationToken).ConfigureAwait(false) is { } remark
        && (await views.PermissionsAsync(remark, user, cancellationToken).ConfigureAwait(false)).RootCause;
}

/// <summary>Proof of completion: read with the action, written by the assignee (or PM Office) while the action is open.</summary>
internal sealed class ActionFiles(CapReader reader, CapService service, ActionViews views) : IFileOwnerAccess
{
    public string OwnerType => CapAction.EntityType;

    public string AttachmentPurpose => CapFilePurposes.Evidence.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.ActionAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id) || await reader.ActionAsync(id, user, cancellationToken).ConfigureAwait(false) is not { } action || !action.IsOpen)
        {
            return false;
        }

        var remarks = await service.RemarksOfAsync(action.Id, cancellationToken).ConfigureAwait(false);
        var can = await views.PermissionsAsync(action, remarks, user, cancellationToken).ConfigureAwait(false);
        return can.Progress || can.Edit;
    }
}

/// <summary>Global search over remarks (number, text), scope-filtered in the query.</summary>
internal sealed class RemarkSearchSource(CapReader reader) : IRecordSearchSource
{
    public string ResourceType => Remark.EntityType;

    public string SystemCode => CapPermissions.SystemCode;

    public string ReadPermission => CapPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = Like.Pattern(text);
        var rows = await (await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => EF.Functions.ILike(r.Number, pattern, "\\") || EF.Functions.ILike(r.TextAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.TextEn ?? string.Empty, pattern, "\\"))
            .OrderBy(r => r.Number).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => Hit(r, 1)).ToList();
    }

    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var hits = await (await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Id != excludeId)
            .Select(r => new
            {
                Remark = r,
                Score = Math.Max(EF.Functions.TrigramsSimilarity(r.TextEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(r.TextAr ?? string.Empty, text)),
            })
            .Where(x => x.Score > 0.2).OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return hits.Select(h => Hit(h.Remark, h.Score)).ToList();
    }

    private static RecordSearchHit Hit(Remark r, double score) =>
        new(Remark.EntityType, r.Id, CapPermissions.SystemCode, r.Number, CapNotifier.Short(r.TextAr ?? r.TextEn), CapNotifier.Short(r.TextEn ?? r.TextAr), r.Status,
            CapService.Link(r), score);
}
