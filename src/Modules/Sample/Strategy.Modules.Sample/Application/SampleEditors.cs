using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Sample.Application;

/// <summary>An employee holding the item's "editor" record role.</summary>
public sealed record SampleEditorDto(Guid EmployeeId, string? NameAr, string? NameEn, DateTimeOffset Since);

public sealed record ListSampleEditors(Guid ItemId) : IQuery<IReadOnlyList<SampleEditorDto>>;

/// <summary>Grants the "editor" record role on one item (CLAUDE.md §7: record roles are granted by the owning module).</summary>
public sealed record AddSampleEditor(Guid ItemId, Guid? EmployeeId) : ICommand<IReadOnlyList<SampleEditorDto>>;

public sealed record RemoveSampleEditor(Guid ItemId, Guid EmployeeId) : ICommand<IReadOnlyList<SampleEditorDto>>;

internal sealed class SampleEditors(SampleDbContext db, IAccessService access, IRecordRoleService recordRoles, IEmployeeDirectory employees, IRequestContext context)
    : IQueryHandler<ListSampleEditors, IReadOnlyList<SampleEditorDto>>,
      ICommandHandler<AddSampleEditor, IReadOnlyList<SampleEditorDto>>,
      ICommandHandler<RemoveSampleEditor, IReadOnlyList<SampleEditorDto>>
{
    public async Task<Result<IReadOnlyList<SampleEditorDto>>> HandleAsync(ListSampleEditors query, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(query.ItemId, SamplePermissions.Read, cancellationToken).ConfigureAwait(false);
        return check.IsFailure ? check.Error : Result<IReadOnlyList<SampleEditorDto>>.Success(await ListAsync(query.ItemId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<SampleEditorDto>>> HandleAsync(AddSampleEditor command, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(command.ItemId, SamplePermissions.Update, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        if (command.EmployeeId is not { } employeeId || await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false) is not { Status: not "left" })
        {
            return Error.Validation([new FieldError("employeeId", "validation.unknownReference", "validation.unknownReference")]);
        }

        await recordRoles.GrantAsync(SamplePermissions.ResourceType, command.ItemId, RecordPrincipalKinds.Employee, employeeId, SamplePermissions.EditorRole,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<SampleEditorDto>>.Success(await ListAsync(command.ItemId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<SampleEditorDto>>> HandleAsync(RemoveSampleEditor command, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(command.ItemId, SamplePermissions.Update, cancellationToken).ConfigureAwait(false);
        if (check.IsFailure)
        {
            return check.Error;
        }

        await recordRoles.RevokeAsync(SamplePermissions.ResourceType, command.ItemId, RecordPrincipalKinds.Employee, command.EmployeeId,
            SamplePermissions.EditorRole, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<SampleEditorDto>>.Success(await ListAsync(command.ItemId, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Result> CheckAsync(Guid itemId, string permission, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken).ConfigureAwait(false);
        if (item is null || !await access.CanAsync(context.User, SamplePermissions.Read, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        return permission == SamplePermissions.Read
            || await access.CanAsync(context.User, permission, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false)
            ? Result.Success()
            : Error.Forbidden();
    }

    private async Task<IReadOnlyList<SampleEditorDto>> ListAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var holders = (await recordRoles.GetHoldersAsync(SamplePermissions.ResourceType, itemId, cancellationToken).ConfigureAwait(false))
            .Where(h => h.RoleCode == SamplePermissions.EditorRole && h.PrincipalKind == RecordPrincipalKinds.Employee)
            .ToList();
        var people = await employees.GetManyAsync(holders.Select(h => h.PrincipalId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return holders.Select(h => new SampleEditorDto(
            h.PrincipalId, people.GetValueOrDefault(h.PrincipalId)?.NameAr, people.GetValueOrDefault(h.PrincipalId)?.NameEn, h.ValidFrom)).ToList();
    }
}
