using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Benchmarking.Domain;
using Strategy.Modules.Benchmarking.Infrastructure;
using Strategy.Modules.Cap.Contracts;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Modules.Benchmarking.Application;

public sealed record BenchComparatorStudyDto(Guid StudyId, string Number, string? TitleAr, string? TitleEn, int TargetYear, string Stage, bool Selected, int? Rating);

public sealed record BenchComparatorDto(Guid Id, ComparatorValues Values, int Studies, decimal? AverageRating, IReadOnlyList<BenchComparatorStudyDto> History, uint Version);

public sealed record BenchPracticeValues(
    string? TitleAr, string? TitleEn, string? Description, string? Topic, IReadOnlyList<string>? Tags, string? Applicability, string? SectorCode, bool Active);

public sealed record BenchBestPracticeDto(
    Guid Id, BenchPracticeValues Values, string Origin, Guid? StudyId, string? StudyNumber, Guid? ComparatorId, string? ComparatorNameAr, string? ComparatorNameEn, double Score,
    uint Version);

public sealed record BenchCountDto(string Code, int Count);

public sealed record BenchUnitCountDto(BenchUnitDto? Unit, int Studies, int Completed);

public sealed record BenchDashboardDto(
    int Studies, int Active, int Completed, int Cancelled, IReadOnlyList<BenchCountDto> ByStage, IReadOnlyList<BenchCountDto> ByQuarter, IReadOnlyList<BenchUnitCountDto> ByUnit,
    int Actions, int ActionsCompleted, int ActionsLate, int PracticesAdopted, int Comparators);

public sealed record BenchComparatorBody(ComparatorValues Values, uint? Version);

public sealed record BenchPracticeBody(BenchPracticeValues Values, uint? Version);

public sealed record ListComparators(PageRequest Page, string? Search, string? Scope, string? Sector, bool IncludeInactive) : IQuery<PagedResult<BenchComparatorDto>>;

public sealed record GetComparator(Guid Id) : IQuery<BenchComparatorDto>;

public sealed record SaveComparator(Guid? Id, ComparatorValues Values, uint? Version) : ICommand<BenchComparatorDto>;

public sealed record SearchBestPractices(PageRequest Page, string? Search, string? Topic, string? Sector, IReadOnlyList<string>? Keywords = null) : IQuery<PagedResult<BenchBestPracticeDto>>;

public sealed record SaveBestPractice(Guid? Id, BenchPracticeValues Values, uint? Version) : ICommand<BenchBestPracticeDto>;

public sealed record GetBenchDashboard(int? Year, Guid? UnitId) : IQuery<BenchDashboardDto>;

public sealed record GetBenchSettings : IQuery<SettingsDocument<BenchSettings>>;

public sealed record SaveBenchSettings(BenchSettings Settings, uint Version) : ICommand<SettingsDocument<BenchSettings>>;

/// <summary>Validation of register and library entries.</summary>
internal static class LibraryRules
{
    public static List<FieldError> Comparator(ComparatorValues? v)
    {
        var errors = new List<FieldError>();
        if (v is null)
        {
            errors.Add(new FieldError("values", "validation.required", "validation.required"));
            return errors;
        }

        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (!ComparatorScopes.All.Contains(v.Scope))
        {
            errors.Add(new FieldError("scope", "validation.invalid", "validation.invalid"));
        }

        if ((v.NameAr?.Length ?? 0) > Study.TitleMaxLength || (v.NameEn?.Length ?? 0) > Study.TitleMaxLength || (v.Notes?.Length ?? 0) > Study.TextMaxLength
            || (v.Website?.Length ?? 0) > 500 || (v.ContactEmail?.Length ?? 0) > 200 || (v.ContactName?.Length ?? 0) > 200 || (v.ContactPhone?.Length ?? 0) > 60
            || (v.Country?.Length ?? 0) > 100)
        {
            errors.Add(new FieldError("nameEn", "validation.maximumLength", "validation.maximumLength"));
        }

        return errors;
    }

