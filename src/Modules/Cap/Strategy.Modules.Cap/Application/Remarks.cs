using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Cap.Contracts;
using Strategy.Modules.Cap.Domain;
using Strategy.Modules.Cap.Infrastructure;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Cap.Application;

public sealed record SourcePermissionsDto(bool Edit, bool AddRemark, bool Close);

public sealed record SourceListItemDto(
    Guid Id, string Number, string? TitleAr, string? TitleEn, string SourceTypeCode, DateOnly ReceivedOn, CapUnitDto? Unit, string Origin, string Status, int Remarks,
    int OpenRemarks, int CompletedRemarks);

public sealed record SourceDto(
    Guid Id, string Number, SourceValues Values, string Origin, string? LinkedType, Guid? LinkedId, LinkView? Linked, CapPersonDto? Owner, CapUnitDto? Unit, string Status,
    IReadOnlyList<CapPersonDto> Viewers, SourcePermissionsDto Can, uint Version);

public sealed record RemarkPermissionsDto(
    bool Respond, bool Reassign, bool RootCause, bool ManageActions, bool Submit, bool ConfirmRejection, bool Edit, bool Cancel, bool Verify, bool Validate);

public sealed record RemarkListItemDto(
    Guid Id, string Number, string? TextAr, string? TextEn, Guid SourceId, string SourceNumber, string SourceTypeCode, string TypeCode, string Priority, string? CriterionCode,
    CapUnitDto? Department, CapUnitDto? Section, CapPersonDto? Owner, string Status, string Stage, DateOnly DueOn, bool Overdue, int Actions, int CompletedActions);

public sealed record RootCauseDto(string? Method, IReadOnlyList<string> Whys, IReadOnlyDictionary<string, List<string>> Fishbone, string? Text, bool Required);

public sealed record RemarkDto(
    Guid Id,
    string Number,
    RemarkValues Values,
    Guid SourceId,
    string SourceNumber,
    string? SourceTitleAr,
    string? SourceTitleEn,
    string SourceTypeCode,
    LinkView? SourceLinked,
    LinkView? Linked,
    CapUnitDto? Department,
    CapUnitDto? Section,
    CapPersonDto? Owner,
    DateOnly AssignedOn,
    DateOnly DueOn,
    DateTimeOffset DueAt,
    bool Overdue,
    DateTimeOffset? RespondedAt,
    string Status,
    string Stage,
    string? RejectionReason,
    string? PmoDecision,
    string? PmoDecisionNote,
    RootCauseDto RootCause,
    bool AllRejected,
    int ReopenedCount,
    DateTimeOffset? CompletedAt,
    DateOnly? VerificationDueOn,
    string? CancelReason,
    RemarkPermissionsDto Can,
    uint Version);

public sealed record CapOptionDto(string Code, string? LabelAr, string? LabelEn);

public sealed record CapOptionsDto(
    IReadOnlyDictionary<string, IReadOnlyList<CapOptionDto>> Lookups, IReadOnlyList<string> FishboneCategories, bool CanManageSources, bool CanOffice, bool CanDashboard,
    bool CanExport, bool CanSettings, bool RejectionNeedsPmoConfirmation, bool EffectivenessVerification);

public sealed record KpiTrendDto(Guid CardId, string? Code, string? NameAr, string? NameEn, int Year, decimal? Achieved, decimal? Capped, string Rag, int ApprovedPeriods,
    int TotalPeriods);

public sealed record SourceBody(SourceValues Values, uint Version);

public sealed record RemarkBody(RemarkValues Values, uint Version);

public sealed record ListSources(PageRequest Page, string? Search, string? SourceTypeCode, string? Status) : IQuery<PagedResult<SourceListItemDto>>;

public sealed record GetSource(Guid Id) : IQuery<SourceDto>;

public sealed record CreateSource(SourceValues Values) : ICommand<SourceDto>;

public sealed record UpdateSource(Guid Id, SourceValues Values, uint Version) : ICommand<SourceDto>;

public sealed record CloseSource(Guid Id, bool Close) : ICommand<SourceDto>;

public sealed record ListRemarks(
    PageRequest Page, string? View, string? Search, Guid? SourceId, string? Status, string? Priority, Guid? UnitId, string? SourceTypeCode)
    : IQuery<PagedResult<RemarkListItemDto>>;

public sealed record GetRemark(Guid Id) : IQuery<RemarkDto>;

public sealed record GetCapOptions : IQuery<CapOptionsDto>;

