using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Requests.Application;

public sealed record DraftInput(string? TypeCode, JsonObject? Data, string? Source = null);

public sealed record SaveInput(JsonObject? Data, uint Version);

public sealed record RequestSubmitInput(uint Version, string? Comment = null);

public sealed record RequestCancelInput(string? Reason, uint Version);

public sealed record DecideInput(string? Action, string? Comment, JsonObject? Form);

public sealed record ActionInput(uint Version, string? Comment, JsonObject? Form);

public sealed record BulkCompleteInput(IReadOnlyList<Guid>? Ids, string? Action, string? Comment);

public sealed record BulkResultDto(IReadOnlyList<Guid> Done, IReadOnlyList<BulkFailureDto> Failed);

public sealed record BulkFailureDto(Guid Id, string Code);

/// <summary>Requester and processor commands (docs/design/requests.md §2.3–2.7). 404 outside visibility, 403 for visible-but-forbidden.</summary>
internal sealed class RequestCommands(
    RequestsDbContext db,
    RequestTypeCache types,
    RequestReader reader,
    RequestEngine engine,
    RequestQueries queries,
    FormSecrets secrets,
    FormResolverFactory resolvers,
    PendingDecision pending,
    INumberingService numbering,
    IEmployeeDirectory employees,
    IApprovalEngine approvals,
    IRequestContext context,
    IClock clock)
{
    public async Task<Result<RequestRecordDto>> CreateAsync(DraftInput input, CancellationToken ct)
    {
        var user = context.User;
        if (types.Find(input.TypeCode ?? string.Empty) is not { } type || !await reader.CanSubmitAsync(user, type, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var validated = await ValidateDataAsync(type, input.Data, null, null, submitting: false, ct).ConfigureAwait(false);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var employee = user.EmployeeIdOf();
        var unit = UnitFrom(type, validated.Value) ?? (employee is { } e ? (await employees.GetAsync(e, ct).ConfigureAwait(false))?.UnitId : null);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var number = await numbering.NextAsync(db, RequestPermissions.Numbering, new NumberingContext(clock.Today, TypeCode: type.NumberPrefix), ct).ConfigureAwait(false);
        var source = input.Source is "ui" or "embed" or "mcp" or "ai" ? input.Source : "ui";
        var request = Request.Create(type.Id, type.Code, type.SystemCode, type.SchemaVersion, number, user.UserId(), employee, unit, source);
        var (titleAr, titleEn) = Titles(type, validated.Value);
        request.SetData(secrets.Stored(type.Form, validated.Value), type.SchemaVersion, titleAr, titleEn, unit);
        db.Requests.Add(request);
        await engine.AddEventAsync(request, "created", null, null, null, null, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<Result<RequestRecordDto>> SaveAsync(Guid id, SaveInput input, CancellationToken ct)
    {
        var loaded = await LoadEditableAsync(id, LifecycleActions.Save, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (request, type) = loaded.Value;
        var validated = await ValidateDataAsync(type, input.Data, request, null, submitting: false, ct).ConfigureAwait(false);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = input.Version;
        var (titleAr, titleEn) = Titles(type, validated.Value);
        request.SetData(secrets.Stored(type.Form, validated.Value), type.SchemaVersion, titleAr, titleEn, UnitFrom(type, validated.Value));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await queries.ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<Result<RequestRecordDto>> SubmitAsync(Guid id, RequestSubmitInput input, CancellationToken ct)
    {
        var loaded = await LoadEditableAsync(id, LifecycleActions.Submit, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (request, type) = loaded.Value;
        if (!type.Active)
        {
            return Error.Rule("requests.type.inactive", "requests.errors.typeInactive");
        }

        var stored = request.Data;
        var plain = secrets.Plain(type.Form, stored);
        var validated = await ValidateDataAsync(type, plain, request, plain, submitting: true, ct).ConfigureAwait(false);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var (titleAr, titleEn) = Titles(type, validated.Value);
        request.SetData(secrets.Stored(type.Form, validated.Value), type.SchemaVersion, titleAr, titleEn, UnitFrom(type, validated.Value));
        db.Entry(request).Property(x => x.Version).OriginalValue = input.Version;
        var fromStatus = request.Status;
        var fromStage = request.Stage;

        // A returned request goes back to the stage that returned it; a draft starts at the first stage.
        var target = fromStatus == RequestStatuses.Returned && type.Stage(fromStage) is { } back ? back : RequestLifecycle.After(type.Stages, null);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var handled = await engine.NotifyHandlerAsync(request, type, TransitionKinds.Submitted, fromStage, target?.Code, null, null, input.Comment, ct).ConfigureAwait(false);
        if (handled.IsFailure)
        {
            return handled.Error;
        }

        var entered = await engine.EnterAsync(request, type, handled.Value.NextStage is { } next ? type.Stage(next) : target, input.Comment, ct).ConfigureAwait(false);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        await engine.AddEventAsync(request, fromStatus == RequestStatuses.Returned ? "resubmitted" : "submitted", fromStatus, fromStage, input.Comment, null, ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<Result<RequestRecordDto>> CancelAsync(Guid id, RequestCancelInput input, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return Error.NotFound();
        }

        // The requester cancels a draft, a returned or an in-review request; managers cancel any open request.
        var own = RequestReader.IsRequester(context.User, request) && request.Status is RequestStatuses.Draft or RequestStatuses.Returned or RequestStatuses.InReview;
        if (!own && !await reader.CanAsync(context.User, RequestPermissions.Manage, request, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (!RequestLifecycle.Allows(request.Status, LifecycleActions.Cancel))
        {
            return Error.Rule("requests.lifecycle.notAllowed", "requests.errors.notAllowed");
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = input.Version;
        var (fromStatus, fromStage) = (request.Status, request.Stage);
        request.Cancel(input.Reason, clock.UtcNow);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        if (fromStatus == RequestStatuses.InReview && type.Stage(fromStage)?.ProcessKey is { } processKey)
        {
            var stopped = await approvals.CancelAsync(processKey, request.Id, input.Reason.Trim(), db, ct).ConfigureAwait(false);
            if (stopped.IsFailure)
            {
                return stopped.Error;
            }
        }

        if (fromStatus != RequestStatuses.Draft)
        {
            var handled = await engine.NotifyHandlerAsync(request, type, TransitionKinds.Cancelled, fromStage, null, null, null, input.Reason, ct).ConfigureAwait(false);
            if (handled.IsFailure)
            {
                return handled.Error;
            }
        }

        await engine.AddEventAsync(request, "cancelled", fromStatus, fromStage, input.Reason, null, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>Approve / reject / return the approval stage from the request page, with the approve form (ADR 0081).</summary>
    public async Task<Result<RequestRecordDto>> DecideAsync(Guid id, DecideInput input, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return Error.NotFound();
        }

        if (request.Status != RequestStatuses.InReview || type.Stage(request.Stage) is not { Kind: StageKinds.Approval, ProcessKey: { } processKey } stage)
        {
            return Error.Rule("requests.lifecycle.notAllowed", "requests.errors.notAllowed");
        }

        var action = input.Action ?? string.Empty;
        if (action == ApprovalActions.Approve && stage.ApproveForm is { IsEmpty: false } form)
        {
            var current = request.ProcessData[stage.Code] is JsonObject saved ? secrets.Plain(form, saved) : [];
            var incoming = SecretValues.RestoreMasked(form, input.Form ?? [], current);
            var validated = await FormDataValidator.ValidateAsync(form, incoming, submitting: true, resolvers.For(context.User, request.Id), ct, "form").ConfigureAwait(false);
            if (!validated.IsValid)
            {
                return Error.Validation(validated.Errors);
            }

            pending.RequestId = request.Id;
            pending.Values = validated.Values;
        }

        var decided = await approvals.DecideAsync(processKey, request.Id, action, input.Comment, ct).ConfigureAwait(false);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        return await queries.GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>A processing-stage action (complete, custom, return, reject) with its mini-form.</summary>
    public async Task<Result<RequestRecordDto>> ActAsync(Guid id, string actionCode, ActionInput input, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return Error.NotFound();
        }

        if (!RequestLifecycle.Allows(request.Status, LifecycleActions.StageAction) || type.Stage(request.Stage) is not { Kind: StageKinds.Processing } stage)
        {
            return Error.Rule("requests.lifecycle.notAllowed", "requests.errors.notAllowed");
        }

        if (!await reader.IsProcessorAsync(context.User, type, stage, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (stage.Actions.FirstOrDefault(a => a.Code == actionCode) is not { } action
            || (engine.HandlerOf(type.Code) is { } h && await h.AvailableActionsAsync(new RequestHandlerContext(engine.Snapshot(request, type), context.User), ct)
                .ConfigureAwait(false) is { } allowed && !allowed.Contains(actionCode)))
        {
            return Error.Rule("requests.action.unavailable", "requests.errors.actionUnavailable");
        }

        if ((action.RequiresComment || action.Kind is ActionKinds.Return or ActionKinds.Reject) && string.IsNullOrWhiteSpace(input.Comment))
        {
            return Error.Validation([new FieldError("comment", "validation.notEmpty", "validation.notEmpty")]);
        }

        var stageForm = RequestEngine.StageForm(stage);
        var values = new JsonObject();
        if (action.Form is { IsEmpty: false } form)
        {
            var validated = await FormDataValidator.ValidateAsync(form, input.Form ?? [], submitting: true, resolvers.For(context.User, request.Id), ct, "form")
                .ConfigureAwait(false);
            if (!validated.IsValid)
            {
                return Error.Validation(validated.Errors);
            }

            values = validated.Values;
        }

        db.Entry(request).Property(x => x.Version).OriginalValue = input.Version;
        var (fromStatus, fromStage) = (request.Status, request.Stage);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (values.Count > 0)
        {
            var merged = request.ProcessData[stage.Code] is JsonObject existing ? (JsonObject)existing.DeepClone() : [];
            foreach (var (key, node) in secrets.Stored(stageForm, values))
            {
                merged[key] = node?.DeepClone();
            }

            request.SetProcessData(stage.Code, merged);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var target = action.Kind is ActionKinds.Return or ActionKinds.Reject ? null : action.ToStage;
        var handled = await engine.NotifyHandlerAsync(request, type, TransitionKinds.Action, stage.Code, target, action.Code, values, input.Comment, ct).ConfigureAwait(false);
        if (handled.IsFailure)
        {
            return handled.Error;
        }

        switch (action.Kind)
        {
            case ActionKinds.Return:
                request.MoveTo(RequestStatuses.Returned, stage.Code, clock.UtcNow);
                await engine.AddEventAsync(request, action.Code, fromStatus, fromStage, input.Comment, FormSecrets.Masked(stageForm, secrets.Stored(stageForm, values)), ct)
                    .ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await engine.NotifyRequesterAsync(RequestNotifications.Returned, request, type, input.Comment, ct).ConfigureAwait(false);
                break;
            case ActionKinds.Reject:
                request.MoveTo(RequestStatuses.Rejected, stage.Code, clock.UtcNow);
                await engine.AddEventAsync(request, action.Code, fromStatus, fromStage, input.Comment, null, ct).ConfigureAwait(false);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await engine.NotifyRequesterAsync(RequestNotifications.Rejected, request, type, input.Comment, ct).ConfigureAwait(false);
                break;
            default:
            {
                var next = handled.Value.NextStage ?? target;
                var entered = await engine.EnterAsync(request, type, next is null ? null : type.Stage(next), input.Comment, ct).ConfigureAwait(false);
                if (entered.IsFailure)
                {
                    return entered.Error;
                }

                await engine.AddEventAsync(request, action.Code, fromStatus, fromStage, input.Comment, FormSecrets.Masked(stageForm, secrets.Stored(stageForm, values)), ct)
                    .ConfigureAwait(false);
                break;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await queries.ToDtoAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>Bulk "complete" for processing stages whose action form has no required field (safe bulk action, SPEC §5.1).</summary>
    public async Task<Result<BulkResultDto>> BulkCompleteAsync(BulkCompleteInput input, CancellationToken ct)
    {
        var ids = (input.Ids ?? []).Distinct().Take(200).ToList();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(input.Action))
        {
            return Error.Validation([new FieldError("ids", "validation.notEmpty", "validation.notEmpty")]);
        }

        var done = new List<Guid>();
        var failed = new List<BulkFailureDto>();
        foreach (var id in ids)
        {
            // One transaction per request: a failure leaves the others done (reported).
            db.ChangeTracker.Clear();
            var result = await BulkOneAsync(id, input.Action, input.Comment, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                done.Add(id);
            }
            else
            {
                failed.Add(new BulkFailureDto(id, result.Error!.Code));
            }
        }

        return new BulkResultDto(done, failed);
    }

    private async Task<Result> BulkOneAsync(Guid id, string action, string? comment, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return Error.NotFound();
        }

        var stageAction = type.Stage(request.Stage)?.Actions.FirstOrDefault(a => a.Code == action);
        if (stageAction is null || stageAction.Kind is not (ActionKinds.Complete or ActionKinds.Custom) || stageAction.RequiresComment
            || (stageAction.Form?.AllFields.Any(f => f.Required) ?? false))
        {
            return Error.Rule("requests.bulk.notSafe", "requests.errors.bulkNotSafe");
        }

        var result = await ActAsync(id, action, new ActionInput(request.Version, comment, null), ct).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : result.Error!;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Result<(Request Request, TypeModel Type)>> LoadEditableAsync(Guid id, string action, CancellationToken ct)
    {
        var request = await reader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (request is null || types.Find(request.TypeId) is not { } type)
        {
            return Error.NotFound();
        }

        if (!RequestReader.IsRequester(context.User, request))
        {
            return Error.Forbidden();
        }

        if (!RequestLifecycle.Allows(request.Status, action))
        {
            return Error.Rule("requests.lifecycle.notAllowed", "requests.errors.notAllowed");
        }

        return (request, type);
    }

    /// <summary>Validates incoming values (masked secrets restored from the stored ones) and the handler's rules.</summary>
    private async Task<Result<JsonObject>> ValidateDataAsync(TypeModel type, JsonObject? input, Request? request, JsonObject? plainAlready, bool submitting, CancellationToken ct)
    {
        var current = request is null ? [] : plainAlready ?? secrets.Plain(type.Form, request.Data);
        var incoming = SecretValues.RestoreMasked(type.Form, input ?? [], current);
        var validated = await FormDataValidator.ValidateAsync(type.Form, incoming, submitting, resolvers.For(context.User, request?.Id), ct).ConfigureAwait(false);
        var errors = validated.Errors.ToList();
        if (errors.Count == 0 && engine.HandlerOf(type.Code) is { } handler)
        {
            var snapshot = new RequestSnapshot(request?.Id ?? Guid.Empty, request?.Number ?? string.Empty, type.Code, context.UserId, context.User.EmployeeIdOf(),
                request?.UnitId, request?.Status ?? RequestStatuses.Draft, request?.Stage, new FormValues(validated.Values), [], request?.SubjectType, request?.SubjectId);
            errors.AddRange(await handler.ValidateAsync(new RequestHandlerContext(snapshot, context.User), submitting, ct).ConfigureAwait(false));
        }

        return errors.Count > 0 ? Error.Validation(errors) : validated.Values;
    }

    private static (string? Ar, string? En) Titles(TypeModel type, JsonObject values)
    {
        var data = new FormValues(values);
        var ar = type.Form.AllFields.FirstOrDefault(f => f.TitleOf == "ar") is { } a ? data.Text(a.Code) : null;
        var en = type.Form.AllFields.FirstOrDefault(f => f.TitleOf == "en") is { } e ? data.Text(e.Code) : null;
        return (ar ?? en, en ?? ar);
    }

    private static Guid? UnitFrom(TypeModel type, JsonObject values) =>
        type.Form.AllFields.FirstOrDefault(f => f.RequestUnit && f.Type == FieldTypes.Unit) is { } field ? new FormValues(values).Id(field.Code) : null;
}
