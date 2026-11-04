using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Calendar;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Requests.Application;

/// <summary>Values of an approve form given through the framework's decide endpoint, kept for the hook in the same scope (ADR 0081).</summary>
internal sealed class PendingDecision
{
    public Guid? RequestId { get; set; }

    public JsonObject? Values { get; set; }
}

/// <summary>Builds reference resolvers for the validator.</summary>
internal sealed class FormResolverFactory(ILookupService lookups, IEmployeeDirectory employees, IOrgDirectory org, IFileService files, ILinkRegistry links)
{
    public IFormReferenceResolver For(ClaimsPrincipal user, Guid? requestId) => new FormReferenceResolver(lookups, employees, org, files, links, user, requestId);
}

/// <summary>
/// The stage / action engine shared by the commands and the approval hook: snapshots for handlers, stage entry (approval submission, SLA,
/// processor notice), handler calls and the timeline (docs/design/requests.md §2.4–2.6).
/// </summary>
internal sealed class RequestEngine(
    RequestsDbContext db,
    FormSecrets secrets,
    IEnumerable<IRequestTypeHandler> handlers,
    IBusinessHoursCalculator businessHours,
    IEmployeeDirectory employees,
    IRequestContext context,
    RequestNotifier notifier,
    IServiceProvider services,
    IClock clock)
{
    public IRequestTypeHandler? HandlerOf(string typeCode) => handlers.FirstOrDefault(h => h.TypeCode == typeCode);

    /// <summary>The form of a stage's mini-forms: the approve form, or every action form of a processing stage.</summary>
    public static FormSchema StageForm(StageDefinition? stage) => stage is null
        ? new FormSchema()
        : stage.Kind == StageKinds.Approval
            ? stage.ApproveForm ?? new FormSchema()
            : new FormSchema
            {
                Sections = [new FormSection { Code = "actions", Fields = stage.Actions.SelectMany(a => a.Form?.AllFields ?? []).GroupBy(f => f.Code).Select(g => g.First()).ToList() }],
            };

    public RequestSnapshot Snapshot(Request request, TypeModel type)
    {
        var processPlain = new JsonObject();
        foreach (var (stageCode, node) in request.ProcessData)
        {
            if (node is JsonObject values)
            {
                processPlain[stageCode] = secrets.Plain(StageForm(type.Stage(stageCode)), values);
            }
        }

        return new RequestSnapshot(request.Id, request.Number, request.TypeCode, request.RequesterUserId, request.RequesterEmployeeId, request.UnitId, request.Status,
            request.Stage, new FormValues(secrets.Plain(type.Form, request.Data)), processPlain, request.SubjectType, request.SubjectId);
    }

    /// <summary>Calls the type's handler (built-in types) and applies its outcome to the request.</summary>
    public async Task<Result<RequestTransitionOutcome>> NotifyHandlerAsync(
        Request request, TypeModel type, string kind, string? fromStage, string? toStage, string? actionCode, JsonObject? actionValues, string? comment, CancellationToken ct)
    {
        if (HandlerOf(type.Code) is not { } handler)
        {
            return RequestTransitionOutcome.None;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var transition = new RequestTransition(kind, Snapshot(request, type), fromStage, toStage, actionCode, new FormValues(actionValues), comment, context.UserId);
        var outcome = await handler.OnTransitionAsync(transition, db, ct).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        request.SetSubject(outcome.Value.SubjectType, outcome.Value.SubjectId);
        request.SetOutcome(outcome.Value.Outcome);
        if (outcome.Value.TitleAr is not null || outcome.Value.TitleEn is not null)
        {
            request.SetTitle(outcome.Value.TitleAr ?? request.TitleAr, outcome.Value.TitleEn ?? request.TitleEn);
        }

        return outcome.Value;
    }

    /// <summary>
    /// Moves the request into <paramref name="stage"/> (null: completed): submits approval stages to the engine, sets the SLA of processing
    /// stages and tells the processors; tells the requester when the request completes.
    /// </summary>
    public async Task<Result> EnterAsync(Request request, TypeModel type, StageDefinition? stage, string? comment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (stage is null)
        {
            request.MoveTo(RequestStatuses.Completed, null, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.ToRequesterAsync(RequestNotifications.Completed, request, type, null, null, db, ct).ConfigureAwait(false);
            return Result.Success();
        }

        if (stage.Kind == StageKinds.Approval)
        {
            request.MoveTo(RequestStatuses.InReview, stage.Code, now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var engine = services.GetRequiredService<IApprovalEngine>();
            var started = await engine.SubmitAsync(
                new ApprovalSubmission(
                    stage.ProcessKey!,
                    new ApprovalSubject(Request.EntityType, request.Id, request.Number, request.TitleAr ?? type.NameAr, request.TitleEn ?? type.NameEn,
                        request.RequesterEmployeeId, request.RequesterUserId, request.UnitId),
                    type.IsBuiltIn ? new Dictionary<string, object?>() : Variables(type, request),
                    comment),
                db, ct).ConfigureAwait(false);
            return started.IsFailure ? started.Error : Result.Success();
        }

        DateTimeOffset? due = null;
        if (stage.SlaHours is > 0 and var hours)
        {
            var calendar = await businessHours.ResolveForUnitAsync(request.UnitId, ct).ConfigureAwait(false);
            due = await businessHours.AddWorkingTimeAsync(calendar.Id, now, TimeSpan.FromHours(hours), ct).ConfigureAwait(false);
        }

        request.MoveTo(RequestStatuses.InProgress, stage.Code, now, due);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.ToProcessorsAsync(RequestNotifications.StageEntered, request, type, stage, db, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task NotifyRequesterAsync(Platform.Notifications.NotificationTemplateDefinition template, Request request, TypeModel type, string? comment, CancellationToken ct) =>
        notifier.ToRequesterAsync(template, request, type, null, comment, db, ct);

    /// <summary>Entry-condition values of a custom type's process (ADR 0081).</summary>
    public static Dictionary<string, object?> Variables(TypeModel type, Request request)
    {
        var data = new FormValues(request.Data);
        var values = new Dictionary<string, object?> { [RequestResource.TypeVariable] = type.Code };
        foreach (var field in type.Form.AllFields.Where(f => FieldTypes.Variables.Contains(f.Type)))
        {
            values[$"f_{field.Code}"] = field.Type switch
            {
                FieldTypes.Checkbox => data.Flag(field.Code),
                FieldTypes.Picklist => data.Text(field.Code) ?? string.Empty,
                _ => data.Number(field.Code) ?? 0m,
            };
        }

        return values;
    }

    public async Task AddEventAsync(Request request, string action, string? fromStatus, string? fromStage, string? comment, JsonObject? maskedData, CancellationToken ct)
    {
        var actor = context.User;
        string? name = null;
        if (actor.EmployeeIdOf() is { } employee && await employees.GetAsync(employee, ct).ConfigureAwait(false) is { } e)
        {
            name = e.NameEn ?? e.NameAr;
        }

        db.Events.Add(RequestEvent.Create(request.Id, action, fromStatus, request.Status, fromStage, request.Stage, context.UserId, name, context.OnBehalfOfId, comment,
            maskedData, clock.UtcNow));
    }

    public static string Today(DateTimeOffset now) => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
