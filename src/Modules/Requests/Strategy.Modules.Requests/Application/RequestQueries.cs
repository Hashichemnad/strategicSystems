using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Requests.Application;

public sealed record RequestPersonDto(Guid? Id, string? NameAr, string? NameEn);

public sealed record RequestUnitDto(Guid Id, string Code, string? NameAr, string? NameEn);

public sealed record StageStepDto(string Code, string Kind, string? NameAr, string? NameEn, string State);

public sealed record StageActionDto(string Code, string Kind, string? LabelAr, string? LabelEn, FormSchema? Form, bool RequiresComment, string Tone);

public sealed record RequestActionsDto(bool Edit, bool Submit, bool Cancel, bool Decide, IReadOnlyList<string> DecideActions, FormSchema? ApproveForm,
    IReadOnlyList<StageActionDto> Actions, bool Upload);

public sealed record RequestEventDto(Guid Id, string Action, string? FromStatus, string? ToStatus, string? FromStage, string? ToStage, string? ActorName, string? Comment,
    JsonObject? Data, DateTimeOffset At);

public sealed record RequestTemplateDto(string Code, string? NameAr, string? NameEn);

public sealed record RequestRecordDto(
    Guid Id,
    string Number,
    string TypeCode,
    string? TypeNameAr,
    string? TypeNameEn,
    string SystemCode,
    string? TitleAr,
    string? TitleEn,
    string Status,
    string? Stage,
    IReadOnlyList<StageStepDto> Stages,
    RequestPersonDto Requester,
    RequestUnitDto? Unit,
    FormSchema Form,
    JsonObject Data,
    JsonObject ProcessData,
    IReadOnlyDictionary<string, FormSchema> StageForms,
    string? Outcome,
    string? SubjectType,
    Guid? SubjectId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? StageDueAt,
    bool Overdue,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    string Source,
    RequestActionsDto Can,
    ApprovalStatusView? Approval,
    IReadOnlyList<RequestEventDto> Events,
    IReadOnlyList<RequestTemplateDto> Templates,
    uint Version);

public sealed record RequestRowDto(
    Guid Id,
    string Number,
    string TypeCode,
    string? TypeNameAr,
    string? TypeNameEn,
    string? TitleAr,
    string? TitleEn,
    string Status,
    string? Stage,
    string? StageNameAr,
    string? StageNameEn,
    RequestPersonDto Requester,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? StageDueAt,
    bool Overdue,
    DateTimeOffset UpdatedAt);

public sealed record CatalogueTypeDto(
    string Code, string SystemCode, string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, string? Category, string Icon, bool Embeddable);

public sealed record CatalogueDto(IReadOnlyList<CatalogueTypeDto> Types, IReadOnlyList<CategoryDto> Categories);

public sealed record CategoryDto(string Code, string? LabelAr, string? LabelEn);

public sealed record TypeFormDto(string Code, string SystemCode, string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, string Icon, FormSchema Form,
    IReadOnlyList<StageStepDto> Stages, bool CanSubmit);

