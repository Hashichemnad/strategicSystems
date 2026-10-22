using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Application;

public sealed record ListLevels : IQuery<IReadOnlyList<OrgLevelDto>>;

internal sealed class ListLevelsHandler(OrgDbContext db) : IQueryHandler<ListLevels, IReadOnlyList<OrgLevelDto>>
{
    public async Task<Result<IReadOnlyList<OrgLevelDto>>> HandleAsync(ListLevels query, CancellationToken cancellationToken)
    {
        var levels = await db.Levels.AsNoTracking().OrderBy(l => l.Sort).ThenBy(l => l.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        return levels.Select(OrgLevelDto.From).ToList();
    }
}

public sealed record CreateLevel(string? Code, string? NameAr, string? NameEn, int Sort) : ICommand<OrgLevelDto>;

public sealed class CreateLevelValidator : AbstractValidator<CreateLevel>
{
    public CreateLevelValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(OrgLevel.CodeMaxLength)
            .Matches("^[a-z][a-z0-9_]*$").WithErrorCode("validation.codeFormat");
        RuleFor(x => x.NameAr).NotEmpty().When(x => string.IsNullOrWhiteSpace(x.NameEn)).WithErrorCode("validation.atLeastOneLanguage");
        RuleFor(x => x.NameAr).MaximumLength(OrgLevel.NameMaxLength);
        RuleFor(x => x.NameEn).MaximumLength(OrgLevel.NameMaxLength);
        RuleFor(x => x.Sort).InclusiveBetween(0, 10_000);
    }
}

internal sealed class CreateLevelHandler(OrgDbContext db) : ICommandHandler<CreateLevel, OrgLevelDto>
{
    public async Task<Result<OrgLevelDto>> HandleAsync(CreateLevel command, CancellationToken cancellationToken)
    {
        var code = command.Code!.Trim();
        if (await db.Levels.AnyAsync(l => l.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return OrgErrors.DuplicateLevelCode;
        }

        var level = OrgLevel.Create(code, command.NameAr, command.NameEn, command.Sort);
        db.Levels.Add(level);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OrgLevelDto.From(level);
    }
}

public sealed record UpdateLevel(Guid Id, string? NameAr, string? NameEn, int Sort, bool Active, uint Version) : ICommand<OrgLevelDto>;

public sealed class UpdateLevelValidator : AbstractValidator<UpdateLevel>
{
    public UpdateLevelValidator()
    {
        RuleFor(x => x.NameAr).NotEmpty().When(x => string.IsNullOrWhiteSpace(x.NameEn)).WithErrorCode("validation.atLeastOneLanguage");
        RuleFor(x => x.NameAr).MaximumLength(OrgLevel.NameMaxLength);
        RuleFor(x => x.NameEn).MaximumLength(OrgLevel.NameMaxLength);
        RuleFor(x => x.Sort).InclusiveBetween(0, 10_000);
    }
}

internal sealed class UpdateLevelHandler(OrgDbContext db) : ICommandHandler<UpdateLevel, OrgLevelDto>
{
    public async Task<Result<OrgLevelDto>> HandleAsync(UpdateLevel command, CancellationToken cancellationToken)
    {
        var level = await db.Levels.FirstOrDefaultAsync(l => l.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (level is null)
        {
            return Error.NotFound();
        }

        if (!command.Active && level.Active
            && await db.Units.AnyAsync(u => u.LevelCode == level.Code && u.Active, cancellationToken).ConfigureAwait(false))
        {
            return Error.Rule("org.level.inUse", "org.errors.levelInUse");
        }

        db.Entry(level).Property(x => x.Version).OriginalValue = command.Version;
        level.Update(command.NameAr, command.NameEn, command.Sort, command.Active);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return OrgLevelDto.From(level);
    }
}

public sealed record GetOrgSettings : IQuery<OrgSettingsDto>;

internal sealed class GetOrgSettingsHandler(OrgDbContext db) : IQueryHandler<GetOrgSettings, OrgSettingsDto>
{
    public async Task<Result<OrgSettingsDto>> HandleAsync(GetOrgSettings query, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.AsNoTracking().FirstAsync(cancellationToken).ConfigureAwait(false);
        return new OrgSettingsDto(settings.EnforceLevelOrder, settings.Version);
    }
}

public sealed record UpdateOrgSettings(bool EnforceLevelOrder, uint Version) : ICommand<OrgSettingsDto>;

internal sealed class UpdateOrgSettingsHandler(OrgDbContext db) : ICommandHandler<UpdateOrgSettings, OrgSettingsDto>
{
    public async Task<Result<OrgSettingsDto>> HandleAsync(UpdateOrgSettings command, CancellationToken cancellationToken)
    {
        var settings = await db.Settings.FirstAsync(cancellationToken).ConfigureAwait(false);
        db.Entry(settings).Property(x => x.Version).OriginalValue = command.Version;
        settings.EnforceLevelOrder = command.EnforceLevelOrder;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return new OrgSettingsDto(settings.EnforceLevelOrder, settings.Version);
    }
}
