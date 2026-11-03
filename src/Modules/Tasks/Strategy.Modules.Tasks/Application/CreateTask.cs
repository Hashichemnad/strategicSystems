using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Links;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Tasks.Application;

public sealed record RecurrenceInput(string? Frequency, int? Interval, DateOnly? Until, int? Count);

/// <summary>
/// New Task (SPEC §11.7): one or many assignees (and / or every employee of a unit) → one task per assignee, tied by an assignment group
/// when there are several (SPEC §11.4).
/// </summary>
public sealed record CreateTask(
    string? TitleAr,
    string? TitleEn,
    string? Description,
    string? TypeCode,
    string? SourceCode,
    string? CategoryCode,
    string? Priority,
    bool? Confidential,
    DateOnly? PlannedStart,
    DateOnly? DueDate,
    decimal? EstimatedHours,
    IReadOnlyList<Guid>? AssigneeEmployeeIds,
    Guid? AssigneeUnitId = null,
    string? CompletionRule = null,
    bool Draft = false,
    Guid? OnBehalfOfEmployeeId = null,
    string? LinkedType = null,
    Guid? LinkedId = null,
    IReadOnlyList<string>? Checklist = null,
    RecurrenceInput? Recurrence = null,
    IReadOnlyList<Guid>? DependsOn = null) : ICommand<CreatedTasksDto>, ITaskText;

/// <summary>Title in at least one language and the length limits shared by every task command.</summary>
public interface ITaskText
{
    string? TitleAr { get; }

    string? TitleEn { get; }

    string? Description { get; }
}

public static class TaskTextRules
{
    public static void AddTaskTextRules<T>(this AbstractValidator<T> validator)
        where T : ITaskText
    {
        ArgumentNullException.ThrowIfNull(validator);
        validator.RuleFor(x => x.TitleAr).MaximumLength(TaskItem.TitleMaxLength);
        validator.RuleFor(x => x.TitleEn).MaximumLength(TaskItem.TitleMaxLength);
        validator.RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        validator.RuleFor(x => x.TitleEn)
            .Must((x, _) => !string.IsNullOrWhiteSpace(x.TitleAr) || !string.IsNullOrWhiteSpace(x.TitleEn))
            .WithErrorCode("validation.atLeastOneLanguage")
            .OverridePropertyName("titleEn");
    }
}

public sealed class CreateTaskValidator : AbstractValidator<CreateTask>
{
    public CreateTaskValidator()
    {
        this.AddTaskTextRules();
        RuleFor(x => x.TypeCode).NotEmpty();
        RuleFor(x => x.SourceCode).NotEmpty();
        RuleFor(x => x.Priority).Must(p => p is null || TaskPriorities.All.Contains(p)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.CompletionRule).Must(r => r is null || GroupCompletionRules.Codes.Contains(r)).WithErrorCode("validation.unknownCode");
        RuleFor(x => x.AssigneeEmployeeIds)
            .Must((x, ids) => (ids?.Count ?? 0) > 0 || x.AssigneeUnitId is not null)
            .WithErrorCode("validation.notEmpty")
            .OverridePropertyName("assigneeEmployeeIds");
        RuleFor(x => x.AssigneeEmployeeIds).Must(ids => ids is null || ids.Count <= 50).WithErrorCode("validation.invalid");
        RuleFor(x => x.Checklist).Must(c => c is null || (c.Count <= 50 && c.All(t => (t?.Length ?? 0) <= ChecklistItem.TextMaxLength)))
            .WithErrorCode("validation.invalid");
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.PlannedStart).When(x => x.DueDate is not null && x.PlannedStart is not null)
            .WithErrorCode("validation.dateRange");
        RuleFor(x => x.EstimatedHours).InclusiveBetween(0, 10_000).When(x => x.EstimatedHours is not null);
        RuleFor(x => x.LinkedId).NotNull().When(x => x.LinkedType is not null);
        RuleFor(x => x.Recurrence).Must(r => r is null || new RecurrenceRule(r.Frequency ?? string.Empty, r.Interval ?? 1, r.Until, r.Count).IsValid)
            .WithErrorCode("validation.invalid");
        RuleFor(x => x.DependsOn).Must(d => d is null || d.Count <= 20).WithErrorCode("validation.invalid");
    }
}