/// <summary>Read side of requests (request page, My Requests, processor queue, catalogue).</summary>
internal sealed class RequestQueries(
    RequestsDbContext db,
    RequestTypeCache types,
    RequestReader reader,
    RequestEngine engine,
    IApprovalEngine approvals,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    Platform.Settings.ILookupService lookups,
    IRequestContext context,
    IClock clock)
{
    public async Task<Result<RequestRecordDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        return request is null ? Error.NotFound() : await ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<RequestRecordDto> ToDtoAsync(Request request, CancellationToken ct)
    {
        var type = types.Find(request.TypeId) ?? throw new InvalidOperationException("Unknown request type.");
        var user = context.User;
        var requester = RequestReader.IsRequester(user, request);
        var stage = type.Stage(request.Stage);
        var status = stage?.ProcessKey is { } key && request.Status == RequestStatuses.InReview
            ? await approvals.GetStatusViewAsync(key, request.Id, user, ct).ConfigureAwait(false)
            : null;

        var actions = new List<StageActionDto>();
        if (request.Status == RequestStatuses.InProgress && stage is { Kind: StageKinds.Processing }
            && await reader.IsProcessorAsync(user, type, stage, ct).ConfigureAwait(false))
        {
            var allowed = engine.HandlerOf(type.Code) is { } handler
                ? await handler.AvailableActionsAsync(new RequestHandlerContext(engine.Snapshot(request, type), user), ct).ConfigureAwait(false)
                : null;
            actions.AddRange(stage.Actions.Where(a => allowed is null || allowed.Contains(a.Code))
                .Select(a => new StageActionDto(a.Code, a.Kind, a.LabelAr, a.LabelEn, a.Form, a.RequiresComment || a.Kind is ActionKinds.Return or ActionKinds.Reject, a.Tone)));
        }

        var editable = requester && request.Status is RequestStatuses.Draft or RequestStatuses.Returned;
        var cancel = (requester && request.Status is RequestStatuses.Draft or RequestStatuses.Returned or RequestStatuses.InReview)
            || (RequestStatuses.IsOpen(request.Status) && request.Status != RequestStatuses.Draft
                && await reader.CanAsync(user, RequestPermissions.Manage, request, ct).ConfigureAwait(false));
        var decide = status?.MyTaskId is not null;
        var can = new RequestActionsDto(editable, editable, cancel, decide, decide ? status!.MyActions : [], decide ? stage?.ApproveForm : null, actions,
            editable || actions.Count > 0);

        var stageForms = type.Stages.ToDictionary(s => s.Code, RequestEngine.StageForm);
        var processMasked = new JsonObject();
        foreach (var (code, node) in request.ProcessData)
        {
            if (node is JsonObject values)
            {
                processMasked[code] = FormSecrets.Masked(stageForms.GetValueOrDefault(code) ?? new FormSchema(), values);
            }
        }

        var events = await db.Events.AsNoTracking().Where(e => e.RequestId == request.Id).OrderBy(e => e.At).ToListAsync(ct).ConfigureAwait(false);
        var person = await PersonAsync(request.RequesterEmployeeId, ct).ConfigureAwait(false);
        RequestUnitDto? unit = request.UnitId is { } u && await org.GetUnitAsync(u, ct).ConfigureAwait(false) is { } unitRef
            ? new RequestUnitDto(unitRef.Id, unitRef.Code, unitRef.NameAr ?? unitRef.NameEn, unitRef.NameEn ?? unitRef.NameAr)
            : null;
        var processor = actions.Count > 0 || await reader.CanAsync(user, RequestPermissions.Manage, request, ct).ConfigureAwait(false);
        var templates = type.Templates.Where(t => t.Audience == "requester" ? requester || processor : processor)
            .Select(t => new RequestTemplateDto(t.Code, t.NameAr, t.NameEn)).ToList();
        return new RequestRecordDto(request.Id, request.Number, type.Code, type.NameAr, type.NameEn, type.SystemCode, request.TitleAr, request.TitleEn, request.Status, request.Stage,
            Steps(type, request), person, unit, type.Form, FormSecrets.Masked(type.Form, request.Data), processMasked, stageForms, request.Outcome, request.SubjectType,
            request.SubjectId, request.CreatedAt, request.SubmittedAt, request.StageDueAt, Overdue(request), request.CompletedAt, request.CancelReason, request.Source,
            can, status,
            events.Select(e => new RequestEventDto(e.Id, e.Action, e.FromStatus, e.ToStatus, e.FromStage, e.ToStage, e.ActorName, e.Comment,
                e.DataJson is null ? null : JsonNode.Parse(e.DataJson) as JsonObject, e.At)).ToList(),
            templates, request.Version);
    }

    public static IReadOnlyList<StageStepDto> Steps(TypeModel type, Request request)
    {
        var index = request.Stage is null ? -1 : type.Stages.ToList().FindIndex(s => s.Code == request.Stage);
        var done = request.Status == RequestStatuses.Completed;
        var list = new List<StageStepDto>
        {
            new("draft", "draft", "المسودة", "Draft", request.Status == RequestStatuses.Draft ? "current" : "done"),
        };
        for (var i = 0; i < type.Stages.Count; i++)
        {
            var s = type.Stages[i];
            var state = done || (index >= 0 && i < index) ? "done"
                : i == index && request.Status is RequestStatuses.InReview or RequestStatuses.InProgress or RequestStatuses.Returned ? "current"
                : i == index && request.Status is RequestStatuses.Rejected or RequestStatuses.Cancelled ? "stopped"
                : "upcoming";
            list.Add(new StageStepDto(s.Code, s.Kind, s.NameAr, s.NameEn, state));
        }

        list.Add(new StageStepDto("completed", "final", "مكتمل", "Completed", done ? "done" : "upcoming"));
        return list;
    }

    public bool Overdue(Request request) => request.StageDueAt is { } due && RequestStatuses.IsOpen(request.Status) && due < clock.UtcNow;

    /// <summary>My Requests: the caller's own requests, returned first, newest next.</summary>
    public async Task<PagedResult<RequestRowDto>> MineAsync(PageRequest page, string? status, string? type, string? search, CancellationToken ct)
    {
        var userId = context.UserId;
        var employee = context.User.EmployeeIdOf();
        var query = db.Requests.AsNoTracking().Where(r => (userId != null && r.RequesterUserId == userId) || (employee != null && r.RequesterEmployeeId == employee));
        query = Filter(query, status, type, search);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderBy(r => r.Status == RequestStatuses.Returned ? 0 : 1).ThenByDescending(r => r.UpdatedAt)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<RequestRowDto>(await ItemsAsync(rows, ct).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    /// <summary>Processor queue: requests at processing stages the user processes; with a status filter, every visible request of that status.</summary>
    public async Task<PagedResult<RequestRowDto>> QueueAsync(PageRequest page, string? status, string? type, string? stage, string? search, bool overdueOnly,
        CancellationToken ct)
    {
        var user = context.User;
        var mine = new List<(string Type, string Stage)>();
        foreach (var t in types.All)
        {
            foreach (var s in t.Stages.Where(s => s.Kind == StageKinds.Processing))
            {
                if (await reader.IsProcessorAsync(user, t, s, ct).ConfigureAwait(false))
                {
                    mine.Add((t.Code, s.Code));
                }
            }
        }

        var keys = mine.Select(m => m.Type + "|" + m.Stage).ToList();
        var visible = await reader.VisibleAsync(user, ct).ConfigureAwait(false);
        var query = status is null
            ? visible.Where(r => r.Status == RequestStatuses.InProgress && keys.Contains(r.TypeCode + "|" + r.Stage))
            : visible.Where(r => r.Status != RequestStatuses.Draft);
        query = Filter(query, status, type, search);
        if (stage is not null)
        {
            query = query.Where(r => r.Stage == stage);
        }

        if (overdueOnly)
        {
            var now = clock.UtcNow;
            query = query.Where(r => r.StageDueAt != null && r.StageDueAt < now);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderBy(r => r.StageDueAt == null).ThenBy(r => r.StageDueAt).ThenBy(r => r.SubmittedAt)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<RequestRowDto>(await ItemsAsync(rows, ct).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    private static IQueryable<Request> Filter(IQueryable<Request> query, string? status, string? type, string? search)
    {
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        if (type is not null)
        {
            query = query.Where(r => r.TypeCode == type);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SearchText.Normalise(search);
            query = query.Where(r => r.TitleSearch != null && EF.Functions.ILike(r.TitleSearch, "%" + term + "%"));
        }

        return query;
    }

    public async Task<IReadOnlyList<RequestRowDto>> ItemsAsync(IReadOnlyList<Request> rows, CancellationToken ct)
    {
        var people = await employees.GetManyAsync(rows.Where(r => r.RequesterEmployeeId is not null).Select(r => r.RequesterEmployeeId!.Value).Distinct().ToList(), ct)
            .ConfigureAwait(false);
        return rows.Select(r =>
        {
            var t = types.Find(r.TypeId);
            var s = t?.Stage(r.Stage);
            var p = r.RequesterEmployeeId is { } e && people.TryGetValue(e, out var emp)
                ? new RequestPersonDto(emp.Id, emp.NameAr ?? emp.NameEn, emp.NameEn ?? emp.NameAr)
                : new RequestPersonDto(null, null, null);
            return new RequestRowDto(r.Id, r.Number, r.TypeCode, t?.NameAr, t?.NameEn, r.TitleAr, r.TitleEn, r.Status, r.Stage, s?.NameAr, s?.NameEn, p, r.SubmittedAt,
                r.StageDueAt, Overdue(r), r.UpdatedAt);
        }).ToList();
    }

    public async Task<CatalogueDto> CatalogueAsync(CancellationToken ct)
    {
        var list = new List<CatalogueTypeDto>();
        foreach (var t in types.All.Where(t => t.ShowInCatalogue))
        {
            if (await reader.CanSubmitAsync(context.User, t, ct).ConfigureAwait(false))
            {
                list.Add(new CatalogueTypeDto(t.Code, t.SystemCode, t.NameAr, t.NameEn, t.DescriptionAr, t.DescriptionEn, t.Category, t.Icon, t.Embeddable));
            }
        }

        var categories = (await lookups.GetValuesAsync(RequestPermissions.CategoryLookup, cancellationToken: ct).ConfigureAwait(false))
            .Select(v => new CategoryDto(v.Code, v.LabelAr, v.LabelEn)).ToList();
        return new CatalogueDto(list, categories);
    }

    public async Task<Result<TypeFormDto>> TypeFormAsync(string code, CancellationToken ct)
    {
        if (types.Find(code) is not { Active: true } t || !await reader.CanSubmitAsync(context.User, t, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        return new TypeFormDto(t.Code, t.SystemCode, t.NameAr, t.NameEn, t.DescriptionAr, t.DescriptionEn, t.Icon, t.Form,
            t.Stages.Select(s => new StageStepDto(s.Code, s.Kind, s.NameAr, s.NameEn, "upcoming")).ToList(), true);
    }

    private async Task<RequestPersonDto> PersonAsync(Guid? employeeId, CancellationToken ct) =>
        employeeId is { } id && await employees.GetAsync(id, ct).ConfigureAwait(false) is { } e
            ? new RequestPersonDto(e.Id, e.NameAr ?? e.NameEn, e.NameEn ?? e.NameAr)
            : new RequestPersonDto(null, null, null);

    internal static JsonSerializerOptions Json => RequestJson.Options;
}
