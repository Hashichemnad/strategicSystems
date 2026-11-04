using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.AI;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Requests.Application;

/// <summary>"Next action + who" of a request (SPEC §0.2 current-status engine).</summary>
internal sealed class RequestCurrentStatus(RequestReader reader, RequestTypeCache types, RequestNotifier notifier, IApprovalEngine engine, IRequestContext context)
    : ICurrentStatusProvider
{
    public string ResourceType => Request.EntityType;

    public async Task<CurrentStatus?> GetAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var request = await reader.FindAsync(resourceId, context.User, cancellationToken).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return null;
        }

        var stage = type.Stage(request.Stage);
        var stageName = new BilingualText(stage?.NameAr ?? stage?.NameEn, stage?.NameEn ?? stage?.NameAr);
        var requester = RequestReader.IsRequester(context.User, request);
        switch (request.Status)
        {
            case RequestStatuses.Draft:
                return new CurrentStatus("requests.currentStatus.draft", new Dictionary<string, BilingualText>(), "neutral", "requests.currentStatus.requester", [],
                    requester ? [new CurrentStatusAction("submit", "requests.actions.submit")] : [], request.Stage);
            case RequestStatuses.Returned:
                return new CurrentStatus("requests.currentStatus.returned", new Dictionary<string, BilingualText> { ["stage"] = stageName }, "warning",
                    "requests.currentStatus.requester", [], requester ? [new CurrentStatusAction("submit", "requests.actions.resubmit")] : [], request.Stage);
            case RequestStatuses.InReview:
                {
                    var view = stage?.ProcessKey is { } key ? await engine.GetStatusViewAsync(key, request.Id, context.User, cancellationToken).ConfigureAwait(false) : null;
                    var step = new BilingualText(view?.Instance.CurrentStepNameAr ?? view?.Instance.CurrentStepNameEn, view?.Instance.CurrentStepNameEn ?? view?.Instance.CurrentStepNameAr);
                    IReadOnlyList<CurrentStatusAction> actions = view?.MyTaskId is { } task ? [new CurrentStatusAction("decide", "requests.actions.decide", "primary", task)] : [];
                    return new CurrentStatus("workflow.currentStatus.waitingStep", new Dictionary<string, BilingualText> { ["step"] = step }, "info",
                        "workflow.currentStatus.approvers", view?.Approvers ?? [], actions, request.Stage);
                }

            case RequestStatuses.InProgress when stage is not null:
                {
                    var processors = await notifier.ProcessorUsersAsync(type, stage, cancellationToken).ConfigureAwait(false);
                    var mine = await reader.IsProcessorAsync(context.User, type, stage, cancellationToken).ConfigureAwait(false);
                    return new CurrentStatus("requests.currentStatus.processing", new Dictionary<string, BilingualText> { ["stage"] = stageName }, "info",
                        "requests.currentStatus.processors", processors.Count > 0 ? [new PersonName(null, $"{processors.Count}", $"{processors.Count}")] : [],
                        mine ? [new CurrentStatusAction("act", "requests.actions.act")] : [], request.Stage);
                }

            case RequestStatuses.Completed:
                return new CurrentStatus("requests.currentStatus.completed", new Dictionary<string, BilingualText>(), "success", null, [], [], request.Stage);
            case RequestStatuses.Rejected:
                return new CurrentStatus("requests.currentStatus.rejected", new Dictionary<string, BilingualText>(), "danger", null, [], [], request.Stage);
            default:
                return new CurrentStatus("requests.currentStatus.cancelled", new Dictionary<string, BilingualText>(), "neutral", null, [], [], request.Stage);
        }
    }
}

/// <summary>My Work: requests returned to me and requests at a processing stage I act on (approval tasks come from the engine).</summary>
internal sealed class RequestsMyWork(RequestsDbContext db, RequestTypeCache types, RequestReader reader) : IMyWorkSource
{
    public string SystemCode => RequestPermissions.SystemCode;