/// <summary>Who may be assigned and on whose behalf a task is created (settings, SPEC §11.9).</summary>
internal sealed class TaskAssignees(IEmployeeDirectory employees, IOrgDirectory org)
{
    /// <summary>Active employees (not left) for the ids and the unit; unknown or departed employees are a validation error.</summary>
    public async Task<Result<IReadOnlyList<EmployeeReference>>> ResolveAsync(IReadOnlyList<Guid>? ids, Guid? unitId, CancellationToken ct)
    {
        var wanted = new List<Guid>(ids ?? []);
        if (unitId is { } unit)
        {
            if (await org.GetUnitAsync(unit, ct).ConfigureAwait(false) is not { Active: true })
            {
                return Error.Validation([new FieldError("assigneeUnitId", "validation.unknownReference", "validation.unknownReference")]);
            }

            wanted.AddRange(await employees.GetEmployeeIdsInUnitsAsync([unit], ct).ConfigureAwait(false));
        }

        var distinct = wanted.Distinct().ToList();
        var found = await employees.GetManyAsync(distinct, ct).ConfigureAwait(false);
        if (distinct.Count == 0 || distinct.Any(id => !found.TryGetValue(id, out var e) || e.Status == "left"))
        {
            return Error.Validation([new FieldError("assigneeEmployeeIds", "validation.unknownReference", "validation.unknownReference")]);
        }

        if (distinct.Count > 200)
        {
            return Error.Validation([new FieldError("assigneeEmployeeIds", "validation.invalid", "validation.invalid")]);
        }

        return Result<IReadOnlyList<EmployeeReference>>.Success(distinct.Select(id => found[id]).ToList());
    }

    /// <summary>The assign rule of the settings: anyone, the creator's unit and below, or direct reports. Self always.</summary>
    public async Task<bool> MayAssignAsync(EmployeeReference creator, EmployeeReference assignee, string scope, CancellationToken ct)
    {
        if (creator.Id == assignee.Id || scope == AssignScopes.Anyone)
        {
            return true;
        }

        return scope == AssignScopes.DirectReports
            ? assignee.ManagerEmployeeId == creator.Id
            : (await org.GetSelfAndDescendantIdsAsync(creator.UnitId, ct).ConfigureAwait(false)).Contains(assignee.UnitId);
    }

