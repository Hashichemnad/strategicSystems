using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

/// <summary>Fields shared by create and update.</summary>
public interface IUnitFields
{
    string? Code { get; }

    string? NameAr { get; }

    string? NameEn { get; }

    string? LevelCode { get; }

    string? CostCentre { get; }
}

internal static class UnitRules
{
    public static void AddUnitRules<T>(this AbstractValidator<T> validator)
        where T : IUnitFields
    {
        validator.RuleFor(x => x.Code).NotEmpty().MaximumLength(OrgUnit.CodeMaxLength)
            .Matches("^[A-Za-z0-9][A-Za-z0-9_.-]*$").WithErrorCode("validation.codeFormat");
        validator.RuleFor(x => x.NameAr).NotEmpty().When(x => string.IsNullOrWhiteSpace(x.NameEn))
            .WithErrorCode("validation.atLeastOneLanguage");
        validator.RuleFor(x => x.NameAr).MaximumLength(OrgUnit.NameMaxLength);
        validator.RuleFor(x => x.NameEn).MaximumLength(OrgUnit.NameMaxLength);
        validator.RuleFor(x => x.LevelCode).NotEmpty().MaximumLength(OrgLevel.CodeMaxLength);
        validator.RuleFor(x => x.CostCentre).MaximumLength(OrgUnit.CostCentreMaxLength);
    }

    public static UnitDetails ToDetails(this IUnitFields fields, Guid? head, Guid? deputy) =>
        new(fields.Code!, fields.NameAr, fields.NameEn, fields.LevelCode!, head, deputy, fields.CostCentre);
}

// ---- Commands ----------------------------------------------------------------------------------------------

public sealed record CreateUnit(
    string? Code,
    string? NameAr,
    string? NameEn,
    string? LevelCode,
    Guid? ParentId,
    Guid? HeadEmployeeId,
    Guid? DeputyEmployeeId,
    string? CostCentre,
    DateOnly? EffectiveFrom) : ICommand<OrgUnitDto>, IUnitFields;

public sealed class CreateUnitValidator : AbstractValidator<CreateUnit>
{
    public CreateUnitValidator() => this.AddUnitRules();
}