    public async Task<IReadOnlyList<MyWorkItem>> GetItemsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.UserId();
        var items = new List<MyWorkItem>();
        var returned = await db.Requests.AsNoTracking().Where(r => r.RequesterUserId == userId && r.Status == RequestStatuses.Returned)
            .OrderBy(r => r.UpdatedAt).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        items.AddRange(returned.Select(r => new MyWorkItem(RequestPermissions.SystemCode, Request.EntityType, r.Id, r.Number, r.TitleAr, r.TitleEn,
            "requests.myWork.returned", RequestResource.Link(r.Id), r.UpdatedAt)));

        var keys = new List<string>();
        foreach (var type in types.All)
        {
            foreach (var stage in type.Stages.Where(s => s.Kind == StageKinds.Processing))
            {
                if (await reader.IsProcessorAsync(user, type, stage, cancellationToken).ConfigureAwait(false))
                {
                    keys.Add(type.Code + "|" + stage.Code);
                }
            }
        }

        if (keys.Count > 0)
        {
            var processing = await db.Requests.AsNoTracking().Where(r => r.Status == RequestStatuses.InProgress && keys.Contains(r.TypeCode + "|" + r.Stage))
                .OrderBy(r => r.StageDueAt == null).ThenBy(r => r.StageDueAt).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(processing.Select(r => new MyWorkItem(RequestPermissions.SystemCode, Request.EntityType, r.Id, r.Number, r.TitleAr, r.TitleEn,
                "requests.myWork.process", RequestResource.Link(r.Id), r.StageEnteredAt, r.StageDueAt is { } due ? DateOnly.FromDateTime(due.UtcDateTime) : null)));
        }

        return items;
    }
}

/// <summary>Requests are task-able (SPEC §0.0, ADR 0034).</summary>
public static class RequestLinks
{
    public static readonly LinkableTypeDefinition Definition = new(
        Request.EntityType, RequestPermissions.SystemCode, "طلب", "Request", [LinkCapabilities.TaskAble], RequestPermissions.Read, RequestResource.LinkTemplate);
}

internal sealed class RequestLinkTargets(RequestReader reader, RequestTypeCache types) : ILinkTargetSource
{
    public string ResourceType => Request.EntityType;

    public async Task<IReadOnlyDictionary<Guid, LinkTarget>> GetAsync(IReadOnlyCollection<Guid> ids, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r =>
        {
            var type = types.Find(r.TypeId);
            return new LinkTarget(r.Id, r.Number, r.TitleAr ?? type?.NameAr, r.TitleEn ?? type?.NameEn, r.Status, StatusLabel(r.Status, true), StatusLabel(r.Status, false),
                r.Status switch
                {
                    RequestStatuses.Completed => "success",
                    RequestStatuses.Rejected => "danger",
                    RequestStatuses.Returned => "warning",
                    RequestStatuses.InReview or RequestStatuses.InProgress => "info",
                    _ => "neutral",
                },
                RequestStatuses.IsFinal(r.Status));
        });
    }

    public async Task<bool> HasPermissionAsync(Guid id, string permission, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } request
        && (permission == RequestPermissions.Read || await reader.CanAsync(user, permission, request, cancellationToken).ConfigureAwait(false));

    public static string StatusLabel(string code, bool ar) => code switch
    {
        RequestStatuses.Draft => ar ? "مسودة" : "Draft",
        RequestStatuses.InReview => ar ? "قيد المراجعة" : "In review",
        RequestStatuses.Returned => ar ? "معاد للتصحيح" : "Returned",
        RequestStatuses.InProgress => ar ? "قيد المعالجة" : "In progress",
        RequestStatuses.Completed => ar ? "مكتمل" : "Completed",
        RequestStatuses.Rejected => ar ? "مرفوض" : "Rejected",
        RequestStatuses.Cancelled => ar ? "ملغى" : "Cancelled",
        _ => code,
    };
}

