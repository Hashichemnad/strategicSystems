using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Modules.Kpi.Application;

public sealed record KpiElementDto(
    Guid Id, string Kind, string Code, string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, Guid? ParentId, decimal? Weight, int Sort, bool Active,
    int? CycleStartYear, int? CycleEndYear, int CardCount, uint Version);

public sealed record KpiElementInput(
    Guid? Id, string Kind, string Code, string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, Guid? ParentId, decimal? Weight, int Sort, bool Active,
    int? CycleStartYear, int? CycleEndYear, uint? Version);

public sealed record ListKpiElements : IQuery<IReadOnlyList<KpiElementDto>>;

public sealed record SaveKpiElement(KpiElementInput Input) : ICommand<KpiElementDto>;

/// <summary>Strategy hierarchy (SPEC §2.7, ADR 0062): read with <c>kpi.card.read</c>, edited with <c>kpi.strategy.manage</c>.</summary>
internal sealed class StrategyElementHandlers(KpiDbContext db, KpiReader reader, IRequestContext context)
    : IQueryHandler<ListKpiElements, IReadOnlyList<KpiElementDto>>, ICommandHandler<SaveKpiElement, KpiElementDto>
{
    public async Task<Result<IReadOnlyList<KpiElementDto>>> HandleAsync(ListKpiElements query, CancellationToken cancellationToken)
    {
        var counts = await db.Cards.AsNoTracking().Where(c => c.ObjectiveId != null).GroupBy(c => c.ObjectiveId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
        var elements = await db.Elements.AsNoTracking().OrderBy(e => e.Sort).ThenBy(e => e.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        return elements.Select(e => ToDto(e, counts.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<Result<KpiElementDto>> HandleAsync(SaveKpiElement command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, KpiPermissions.StrategyManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var input = command.Input;
        var errors = new List<FieldError>();
        void Add(string field, string key) => errors.Add(new FieldError(field, key, key));
        if (!ElementKinds.All.Contains(input.Kind))
        {
            Add("kind", "validation.invalidValue");
        }

        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 100)
        {
            Add("code", "validation.notEmpty");
        }
        else if (await db.Elements.AnyAsync(e => e.Code == input.Code.Trim() && e.Id != input.Id, cancellationToken).ConfigureAwait(false))
        {
            Add("code", "validation.duplicate");
        }

        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            Add("nameEn", "validation.oneLanguageRequired");
        }

        var parentKind = ElementKinds.ParentKindOf(input.Kind);
        if (input.ParentId is { } parentId)
        {
            var parent = await db.Elements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == parentId, cancellationToken).ConfigureAwait(false);
            if (parent is null || parent.Kind != parentKind || parentId == input.Id)
            {
                Add("parentId", "kpi.errors.parentKind");
            }
        }
        else if (input.Kind == ElementKinds.Objective)
        {
            Add("parentId", "kpi.errors.parentKind");
        }

        if (input.Weight is < 0 or > 1000)
        {
            Add("weight", "validation.invalidValue");
        }

        if (input.CycleStartYear is { } s && input.CycleEndYear is { } e && e < s)
        {
            Add("cycleEndYear", "validation.invalidValue");
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        StrategyElement element;
        if (input.Id is { } id)
        {
            var existing = await db.Elements.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return Error.NotFound();
            }

            if (existing.Kind != input.Kind)
            {
                return Error.Validation([new FieldError("kind", "kpi.errors.kindFixed", "kpi.errors.kindFixed")]);
            }

            element = existing;
            db.Entry(element).Property(x => x.Version).OriginalValue = input.Version ?? element.Version;
        }
        else
        {
            element = StrategyElement.Create(input.Kind, input.Code);
            db.Elements.Add(element);
        }

        element.Edit(input.Code, input.NameAr, input.NameEn, input.DescriptionAr, input.DescriptionEn, input.ParentId, input.Weight, input.Sort, input.Active, input.CycleStartYear,
            input.CycleEndYear);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var count = await db.Cards.CountAsync(c => c.ObjectiveId == element.Id, cancellationToken).ConfigureAwait(false);
        return ToDto(element, count);
    }

    private static KpiElementDto ToDto(StrategyElement e, int count) => new(e.Id, e.Kind, e.Code, e.NameAr, e.NameEn, e.DescriptionAr, e.DescriptionEn, e.ParentId, e.Weight,
        e.Sort, e.Active, e.CycleStartYear, e.CycleEndYear, count, e.Version);
}