internal sealed class CreateUnitHandler(OrgDbContext db, OrgStructure structure, OrgReader reader) : ICommandHandler<CreateUnit, OrgUnitDto>
{
    public async Task<Result<OrgUnitDto>> HandleAsync(CreateUnit command, CancellationToken cancellationToken)
    {
        var created = await structure.CreateAsync(
            command.ToDetails(command.HeadEmployeeId, command.DeputyEmployeeId), command.ParentId, command.EffectiveFrom, cancellationToken)
            .ConfigureAwait(false);
        if (created.IsFailure)
        {
            return created.Error;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await reader.GetUnitAsync(created.Value.Id, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record UpdateUnit(
    Guid Id,
    string? Code,
    string? NameAr,
    string? NameEn,
    string? LevelCode,
    Guid? HeadEmployeeId,
    Guid? DeputyEmployeeId,
    string? CostCentre,
    DateOnly? EffectiveDate,
    uint Version) : ICommand<OrgUnitDto>, IUnitFields;

public sealed class UpdateUnitValidator : AbstractValidator<UpdateUnit>
{
    public UpdateUnitValidator() => this.AddUnitRules();
}

internal sealed class UpdateUnitHandler(UnitChange change) : ICommandHandler<UpdateUnit, OrgUnitDto>
{
    public Task<Result<OrgUnitDto>> HandleAsync(UpdateUnit command, CancellationToken cancellationToken) =>
        change.ApplyAsync(command.Id, command.Version, (structure, unit, ct) =>
            structure.UpdateAsync(unit, command.ToDetails(command.HeadEmployeeId, command.DeputyEmployeeId), command.EffectiveDate, ct),
            cancellationToken);
}

public sealed record MoveUnit(Guid Id, Guid? NewParentId, DateOnly? EffectiveDate, uint Version) : ICommand<OrgUnitDto>;

internal sealed class MoveUnitHandler(UnitChange change) : ICommandHandler<MoveUnit, OrgUnitDto>
{
    public Task<Result<OrgUnitDto>> HandleAsync(MoveUnit command, CancellationToken cancellationToken) =>
        change.ApplyAsync(command.Id, command.Version, (structure, unit, ct) =>
            structure.MoveAsync(unit, command.NewParentId, command.EffectiveDate, ct), cancellationToken);
}

public sealed record DeactivateUnit(Guid Id, DateOnly? EffectiveDate, uint Version) : ICommand<OrgUnitDto>;

internal sealed class DeactivateUnitHandler(UnitChange change) : ICommandHandler<DeactivateUnit, OrgUnitDto>
{
    public Task<Result<OrgUnitDto>> HandleAsync(DeactivateUnit command, CancellationToken cancellationToken) =>
        change.ApplyAsync(command.Id, command.Version, (structure, unit, ct) =>
            structure.DeactivateAsync(unit, command.EffectiveDate, ct), cancellationToken);
}

public sealed record ActivateUnit(Guid Id, DateOnly? EffectiveDate, uint Version) : ICommand<OrgUnitDto>;

internal sealed class ActivateUnitHandler(UnitChange change) : ICommandHandler<ActivateUnit, OrgUnitDto>
{
    public Task<Result<OrgUnitDto>> HandleAsync(ActivateUnit command, CancellationToken cancellationToken) =>
        change.ApplyAsync(command.Id, command.Version, (structure, unit, ct) =>
            structure.ActivateAsync(unit, command.EffectiveDate, ct), cancellationToken);
}

/// <summary>Load → record access check (404) → change with history → save with the client's row version (409).</summary>
internal sealed class UnitChange(OrgDbContext db, OrgStructure structure, OrgReader reader, IAccessService access, IRequestContext context)
{
    public async Task<Result<OrgUnitDto>> ApplyAsync(
        Guid id,
        uint expectedVersion,
        Func<OrgStructure, OrgUnit, CancellationToken, Task<Result>> change,
        CancellationToken cancellationToken)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        if (unit is null
            || !await access.CanAsync(context.User, OrgPermissions.UnitManage, new ResourceRef(OrgPermissions.UnitResource, id), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        db.Entry(unit).Property(x => x.Version).OriginalValue = expectedVersion;
        var outcome = await change(structure, unit, cancellationToken).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await reader.GetUnitAsync(id, cancellationToken).ConfigureAwait(false);
    }
}

// ---- Queries -----------------------------------------------------------------------------------------------

public sealed record GetUnitTree(DateOnly? AsOf, bool IncludeInactive) : IQuery<IReadOnlyList<OrgTreeNodeDto>>;

internal sealed class GetUnitTreeHandler(OrgReader reader) : IQueryHandler<GetUnitTree, IReadOnlyList<OrgTreeNodeDto>>
{
    public async Task<Result<IReadOnlyList<OrgTreeNodeDto>>> HandleAsync(GetUnitTree query, CancellationToken cancellationToken) =>
        Result.Success(await reader.GetTreeAsync(query.AsOf, query.IncludeInactive, cancellationToken).ConfigureAwait(false));
}

public sealed record GetUnit(Guid Id) : IQuery<OrgUnitDto>;

internal sealed class GetUnitHandler(OrgReader reader) : IQueryHandler<GetUnit, OrgUnitDto>
{
    public Task<Result<OrgUnitDto>> HandleAsync(GetUnit query, CancellationToken cancellationToken) =>
        reader.GetUnitAsync(query.Id, cancellationToken);
}

public sealed record GetUnitHistory(Guid Id) : IQuery<IReadOnlyList<OrgUnitVersionDto>>;

internal sealed class GetUnitHistoryHandler(OrgReader reader) : IQueryHandler<GetUnitHistory, IReadOnlyList<OrgUnitVersionDto>>
{
    public Task<Result<IReadOnlyList<OrgUnitVersionDto>>> HandleAsync(GetUnitHistory query, CancellationToken cancellationToken) =>
        reader.GetUnitHistoryAsync(query.Id, cancellationToken);
}

public sealed record GetUnitPath(Guid Id, DateOnly? AsOf) : IQuery<IReadOnlyList<OrgUnitPathItemDto>>;

internal sealed class GetUnitPathHandler(OrgReader reader) : IQueryHandler<GetUnitPath, IReadOnlyList<OrgUnitPathItemDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitPathItemDto>>> HandleAsync(GetUnitPath query, CancellationToken cancellationToken)
    {
        var path = await reader.GetPathAsync(query.Id, query.AsOf, cancellationToken).ConfigureAwait(false);
        return path is null ? Error.NotFound() : Result.Success(path);
    }
}