    /// <summary>A secretary creates on behalf of the head of their own unit (or of a unit above it).</summary>
    public async Task<bool> IsManagerOfSecretaryAsync(EmployeeReference secretary, Guid managerEmployeeId, CancellationToken ct)
    {
        foreach (var unit in (await org.GetPathAsync(secretary.UnitId, cancellationToken: ct).ConfigureAwait(false)).Reverse())
        {
            if (await org.GetHeadOfUnitAsync(unit.Id, cancellationToken: ct).ConfigureAwait(false) == managerEmployeeId)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class CreateTaskHandler(
    TasksDbContext db, TaskCreator factory, TaskAssignees assignees, TaskReader reader, TaskVisibility visibility, ILinkRegistry links, ILookupService lookups,
    ISettingsStore settingsStore, IEmployeeDirectory employees, IAccessService access, IRequestContext context)
    : ICommandHandler<CreateTask, CreatedTasksDto>
{
    public async Task<Result<CreatedTasksDto>> HandleAsync(CreateTask command, CancellationToken cancellationToken)
    {
        var user = context.User;
        if (user.EmployeeIdOf() is not { } actorEmployeeId || await employees.GetAsync(actorEmployeeId, cancellationToken).ConfigureAwait(false) is not { } actor)
        {
            return Error.Rule("tasks.task.noEmployee", "tasks.errors.noEmployee");
        }

        var settings = await settingsStore.GetAsync<TaskSettings>(cancellationToken).ConfigureAwait(false);
        var creator = actor;
        Guid? enteredBy = null;
        Guid? creatorUserId = user.UserId();
        if (command.OnBehalfOfEmployeeId is { } managerId && managerId != actor.Id)
        {
            if (!settings.SecretaryMayCreateOnBehalf || !await access.CanAsync(user, TaskPermissions.OnBehalf, null, cancellationToken).ConfigureAwait(false))
            {
                return Error.Forbidden();
            }

            if (!await assignees.IsManagerOfSecretaryAsync(actor, managerId, cancellationToken).ConfigureAwait(false)
                || await employees.GetAsync(managerId, cancellationToken).ConfigureAwait(false) is not { } manager)
            {
                return Error.Validation([new FieldError("onBehalfOfEmployeeId", "tasks.validation.notYourManager", "tasks.validation.notYourManager")]);
            }

            creator = manager;
            enteredBy = actor.Id;
            creatorUserId = null; // the manager's own user is resolved through the employee record role
        }

        var checkedContent = await ContentAsync(command, settings, cancellationToken).ConfigureAwait(false);
        if (checkedContent.IsFailure)
        {
            return checkedContent.Error;
        }

        var people = await assignees.ResolveAsync(command.AssigneeEmployeeIds, command.AssigneeUnitId, cancellationToken).ConfigureAwait(false);
        if (people.IsFailure)
        {
            return people.Error;
        }

        foreach (var assignee in people.Value)
        {
            if (!await assignees.MayAssignAsync(creator, assignee, settings.AssignScope, cancellationToken).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("assigneeEmployeeIds", "tasks.validation.mayNotAssign", "tasks.validation.mayNotAssign",
                    new Dictionary<string, object?> { ["name"] = assignee.NameEn ?? assignee.NameAr })]);
            }
        }

        RecurrenceRule? rule = null;
        if (command.Recurrence is { } recurrence)
        {
            rule = new RecurrenceRule(recurrence.Frequency!, recurrence.Interval ?? 1, recurrence.Until, recurrence.Count);
            if (people.Value.Count > 1 || (command.PlannedStart ?? command.DueDate) is null)
            {
                return Error.Validation([new FieldError("recurrence", "tasks.validation.recurrence", "tasks.validation.recurrence")]);
            }
        }

        if (command.DependsOn is { Count: > 0 } dependsOn)
        {
            var readable = await (await visibility.QueryAsync(user, cancellationToken).ConfigureAwait(false))
                .CountAsync(t => dependsOn.Contains(t.Id), cancellationToken).ConfigureAwait(false);
            if (readable != dependsOn.Distinct().Count())
            {
                return Error.Validation([new FieldError("dependsOn", "validation.unknownReference", "validation.unknownReference")]);
            }
        }

        if (command.LinkedType is { } type && command.LinkedId is { } linkedId
            && !await links.CanLinkAsync(user, type, linkedId, LinkCapabilities.TaskAble, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("linkedId", "validation.unknownReference", "validation.unknownReference")]);
        }

        var content = checkedContent.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Guid? groupId = null;
        if (people.Value.Count > 1)
        {
            var group = AssignmentGroup.Create(content.TitleAr, content.TitleEn, creator.Id, creatorUserId, command.CompletionRule ?? GroupCompletionRules.All,
                content.Confidential);
            db.Groups.Add(group);
            groupId = group.Id;
        }

        var created = new List<TaskItem>();
        foreach (var assignee in people.Value)
        {
            created.Add(await factory.CreateAsync(
                new NewTaskSpec(content, creator.Id, creatorUserId, enteredBy, assignee.Id, assignee.UnitId, command.Draft, groupId, null, command.Checklist,
                    command.DependsOn, rule),
                settings, actor.Id, user.UserId(), cancellationToken).ConfigureAwait(false));
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CreatedTasksDto(await reader.ListItemsAsync(created, user, cancellationToken).ConfigureAwait(false), groupId);
    }

    private async Task<Result<TaskContent>> ContentAsync(CreateTask command, TaskSettings settings, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (!await lookups.IsActiveCodeAsync(TaskPermissions.TypeLookup, command.TypeCode!, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("typeCode", "validation.unknownCode", "validation.unknownCode"));
        }

        if (!await lookups.IsActiveCodeAsync(TaskPermissions.SourceLookup, command.SourceCode!, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("sourceCode", "validation.unknownCode", "validation.unknownCode"));
        }

        if (command.CategoryCode is { Length: > 0 } category && !await lookups.IsActiveCodeAsync(TaskPermissions.CategoryLookup, category, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("categoryCode", "validation.unknownCode", "validation.unknownCode"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        return new TaskContent(
            command.TitleAr, command.TitleEn, command.Description, command.TypeCode!, settings.TemplateOf(command.TypeCode!), command.SourceCode!,
            command.CategoryCode, command.Priority ?? TaskPriorities.Medium, command.Confidential ?? settings.IsConfidentialCategory(command.CategoryCode),
            command.PlannedStart, command.DueDate, command.EstimatedHours, command.LinkedType, command.LinkedId);
    }
}