public sealed record GetRemarkKpiTrend(Guid Id) : IQuery<KpiTrendDto?>;

public sealed record CreateRemark(Guid SourceId, RemarkValues Values) : ICommand<RemarkDto>;

public sealed record UpdateRemark(Guid Id, RemarkValues Values, uint Version) : ICommand<RemarkDto>;

public sealed record AcceptRemark(Guid Id) : ICommand<RemarkDto>;

public sealed record RejectRemark(Guid Id, string? Reason) : ICommand<RemarkDto>;

public sealed record ConfirmRemarkRejection(Guid Id, bool Confirm, string? Note) : ICommand<RemarkDto>;

public sealed record ReassignRemark(Guid Id, Guid OwnerEmployeeId) : ICommand<RemarkDto>;

public sealed record SaveRootCause(Guid Id, RootCauseValues Values, uint Version) : ICommand<RemarkDto>;

public sealed record SubmitRemarkActions(Guid Id, string? Comment) : ICommand<RemarkDto>;

public sealed record CancelRemark(Guid Id, string? Reason) : ICommand<RemarkDto>;

public sealed record VerifyRemark(Guid Id, bool Effective, string? Comment) : ICommand<RemarkDto>;

/// <summary>Maps remarks and sources for the API and works out what the caller may do (docs/design/cap.md §7).</summary>
internal sealed class CapViews(CapDbContext db, CapReader reader, ILinkRegistry links, IRequestContext context, IClock clock, ISettingsStore settings)
{
    public async Task<RemarkDto> ToDtoAsync(Remark r, CancellationToken ct)
    {
        var user = context.User;
        var source = await db.Sources.AsNoTracking().FirstAsync(s => s.Id == r.SourceId, ct).ConfigureAwait(false);
        var people = await reader.PeopleAsync([r.OwnerEmployeeId], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([r.DepartmentUnitId, r.SectionUnitId], ct).ConfigureAwait(false);
        var cap = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var can = await PermissionsAsync(r, user, ct).ConfigureAwait(false);
        var refs = new List<LinkRef>();
        if (source.LinkedType is { } st && source.LinkedId is { } sid)
        {
            refs.Add(new LinkRef(st, sid));
        }

        if (r.LinkedType is { } rt && r.LinkedId is { } rid)
        {
            refs.Add(new LinkRef(rt, rid));
        }

        var resolved = refs.Count == 0 ? [] : await links.ResolveAsync(refs, user, ct).ConfigureAwait(false);
        var (whys, fishbone) = r.RootCause();
        var required = cap.RootCauseRequired(r.Priority, r.TypeCode, source.SourceTypeCode, r.CriterionCode);
        return new RemarkDto(r.Id, r.Number, r.Values(), source.Id, source.Number, source.TitleAr, source.TitleEn, source.SourceTypeCode,
            resolved.FirstOrDefault(l => l.Type == source.LinkedType && l.Id == source.LinkedId),
            resolved.FirstOrDefault(l => l.Type == r.LinkedType && l.Id == r.LinkedId), CapReader.Unit(r.DepartmentUnitId, units), CapReader.Unit(r.SectionUnitId, units),
            CapReader.Person(r.OwnerEmployeeId, people), r.AssignedOn, r.DueOn, r.DueAt, Overdue(r), r.RespondedAt, r.Status, r.Stage, r.RejectionReason, r.PmoDecision,
            r.PmoDecisionNote, new RootCauseDto(r.RootCauseMethod, whys, fishbone, r.RootCauseText, required), r.AllRejected, r.ReopenedCount, r.CompletedAt,
            r.VerificationDueOn, r.CancelReason, can, r.Version);
    }

    public bool Overdue(Remark r) => r.IsOpen && r.Status != CapRemarkStatuses.Verification && r.DueAt < clock.UtcNow;

    public async Task<RemarkPermissionsDto> PermissionsAsync(Remark r, ClaimsPrincipal user, CancellationToken ct)
    {
        var me = user.EmployeeIdOf();
        var office = await reader.CanAsync(user, CapPermissions.Office, r, ct).ConfigureAwait(false);
        var manage = await reader.CanAsync(user, CapPermissions.SourceManage, r, ct).ConfigureAwait(false);
        var respond = await reader.CanAsync(user, CapPermissions.Respond, r, ct).ConfigureAwait(false)
            && (me is not null && r.OwnerEmployeeId == me || office);
        var planning = r.Status is CapRemarkStatuses.Planning or CapRemarkStatuses.Correction or CapRemarkStatuses.Implementation;
        var cap = await settings.GetAsync<CapSettings>(ct).ConfigureAwait(false);
        return new RemarkPermissionsDto(
            respond && r.Status == CapRemarkStatuses.Assigned,
            (respond || manage) && r.IsOpen && r.Status != CapRemarkStatuses.Validation,
            respond && r.IsOpen && r.Status != CapRemarkStatuses.Validation,
            respond && planning,
            respond && r.Status is CapRemarkStatuses.Planning or CapRemarkStatuses.Correction or CapRemarkStatuses.Implementation,
            office && r.Status == CapRemarkStatuses.RejectionPending,
            manage && r.IsOpen,
            (manage || office) && r.IsOpen,
            office && r.Status == CapRemarkStatuses.Verification && cap.EffectivenessVerification,
            office && r.Status == CapRemarkStatuses.Validation);
    }

    public async Task<IReadOnlyList<RemarkListItemDto>> ListItemsAsync(IReadOnlyList<Remark> remarks, CancellationToken ct)
    {
        var people = await reader.PeopleAsync(remarks.Select(r => r.OwnerEmployeeId), ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync(remarks.SelectMany(r => new[] { (Guid?)r.DepartmentUnitId, r.SectionUnitId }), ct).ConfigureAwait(false);
        var sourceIds = remarks.Select(r => r.SourceId).Distinct().ToList();
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        var ids = remarks.Select(r => r.Id).ToList();
        var counts = await db.RemarkActions.AsNoTracking().Where(l => ids.Contains(l.RemarkId))
            .Join(db.Actions.AsNoTracking(), l => l.ActionId, a => a.Id, (l, a) => new { l.RemarkId, a.Status })
            .Where(x => x.Status != ActionStatuses.Cancelled)
            .GroupBy(x => x.RemarkId).Select(g => new { g.Key, Total = g.Count(), Done = g.Count(x => x.Status == ActionStatuses.Completed) })
            .ToDictionaryAsync(x => x.Key, ct).ConfigureAwait(false);
        return remarks.Select(r => new RemarkListItemDto(r.Id, r.Number, r.TextAr, r.TextEn, r.SourceId, sources[r.SourceId].Number, sources[r.SourceId].SourceTypeCode,
            r.TypeCode, r.Priority, r.CriterionCode, CapReader.Unit(r.DepartmentUnitId, units), CapReader.Unit(r.SectionUnitId, units), CapReader.Person(r.OwnerEmployeeId, people),
            r.Status, r.Stage, r.DueOn, Overdue(r), counts.GetValueOrDefault(r.Id)?.Total ?? 0, counts.GetValueOrDefault(r.Id)?.Done ?? 0)).ToList();
    }

    public async Task<SourceDto> ToDtoAsync(CapSource s, CancellationToken ct)
    {
        var user = context.User;
        var people = await reader.PeopleAsync([s.OwnerEmployeeId, .. s.ViewerEmployeeIds.Select(v => (Guid?)v)], ct).ConfigureAwait(false);
        var units = await reader.UnitsAsync([s.UnitId], ct).ConfigureAwait(false);
        var manage = await reader.CanAllAsync(user, CapPermissions.SourceManage, ct).ConfigureAwait(false);
        LinkView? linked = null;
        if (s.LinkedType is { } type && s.LinkedId is { } id)
        {
            linked = (await links.ResolveAsync([new LinkRef(type, id)], user, ct).ConfigureAwait(false)) is [var first, ..] ? first : null;
        }

        return new SourceDto(s.Id, s.Number, s.Values(), s.Origin, s.LinkedType, s.LinkedId, linked, CapReader.Person(s.OwnerEmployeeId, people),
            CapReader.Unit(s.UnitId, units), s.Status, s.ViewerEmployeeIds.Select(v => CapReader.Person(v, people)!).ToList(),
            new SourcePermissionsDto(manage && s.Origin == "manual", manage && s.Status == "active", manage), s.Version);
    }
}

internal sealed class RemarkQueries(CapDbContext db, CapReader reader, CapViews views, ILookupService lookups, IRequestContext context, IClock clock, IServiceProvider services)
    : IQueryHandler<ListSources, PagedResult<SourceListItemDto>>, IQueryHandler<GetSource, SourceDto>, IQueryHandler<ListRemarks, PagedResult<RemarkListItemDto>>,
        IQueryHandler<GetRemark, RemarkDto>, IQueryHandler<GetCapOptions, CapOptionsDto>, IQueryHandler<GetRemarkKpiTrend, KpiTrendDto?>
{
    public async Task<Result<PagedResult<SourceListItemDto>>> HandleAsync(ListSources query, CancellationToken cancellationToken)
    {
        var source = await reader.SourcesAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(s => EF.Functions.ILike(s.Number, pattern, "\\") || EF.Functions.ILike(s.TitleAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(s.TitleEn ?? string.Empty, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.SourceTypeCode))
        {
            source = source.Where(s => s.SourceTypeCode == query.SourceTypeCode);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(s => s.Status == query.Status);
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await source.OrderByDescending(s => s.ReceivedOn).ThenByDescending(s => s.Number).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = rows.Select(s => s.Id).ToList();
        var visible = (await reader.RemarksAsync(context.User, cancellationToken).ConfigureAwait(false)).Where(r => ids.Contains(r.SourceId));
        var counts = await visible.GroupBy(r => r.SourceId).Select(g => new
        {
            g.Key,
            Total = g.Count(),
            Completed = g.Count(r => r.Status == CapRemarkStatuses.Completed),
            Open = g.Count(r => r.Status != CapRemarkStatuses.Completed && r.Status != CapRemarkStatuses.Rejected && r.Status != CapRemarkStatuses.Cancelled),
        }).ToDictionaryAsync(x => x.Key, cancellationToken).ConfigureAwait(false);
        var units = await reader.UnitsAsync(rows.Select(r => r.UnitId), cancellationToken).ConfigureAwait(false);
        return new PagedResult<SourceListItemDto>(rows.Select(s => new SourceListItemDto(s.Id, s.Number, s.TitleAr, s.TitleEn, s.SourceTypeCode, s.ReceivedOn,
            CapReader.Unit(s.UnitId, units), s.Origin, s.Status, counts.GetValueOrDefault(s.Id)?.Total ?? 0, counts.GetValueOrDefault(s.Id)?.Open ?? 0,
            counts.GetValueOrDefault(s.Id)?.Completed ?? 0)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<SourceDto>> HandleAsync(GetSource query, CancellationToken cancellationToken) =>
        await reader.SourceAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } s
            ? await views.ToDtoAsync(s, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public static readonly IReadOnlyList<string> Views = ["all", "mine", "to-accept", "my-unit", "overdue", "to-confirm", "open"];

    public async Task<Result<PagedResult<RemarkListItemDto>>> HandleAsync(ListRemarks query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var source = await reader.RemarksAsync(user, cancellationToken).ConfigureAwait(false);
        var me = user.EmployeeIdOf() ?? Guid.Empty;
        var now = clock.UtcNow;
        switch (query.View)
        {
            case "mine":
                source = source.Where(r => r.OwnerEmployeeId == me);
                break;
            case "to-accept":
                source = source.Where(r => r.OwnerEmployeeId == me && r.Status == CapRemarkStatuses.Assigned);
                break;
            case "my-unit":
                if (await reader.EmployeeAsync(me, cancellationToken).ConfigureAwait(false) is { UnitId: { } unit })
                {
                    var units = await reader.UnitAndBelowAsync(unit, cancellationToken).ConfigureAwait(false);
                    source = source.Where(r => units.Contains(r.DepartmentUnitId) || (r.SectionUnitId != null && units.Contains(r.SectionUnitId.Value)));
                }
                else
                {
                    source = source.Where(_ => false);
                }

                break;
            case "overdue":
                source = source.Where(r => r.DueAt < now && r.Status != CapRemarkStatuses.Completed && r.Status != CapRemarkStatuses.Rejected
                    && r.Status != CapRemarkStatuses.Cancelled && r.Status != CapRemarkStatuses.Verification);
                break;
            case "to-confirm":
                source = source.Where(r => r.Status == CapRemarkStatuses.RejectionPending);
                break;
            case "open":
                source = source.Where(r => r.Status != CapRemarkStatuses.Completed && r.Status != CapRemarkStatuses.Rejected && r.Status != CapRemarkStatuses.Cancelled);
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(r => EF.Functions.ILike(r.Number, pattern, "\\") || EF.Functions.ILike(r.TextAr ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(r.TextEn ?? string.Empty, pattern, "\\"));
        }

        if (query.SourceId is { } sourceId)
        {
            source = source.Where(r => r.SourceId == sourceId);
        }

        if (!string.IsNullOrWhiteSpace(query.SourceTypeCode))
        {
            var typed = db.Sources.Where(s => s.SourceTypeCode == query.SourceTypeCode).Select(s => s.Id);
            source = source.Where(r => typed.Contains(r.SourceId));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(r => r.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            source = source.Where(r => r.Priority == query.Priority);
        }

        if (query.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, cancellationToken).ConfigureAwait(false);
            source = source.Where(r => units.Contains(r.DepartmentUnitId));
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = (page.Sort ?? "dueOn") switch
        {
            "number" => source.OrderBy(r => r.Number),
            "-number" => source.OrderByDescending(r => r.Number),
            "-dueOn" => source.OrderByDescending(r => r.DueOn).ThenBy(r => r.Number),
            _ => source.OrderBy(r => r.DueOn).ThenBy(r => r.Number),
        };
        var rows = await ordered.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResult<RemarkListItemDto>(await views.ListItemsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<Result<RemarkDto>> HandleAsync(GetRemark query, CancellationToken cancellationToken) =>
        await reader.RemarkAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false) is { } r
            ? await views.ToDtoAsync(r, cancellationToken).ConfigureAwait(false)
            : Error.NotFound();

    public async Task<Result<CapOptionsDto>> HandleAsync(GetCapOptions query, CancellationToken cancellationToken)
    {
        var user = context.User;
        var lists = new Dictionary<string, IReadOnlyList<CapOptionDto>>();
        foreach (var type in CapAccessCatalogue.Lookups)
        {
            var values = await lookups.GetValuesAsync(type.Code, cancellationToken: cancellationToken).ConfigureAwait(false);
            lists[type.Code] = values.Select(v => new CapOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        async Task<bool> Can(string permission) => await reader.CanAllAsync(user, permission, cancellationToken).ConfigureAwait(false);
        return new CapOptionsDto(lists, RootCauseMethods.FishboneCategories, await Can(CapPermissions.SourceManage).ConfigureAwait(false),
            await Can(CapPermissions.Office).ConfigureAwait(false), await reader.CanAsync(user, CapPermissions.DashboardRead, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, CapPermissions.Export, cancellationToken).ConfigureAwait(false),
            await reader.CanAsync(user, CapPermissions.SettingsManage, cancellationToken).ConfigureAwait(false), settings.RejectionNeedsPmoConfirmation,
            settings.EffectivenessVerification);
    }

    /// <summary>"The remark shows the KPI trend" (SPEC §9.5): year performance of the source's KPI card for the remark's year.</summary>
    public async Task<Result<KpiTrendDto?>> HandleAsync(GetRemarkKpiTrend query, CancellationToken cancellationToken)
    {
        var remark = await reader.RemarkAsync(query.Id, context.User, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return Error.NotFound();
        }

        var source = await db.Sources.AsNoTracking().FirstAsync(s => s.Id == remark.SourceId, cancellationToken).ConfigureAwait(false);
        if (source.LinkedType != "kpi.card" || source.LinkedId is not { } cardId || services.GetService(typeof(IKpiCatalog)) is not IKpiCatalog catalog)
        {
            return (KpiTrendDto?)null;
        }

        var card = await catalog.GetAsync(cardId, context.User, cancellationToken).ConfigureAwait(false);
        var performance = await catalog.GetPerformanceAsync(cardId, remark.AssignedOn.Year, context.User, cancellationToken).ConfigureAwait(false);
        if (card is null || performance is null)
        {
            return (KpiTrendDto?)null;
        }

        return new KpiTrendDto(cardId, card.Code, card.NameAr, card.NameEn, performance.Year, performance.Achieved, performance.Capped, performance.Rag,
            performance.ApprovedPeriods, performance.TotalPeriods);
    }
}

/// <summary>C1 commands (docs/design/cap.md §5): sources, remarks, owner response, PMO confirmation, root cause, submit, cancel, verification.</summary>
internal sealed class RemarkCommands(
    CapDbContext db, CapReader reader, CapViews views, CapService service, CapNotifier notifier, CapDeadlines deadlines, ILookupService lookups, IRequestContext context,
    IClock clock, CapApprovals approvals)
    : ICommandHandler<CreateSource, SourceDto>, ICommandHandler<UpdateSource, SourceDto>, ICommandHandler<CloseSource, SourceDto>, ICommandHandler<CreateRemark, RemarkDto>,
        ICommandHandler<UpdateRemark, RemarkDto>, ICommandHandler<AcceptRemark, RemarkDto>, ICommandHandler<RejectRemark, RemarkDto>,
        ICommandHandler<ConfirmRemarkRejection, RemarkDto>, ICommandHandler<ReassignRemark, RemarkDto>, ICommandHandler<SaveRootCause, RemarkDto>,
        ICommandHandler<SubmitRemarkActions, RemarkDto>, ICommandHandler<CancelRemark, RemarkDto>, ICommandHandler<VerifyRemark, RemarkDto>
{
    public async Task<Result<SourceDto>> HandleAsync(CreateSource command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAllAsync(context.User, CapPermissions.SourceManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = await ValidateSourceAsync(command.Values, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var source = await service.CreateSourceAsync(command.Values, "manual", null, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SourceDto>> HandleAsync(UpdateSource command, CancellationToken cancellationToken)
    {
        var source = await reader.SourceAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAllAsync(context.User, CapPermissions.SourceManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var values = command.Values;
        if (source.Origin != "manual")
        {
            // A system source keeps its type; titles, description and owner may be adjusted.
            values = values with { SourceTypeCode = source.SourceTypeCode };
        }

        var errors = await ValidateSourceAsync(values, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(source).Property(s => s.Version).OriginalValue = command.Version;
        source.Edit(values);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SourceDto>> HandleAsync(CloseSource command, CancellationToken cancellationToken)
    {
        var source = await reader.SourceAsync(command.Id, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAllAsync(context.User, CapPermissions.SourceManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (command.Close)
        {
            source.Close();
        }
        else
        {
            source.Reopen();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(CreateRemark command, CancellationToken cancellationToken)
    {
        var source = await reader.SourceAsync(command.SourceId, context.User, cancellationToken, tracked: true).ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAllAsync(context.User, CapPermissions.SourceManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (source.Status != "active")
        {
            return Error.Rule("cap.sourceClosed", "cap.errors.sourceClosed");
        }

        var errors = await service.ValidateRemarkAsync(command.Values, lookups, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var today = await deadlines.TodayAsync(cancellationToken).ConfigureAwait(false);
        var remark = await service.CreateRemarkAsync(source, command.Values, today, null, null, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(UpdateRemark command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).Edit)
        {
            return remark.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");
        }

        var errors = await service.ValidateRemarkAsync(command.Values, lookups, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(remark).Property(r => r.Version).OriginalValue = command.Version;
        var previousOwner = remark.OwnerEmployeeId;
        var previousPriority = remark.Priority;
        remark.Edit(command.Values with { OwnerEmployeeId = command.Values.OwnerEmployeeId ?? previousOwner });
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (previousPriority != remark.Priority)
        {
            var approved = (await service.ActionsOfAsync(remark.Id, cancellationToken, tracked: false).ConfigureAwait(false))
                .Any(a => a.ValidationStatus == ValidationStatuses.Approved);
            if (!approved)
            {
                await service.RescheduleAsync(remark, cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (previousOwner != remark.OwnerEmployeeId)
        {
            await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
            await notifier.SendAsync(CapNotifications.RemarkAssigned, [remark.OwnerEmployeeId], CapNotifier.Values(remark), CapService.Link(remark), db, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(AcceptRemark command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).Respond)
        {
            return Forbidden(remark);
        }

        return await TransitionAsync(remark, RemarkActions.Accept, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(RejectRemark command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).Respond)
        {
            return Forbidden(remark);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        var action = settings.RejectionNeedsPmoConfirmation ? RemarkActions.RejectForConfirmation : RemarkActions.Reject;
        var result = await TransitionAsync(remark, action, command.Reason, cancellationToken, async () =>
        {
            var template = settings.RejectionNeedsPmoConfirmation ? CapNotifications.RejectionToConfirm : CapNotifications.RemarkRejected;
            await notifier.SendAsync(template, [], CapNotifier.Values(remark, actor: await ActorAsync(cancellationToken).ConfigureAwait(false), text: command.Reason),
                CapService.Link(remark), db, cancellationToken, [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return result;
    }

    public async Task<Result<RemarkDto>> HandleAsync(ConfirmRemarkRejection command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).ConfirmRejection)
        {
            return Forbidden(remark);
        }

        if (!command.Confirm && string.IsNullOrWhiteSpace(command.Note))
        {
            return Error.Validation([new FieldError("note", "validation.required", "validation.required")]);
        }

        return await TransitionAsync(remark, command.Confirm ? RemarkActions.ConfirmRejection : RemarkActions.SendBack, command.Note, cancellationToken, async () =>
        {
            if (!command.Confirm)
            {
                await notifier.SendAsync(CapNotifications.RemarkSentBack, [remark.OwnerEmployeeId], CapNotifier.Values(remark, text: command.Note), CapService.Link(remark), db,
                    cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(ReassignRemark command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        var can = await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false);
        if (!can.Reassign)
        {
            return Forbidden(remark);
        }

        var employee = await reader.EmployeeAsync(command.OwnerEmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.Validation([new FieldError("ownerEmployeeId", "validation.invalid", "validation.invalid")]);
        }

        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        if (settings.ReassignWithinUnit && !can.Edit)
        {
            var units = await reader.UnitAndBelowAsync(remark.DepartmentUnitId, cancellationToken).ConfigureAwait(false);
            if (!units.Contains(employee.UnitId))
            {
                return Error.Validation([new FieldError("ownerEmployeeId", "cap.errors.reassignOutsideUnit", "cap.errors.reassignOutsideUnit")]);
            }
        }

        remark.Reassign(command.OwnerEmployeeId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(CapNotifications.RemarkAssigned, [remark.OwnerEmployeeId], CapNotifier.Values(remark), CapService.Link(remark), db, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(SaveRootCause command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).RootCause)
        {
            return Forbidden(remark);
        }

        var v = command.Values;
        var errors = new List<FieldError>();
        if (!RootCauseMethods.All.Contains(v.Method))
        {
            errors.Add(new FieldError("method", "validation.invalid", "validation.invalid"));
        }

        if ((v.Whys?.Count ?? 0) > 7)
        {
            errors.Add(new FieldError("whys", "cap.errors.tooManyWhys", "cap.errors.tooManyWhys"));
        }

        if (v.Text is { Length: > CapSource.TextMaxLength } || (v.Whys ?? []).Any(w => w.Length > 1000)
            || (v.Fishbone ?? new Dictionary<string, IReadOnlyList<string>>()).Values.Any(c => c.Count > 20 || c.Any(x => x.Length > 1000)))
        {
            errors.Add(new FieldError("text", "validation.maximumLength", "validation.maximumLength"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(remark).Property(r => r.Version).OriginalValue = command.Version;
        remark.SetRootCause(v);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(SubmitRemarkActions command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).Submit)
        {
            return Forbidden(remark);
        }

        var source = await db.Sources.AsNoTracking().FirstAsync(s => s.Id == remark.SourceId, cancellationToken).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        if (settings.RootCauseRequired(remark.Priority, remark.TypeCode, source.SourceTypeCode, remark.CriterionCode) && !remark.HasRootCause)
        {
            return Error.Rule("cap.rootCauseRequired", "cap.errors.rootCauseRequired");
        }

        var actions = await service.ActionsOfAsync(remark.Id, cancellationToken).ConfigureAwait(false);
        var toSubmit = actions.Where(a => a.IsEditable).ToList();
        if (toSubmit.Count == 0)
        {
            return Error.Rule("cap.noActions", "cap.errors.noActionsToSubmit");
        }

        var errors = new List<FieldError>();
        foreach (var action in toSubmit)
        {
            foreach (var e in await CapService.ValidateActionAsync(action.Values(), lookups, reader, true, cancellationToken).ConfigureAwait(false))
            {
                errors.Add(e with { Field = $"actions[{action.Number}].{e.Field}" });
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (remark.Status == CapRemarkStatuses.Implementation)
        {
            var moved = remark.Apply(RemarkActions.ToValidation, clock.UtcNow);
            if (moved.IsFailure)
            {
                return moved.Error;
            }
        }
        else
        {
            var applied = remark.Apply(RemarkActions.Submit, clock.UtcNow);
            if (applied.IsFailure)
            {
                return applied.Error;
            }
        }

        foreach (var action in toSubmit)
        {
            action.SubmitForValidation();
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var action in toSubmit)
        {
            var submitted = await approvals.SubmitAsync(CapProcesses.Validation, action, remark, command.Comment, db, cancellationToken).ConfigureAwait(false);
            if (submitted.IsFailure)
            {
                return submitted.Error;
            }
        }

        await service.SyncRolesAsync(remark, cancellationToken).ConfigureAwait(false);
        await notifier.SendAsync(CapNotifications.ActionsSubmitted, [], CapNotifier.Values(remark, actor: await ActorAsync(cancellationToken).ConfigureAwait(false),
            count: toSubmit.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), $"/cap/remarks/{remark.Id:D}/validate", db, cancellationToken,
            [CapPermissions.OfficeRoleKey]).ConfigureAwait(false);
        await service.PublishAsync(remark, source, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(CancelRemark command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).Cancel)
        {
            return Forbidden(remark);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.required", "validation.required")]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var action in await service.ActionsOfAsync(remark.Id, cancellationToken).ConfigureAwait(false))
        {
            var others = await db.RemarkActions.CountAsync(l => l.ActionId == action.Id && l.RemarkId != remark.Id, cancellationToken).ConfigureAwait(false);
            if (others == 0 && action.IsOpen)
            {
                if (action.PendingProcess is { } process)
                {
                    await approvals.CancelAsync(process, action.Id, "remark-cancelled", db, cancellationToken).ConfigureAwait(false);
                }

                action.Cancel(command.Reason);
            }
        }

        var applied = remark.Apply(RemarkActions.Cancel, clock.UtcNow, command.Reason);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.PublishAsync(remark, null, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RemarkDto>> HandleAsync(VerifyRemark command, CancellationToken cancellationToken)
    {
        var (remark, error) = await LoadAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (remark is null)
        {
            return error!;
        }

        if (!(await views.PermissionsAsync(remark, context.User, cancellationToken).ConfigureAwait(false)).Verify)
        {
            return Forbidden(remark);
        }

        if (!command.Effective && string.IsNullOrWhiteSpace(command.Comment))
        {
            return Error.Validation([new FieldError("comment", "validation.required", "validation.required")]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Checks.Add(EffectivenessCheck.Create(remark.Id, remark.VerificationDueOn, command.Effective, command.Comment, clock.UtcNow, context.User.UserId()));
        var applied = remark.Apply(command.Effective ? RemarkActions.Effective : RemarkActions.NotEffective, clock.UtcNow, command.Comment);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await service.PublishAsync(remark, null, cancellationToken).ConfigureAwait(false);
        if (!command.Effective)
        {
            await notifier.SendAsync(CapNotifications.RemarkAssigned, [remark.OwnerEmployeeId], CapNotifier.Values(remark, text: command.Comment), CapService.Link(remark), db,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<RemarkDto>> TransitionAsync(Remark remark, string action, string? reason, CancellationToken ct, Func<Task>? after = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var applied = remark.Apply(action, clock.UtcNow, reason);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (after is not null)
        {
            await after().ConfigureAwait(false);
        }

        await service.PublishAsync(remark, null, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await views.ToDtoAsync(remark, ct).ConfigureAwait(false);
    }

    private async Task<(Remark? Remark, Error? Error)> LoadAsync(Guid id, CancellationToken ct)
    {
        var remark = await reader.RemarkAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        return remark is null ? (null, Error.NotFound()) : (remark, null);
    }

    private static Error Forbidden(Remark remark) => remark.IsOpen ? Error.Forbidden() : Error.Rule("cap.notEditable", "cap.errors.notEditable");

    private async Task<string> ActorAsync(CancellationToken ct)
    {
        if (context.User.EmployeeIdOf() is { } me && await reader.EmployeeAsync(me, ct).ConfigureAwait(false) is { } e)
        {
            return e.NameAr ?? e.NameEn ?? string.Empty;
        }

        return string.Empty;
    }

    private async Task<List<FieldError>> ValidateSourceAsync(SourceValues v, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (v.TitleAr is { Length: > CapSource.TitleMaxLength } || v.TitleEn is { Length: > CapSource.TitleMaxLength })
        {
            errors.Add(new FieldError("titleEn", "validation.maximumLength", "validation.maximumLength"));
        }

        if (v.Description is { Length: > CapSource.TextMaxLength })
        {
            errors.Add(new FieldError("description", "validation.maximumLength", "validation.maximumLength"));
        }

        if (!await lookups.IsActiveCodeAsync(CapPermissions.SourceTypeLookup, v.SourceTypeCode, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("sourceTypeCode", "validation.invalid", "validation.invalid"));
        }

        if (v.UnitId is { } unit && await reader.UnitAsync(unit, ct).ConfigureAwait(false) is null)
        {
            errors.Add(new FieldError("unitId", "validation.invalid", "validation.invalid"));
        }

        if (v.OwnerEmployeeId is { } owner && await reader.EmployeeAsync(owner, ct).ConfigureAwait(false) is null)
        {
            errors.Add(new FieldError("ownerEmployeeId", "validation.invalid", "validation.invalid"));
        }

        return errors;
    }
}