    public static List<FieldError> Practice(BenchPracticeValues? v)
    {
        var errors = new List<FieldError>();
        if (v is null)
        {
            errors.Add(new FieldError("values", "validation.required", "validation.required"));
            return errors;
        }

        if (string.IsNullOrWhiteSpace(v.TitleAr) && string.IsNullOrWhiteSpace(v.TitleEn))
        {
            errors.Add(new FieldError("titleEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if ((v.Description?.Length ?? 0) > Study.TextMaxLength || (v.TitleAr?.Length ?? 0) > Study.TitleMaxLength || (v.TitleEn?.Length ?? 0) > Study.TitleMaxLength)
        {
            errors.Add(new FieldError("description", "validation.maximumLength", "validation.maximumLength"));
        }

        return errors;
    }
}

internal sealed class LibraryHandlers(BenchDbContext db, BenchReader reader, ILookupService lookups, IRequestContext context, ISettingsEditor editor, IServiceProvider services)
    : IQueryHandler<ListComparators, PagedResult<BenchComparatorDto>>, IQueryHandler<GetComparator, BenchComparatorDto>, ICommandHandler<SaveComparator, BenchComparatorDto>,
        IQueryHandler<SearchBestPractices, PagedResult<BenchBestPracticeDto>>, ICommandHandler<SaveBestPractice, BenchBestPracticeDto>,
        IQueryHandler<GetBenchDashboard, BenchDashboardDto>, IQueryHandler<GetBenchSettings, SettingsDocument<BenchSettings>>,
        ICommandHandler<SaveBenchSettings, SettingsDocument<BenchSettings>>
{
    public async Task<Result<PagedResult<BenchComparatorDto>>> HandleAsync(ListComparators query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.ComparatorRead, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var source = db.Comparators.AsNoTracking();
        if (!query.IncludeInactive)
        {
            source = source.Where(c => c.Active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(c => EF.Functions.ILike(c.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(c.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(c.Country ?? string.Empty, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.Scope))
        {
            source = source.Where(c => c.Scope == query.Scope);
        }

        if (!string.IsNullOrWhiteSpace(query.Sector))
        {
            source = source.Where(c => c.SectorCode == query.Sector);
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await source.OrderBy(c => c.NameEn ?? c.NameAr).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<BenchComparatorDto>();
        foreach (var c in rows)
        {
            items.Add(await ToDtoAsync(c, false, cancellationToken).ConfigureAwait(false));
        }

        return new PagedResult<BenchComparatorDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<Result<BenchComparatorDto>> HandleAsync(GetComparator query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.ComparatorRead, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var c = await db.Comparators.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken).ConfigureAwait(false);
        return c is null ? Error.NotFound() : await ToDtoAsync(c, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<BenchComparatorDto>> HandleAsync(SaveComparator command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.ComparatorManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = LibraryRules.Comparator(command.Values);
        if (command.Values?.SectorCode is { } sector && !await lookups.IsActiveCodeAsync(BenchPermissions.SectorLookup, sector, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new FieldError("sectorCode", "validation.invalid", "validation.invalid"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        Comparator comparator;
        if (command.Id is { } id)
        {
            var found = await db.Comparators.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            comparator = found;
            db.Entry(comparator).Property(x => x.Version).OriginalValue = command.Version ?? 0;
            comparator.Update(command.Values);
        }
        else
        {
            comparator = Comparator.Create(command.Values);
            db.Comparators.Add(comparator);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(comparator, true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Library search (SPEC §8.3): text in titles, description, topic and tags, ranked by trigram similarity; keywords from the AI search are OR-ed.</summary>
    public async Task<Result<PagedResult<BenchBestPracticeDto>>> HandleAsync(SearchBestPractices query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.PracticeRead, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var source = db.BestPractices.AsNoTracking().Where(b => b.Active);
        var terms = (query.Keywords ?? []).Concat(string.IsNullOrWhiteSpace(query.Search) ? [] : [query.Search]).Where(t => !string.IsNullOrWhiteSpace(t)).Take(8).ToList();
        if (terms.Count > 0)
        {
            // Any of the terms (keywords of a natural-language question are OR-ed): matching ids per term, then one filtered query.
            var matching = new HashSet<Guid>();
            foreach (var p in terms.Select(Like.Pattern))
            {
                matching.UnionWith(await source.Where(b => EF.Functions.ILike(b.TitleAr ?? string.Empty, p, "\\") || EF.Functions.ILike(b.TitleEn ?? string.Empty, p, "\\")
                        || EF.Functions.ILike(b.Description ?? string.Empty, p, "\\") || EF.Functions.ILike(b.Topic ?? string.Empty, p, "\\")
                        || EF.Functions.ILike(b.TagsJson, p, "\\"))
                    .Select(b => b.Id).Take(500).ToListAsync(cancellationToken).ConfigureAwait(false));
            }

            source = source.Where(b => matching.Contains(b.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.Topic))
        {
            var pattern = Like.Pattern(query.Topic);
            source = source.Where(b => EF.Functions.ILike(b.Topic ?? string.Empty, pattern, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(query.Sector))
        {
            source = source.Where(b => b.SectorCode == query.Sector);
        }

        var page = query.Page.Normalised();
        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        var text = string.Join(' ', terms);
        var ranked = text.Length == 0
            ? await source.OrderByDescending(b => b.CreatedAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).Select(b => new { B = b, Score = 1d })
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : await source.Select(b => new
                {
                    B = b,
                    Score = Math.Max(EF.Functions.TrigramsSimilarity(b.TitleEn ?? string.Empty, text), EF.Functions.TrigramsSimilarity(b.TitleAr ?? string.Empty, text)),
                })
                .OrderByDescending(x => x.Score).ThenByDescending(x => x.B.CreatedAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        var studyIds = ranked.Where(r => r.B.StudyId != null).Select(r => r.B.StudyId!.Value).Distinct().ToList();
        var studies = await db.Studies.AsNoTracking().Where(s => studyIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Number, cancellationToken).ConfigureAwait(false);
        var comparatorIds = ranked.Where(r => r.B.ComparatorId != null).Select(r => r.B.ComparatorId!.Value).Distinct().ToList();
        var comparators = await db.Comparators.AsNoTracking().Where(c => comparatorIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        return new PagedResult<BenchBestPracticeDto>(ranked.Select(r => ToDto(r.B, r.Score, studies, comparators)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<BenchBestPracticeDto>> HandleAsync(SaveBestPractice command, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.PracticeManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var errors = LibraryRules.Practice(command.Values);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        BestPractice entry;
        if (command.Id is { } id)
        {
            var found = await db.BestPractices.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            entry = found;
            db.Entry(entry).Property(x => x.Version).OriginalValue = command.Version ?? 0;
        }
        else
        {
            entry = BestPractice.Create("manual", null, null, null);
            db.BestPractices.Add(entry);
        }

        var v = command.Values;
        entry.Update(v.TitleAr, v.TitleEn, v.Description, v.Topic, v.Tags ?? [], v.Applicability, v.SectorCode, v.Active);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, 1, new Dictionary<Guid, string>(), new Dictionary<Guid, Comparator>());
    }

    /// <summary>Dashboard (SPEC §8.5): studies by stage / quarter / unit, actions completion and late actions (CAP), practices adopted.</summary>
    public async Task<Result<BenchDashboardDto>> HandleAsync(GetBenchDashboard query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, BenchPermissions.DashboardRead, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var source = await reader.StudiesAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (query.Year is { } year)
        {
            source = source.Where(s => s.TargetYear == year);
        }

        if (query.UnitId is { } unitId)
        {
            var units = await reader.UnitAndBelowAsync(unitId, cancellationToken).ConfigureAwait(false);
            source = source.Where(s => units.Contains(s.ResponsibleUnitId));
        }

        var studies = await source.ToListAsync(cancellationToken).ConfigureAwait(false);
        int actions = 0, completed = 0, late = 0;
        if (services.GetService(typeof(ICapRemarkSources)) is ICapRemarkSources cap)
        {
            foreach (var s in studies.Where(s => s.AnalysisApproved))
            {
                if (await cap.GetProgressAsync(Study.EntityType, s.Id, cancellationToken).ConfigureAwait(false) is { } progress)
                {
                    actions += progress.ActionCount;
                    completed += progress.CompletedCount;
                    late += progress.Remarks.SelectMany(r => r.Actions).Count(a => a.Status == "late");
                }
            }
        }

        var ids = studies.Select(s => s.Id).ToList();
        var practices = await db.BestPractices.CountAsync(b => b.StudyId != null && ids.Contains(b.StudyId.Value), cancellationToken).ConfigureAwait(false);
        var unitsById = await reader.UnitsAsync(studies.Select(s => (Guid?)s.ResponsibleUnitId), cancellationToken).ConfigureAwait(false);
        return new BenchDashboardDto(studies.Count, studies.Count(s => s.Status == StudyStatuses.Active), studies.Count(s => s.Status == StudyStatuses.Completed),
            studies.Count(s => s.Status == StudyStatuses.Cancelled),
            StudyStages.All.Select(st => new BenchCountDto(st, studies.Count(s => s.Stage == st && s.Status != StudyStatuses.Cancelled))).ToList(),
            Enumerable.Range(1, 4).Select(q => new BenchCountDto($"q{q}", studies.Count(s => s.TargetQuarter == q))).ToList(),
            studies.GroupBy(s => s.ResponsibleUnitId).Select(g => new BenchUnitCountDto(BenchReader.Unit(g.Key, unitsById), g.Count(),
                g.Count(s => s.Status == StudyStatuses.Completed))).OrderByDescending(u => u.Studies).ToList(),
            actions, completed, late, practices, await db.Comparators.CountAsync(c => c.Active, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<SettingsDocument<BenchSettings>>> HandleAsync(GetBenchSettings query, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, BenchPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.GetDocumentAsync<BenchSettings>(cancellationToken).ConfigureAwait(false);

    public async Task<Result<SettingsDocument<BenchSettings>>> HandleAsync(SaveBenchSettings command, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, BenchPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : command.Settings is null
                ? Error.Validation([new FieldError("settings", "validation.required", "validation.required")])
                : await editor.SaveAsync(command.Settings, command.Version, BenchSettings.Validate, cancellationToken).ConfigureAwait(false);

    private async Task<BenchComparatorDto> ToDtoAsync(Comparator c, bool history, CancellationToken ct)
    {
        var links = await db.StudyEntities.AsNoTracking().Where(e => e.ComparatorId == c.Id && !e.Removed)
            .Join(db.Studies.AsNoTracking(), e => e.StudyId, s => s.Id, (e, s) => new { e, s }).ToListAsync(ct).ConfigureAwait(false);
        var ratings = links.Where(l => l.e.Rating is not null).Select(l => (decimal)l.e.Rating!.Value).ToList();
        return new BenchComparatorDto(c.Id, c.Values(), links.Count, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
            history ? links.OrderByDescending(l => l.s.TargetYear).Select(l => new BenchComparatorStudyDto(l.s.Id, l.s.Number, l.s.TitleAr, l.s.TitleEn, l.s.TargetYear, l.s.Stage,
                l.e.Selected, l.e.Rating)).ToList() : [], c.Version);
    }

    private static BenchBestPracticeDto ToDto(BestPractice b, double score, IReadOnlyDictionary<Guid, string> studies, IReadOnlyDictionary<Guid, Comparator> comparators) =>
        new(b.Id, new BenchPracticeValues(b.TitleAr, b.TitleEn, b.Description, b.Topic, b.Tags, b.Applicability, b.SectorCode, b.Active), b.Origin, b.StudyId,
            b.StudyId is { } s ? studies.GetValueOrDefault(s) : null, b.ComparatorId, b.ComparatorId is { } c ? comparators.GetValueOrDefault(c)?.NameAr : null,
            b.ComparatorId is { } d ? comparators.GetValueOrDefault(d)?.NameEn : null, score, b.Version);
}