/// <summary>Files of a request: read with the request; write by the requester while editable and by processors at a processing stage.</summary>
internal sealed class RequestFiles(RequestReader reader, RequestTypeCache types) : IFileOwnerAccess
{
    public string OwnerType => Request.EntityType;

    public string AttachmentPurpose => RequestFilePurposes.Attachment.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) =>
        Guid.TryParse(ownerId, out var id) && await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id) || await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is not { } request)
        {
            return false;
        }

        if (RequestReader.IsRequester(user, request) && request.Status is RequestStatuses.Draft or RequestStatuses.Returned)
        {
            return true;
        }

        return request.Status == RequestStatuses.InProgress && types.Find(request.TypeId) is { } type && type.Stage(request.Stage) is { } stage
            && await reader.IsProcessorAsync(user, type, stage, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Global search / "Ask" over requests the user may read.</summary>
internal sealed class RequestSearchSource(RequestReader reader) : IRecordSearchSource
{
    public string ResourceType => Request.EntityType;

    public string SystemCode => RequestPermissions.SystemCode;

    public string ReadPermission => RequestPermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var term = SearchText.Normalise(text);
        if (term.Length == 0)
        {
            return [];
        }

        var visible = await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false);
        var rows = await visible.Where(r => r.TitleSearch != null && EF.Functions.ILike(r.TitleSearch, "%" + term + "%")).OrderByDescending(r => r.UpdatedAt).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new RecordSearchHit(Request.EntityType, r.Id, RequestPermissions.SystemCode, r.Number, r.TitleAr, r.TitleEn, r.Status,
            RequestResource.Link(r.Id), 1)).ToList();
    }

    public Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken) =>
        SearchAsync(text, user, limit, cancellationToken);
}

/// <summary><see cref="IRequestDirectory"/> for other modules.</summary>
internal sealed class RequestDirectory(RequestReader reader, RequestsDbContext db) : IRequestDirectory
{
    public async Task<RequestBadge?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        await reader.FindAsync(id, user, cancellationToken).ConfigureAwait(false) is { } r
            ? new RequestBadge(r.Id, r.Number, r.TypeCode, r.TitleAr, r.TitleEn, r.Status, r.Stage, RequestResource.Link(r.Id))
            : null;

    public async Task<IReadOnlyList<RequestBadge>> ForSubjectAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default) =>
        (await db.Requests.AsNoTracking().Where(r => r.SubjectType == subjectType && r.SubjectId == subjectId).OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(r => new RequestBadge(r.Id, r.Number, r.TypeCode, r.TitleAr, r.TitleEn, r.Status, r.Stage, RequestResource.Link(r.Id))).ToList();
}

/// <summary>Hourly: processors of a request past its stage deadline are told once (docs/design/requests.md §2.10).</summary>
[Quartz.DisallowConcurrentExecution]
internal sealed class StageOverdueJob(RequestContext requestContext, RequestsDbContext db, RequestTypeCache types, RequestNotifier notifier, IClock clock)
    : StrategyJob(requestContext)
{
    public static readonly JobDefinition Definition = new(
        "requests.stage-overdue", "تنبيهات تأخر معالجة الطلبات", "Overdue request processing notices", "0 10 * * * ?", typeof(StageOverdueJob),
        "يبلغ معالجي الطلبات مرة واحدة عند تجاوز مهلة المرحلة.", "Tells the processors once when a request is past its stage deadline.");

    protected override async Task<string?> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var overdue = await db.Requests.Where(r => r.Status == RequestStatuses.InProgress && r.StageDueAt != null && r.StageDueAt < now && r.OverdueNotifiedAt == null)
            .Take(500).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var request in overdue)
        {
            if (types.Find(request.TypeId) is { } type && type.Stage(request.Stage) is { } stage)
            {
                await notifier.ToProcessorsAsync(RequestNotifications.StageOverdue, request, type, stage, null, cancellationToken).ConfigureAwait(false);
            }

            request.MarkOverdueNotified(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return $"overdue={overdue.Count}";
    }
}
