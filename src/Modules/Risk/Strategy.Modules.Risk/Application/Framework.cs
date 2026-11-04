using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Risk.Contracts;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Persistence;

namespace Strategy.Modules.Risk.Application;

public sealed record FrameworkVersionDto(Guid Id, int VersionNo, FrameworkContent Content, string? Reason, string Recalculation, DateTimeOffset CreatedAt, Guid? CreatedBy);

public sealed record FrameworkSummaryDto(Guid Id, int VersionNo, string ScoringMethod, string? Formula, string? Reason, string Recalculation, DateTimeOffset CreatedAt);

public sealed record CategoryDto(Guid Id, string Code, string? NameAr, string? NameEn, Guid? ParentId, decimal? Appetite, int Sort, bool Active, uint Version);

public sealed record CategoryInput(string Code, string? NameAr, string? NameEn, Guid? ParentId, decimal? Appetite, int Sort, bool Active, uint? Version);

public sealed record FrameworkInput(FrameworkContent Content, string? Reason, string Recalculation);

public sealed record AffectedRiskDto(Guid Id, string Number, string? TitleAr, string? TitleEn, string Kind, string FromBand, string ToBand);

public sealed record FrameworkPreviewDto(
    bool Valid, IReadOnlyList<FieldError> Errors, IReadOnlyList<MatrixCell> Matrix, int OpenRisks, int AffectedRisks, int AffectedProjectRisks,
    IReadOnlyList<AffectedRiskDto> Examples);

public sealed record GetRiskFramework : IQuery<FrameworkVersionDto>;

public sealed record ListRiskFrameworkVersions : IQuery<IReadOnlyList<FrameworkSummaryDto>>;

public sealed record GetRiskFrameworkVersion(int VersionNo) : IQuery<FrameworkVersionDto>;

public sealed record PreviewRiskFramework(FrameworkContent Content) : IQuery<FrameworkPreviewDto>;

public sealed record SaveRiskFramework(FrameworkInput Input) : ICommand<FrameworkVersionDto>;

public sealed record ListRiskCategories(bool IncludeInactive) : IQuery<IReadOnlyList<CategoryDto>>;

public sealed record SaveRiskCategory(Guid? Id, CategoryInput Input) : ICommand<CategoryDto>;

/// <summary>The current framework and its scorer (one per request scope).</summary>
internal sealed class FrameworkStore(RiskDbContext db)
{
    private FrameworkVersion? _current;
    private RiskScorer? _scorer;
    private readonly Dictionary<Guid, (FrameworkVersion Version, RiskScorer Scorer)> _versions = [];

    public async Task<FrameworkVersion> CurrentAsync(CancellationToken ct)
    {
        if (_current is not null)
        {
            return _current;
        }

        _current = await db.Frameworks.AsNoTracking().OrderByDescending(f => f.VersionNo).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? FrameworkVersion.Create(0, DefaultFramework.Content, null, RiskRecalculation.Now);
        return _current;
    }

    public async Task<RiskScorer> ScorerAsync(CancellationToken ct) => _scorer ??= new RiskScorer((await CurrentAsync(ct).ConfigureAwait(false)).Read());

    /// <summary>The version a risk is rated on (ADR 0100 <c>next-review</c>) and its scorer; the current one when unknown.</summary>
    public async Task<(FrameworkVersion Version, RiskScorer Scorer)> ForAsync(Guid versionId, CancellationToken ct)
    {
        var current = await CurrentAsync(ct).ConfigureAwait(false);
        if (versionId == Guid.Empty || versionId == current.Id)
        {
            return (current, await ScorerAsync(ct).ConfigureAwait(false));
        }

        if (_versions.TryGetValue(versionId, out var cached))
        {
            return cached;
        }

        var version = await db.Frameworks.AsNoTracking().FirstOrDefaultAsync(f => f.Id == versionId, ct).ConfigureAwait(false);
        var result = version is null ? (current, await ScorerAsync(ct).ConfigureAwait(false)) : (version, new RiskScorer(version.Read()));
        _versions[versionId] = result;
        return result;
    }

    public void Reset()
    {
        _current = null;
        _scorer = null;
    }
}

/// <summary>Computes the ratings of a risk (design §5.4) and escalation; used by every write path. Pure apart from loading categories / controls.</summary>
internal sealed class RiskRater(RiskDbContext db, FrameworkStore frameworks)
{
    private List<Category>? _categories;

    public async Task<RiskRatings> ComputeAsync(RiskRecord risk, RiskScorer scorer, Guid versionId, IReadOnlyList<string?>? controlRatings, CancellationToken ct)
    {
        controlRatings ??= await db.Controls.AsNoTracking().Where(c => c.RiskId == risk.Id && c.Status == ControlStatuses.Active).Select(c => c.EffectivenessCode)
            .ToListAsync(ct).ConfigureAwait(false);
        var appetite = await AppetiteAsync(risk.CategoryCodes, ct).ConfigureAwait(false);
        return Compute(risk, scorer, versionId, controlRatings, appetite);
    }

    /// <summary>Pure computation (unit-tested).</summary>
    public static RiskRatings Compute(RiskRecord risk, RiskScorer scorer, Guid versionId, IReadOnlyList<string?> controlRatings, decimal? appetite)
    {
        var inherent = scorer.Score(risk.InherentLikelihood, risk.InherentImpact, 0);
        var computedControl = scorer.OverallEffectiveness(controlRatings);
        var control = risk.ControlEffectivenessOverride ?? computedControl;
        var factor = scorer.Factor(control) ?? 0m;
        var residual = scorer.Score(risk.ResidualLikelihood ?? risk.InherentLikelihood, risk.ResidualImpact ?? risk.InherentImpact, factor);
        var residualBand = scorer.Band(residual);
        decimal? target = risk.TargetLikelihood is { } tl && risk.TargetImpact is { } ti ? scorer.Score(tl, ti, factor) : null;
        return new RiskRatings(inherent, scorer.Band(inherent).Code, computedControl, residual, residualBand.Code, scorer.ResidualStatusOf(residualBand.Code), target,
            target is { } t ? scorer.Band(t).Code : null, appetite is { } a ? residual <= a : null, versionId);
    }

    /// <summary>Applies ratings with the risk's framework version (or the current one) and the escalation rule (R4).</summary>
    public async Task<bool> RateAsync(RiskRecord risk, RiskSettings settings, DateTimeOffset now, CancellationToken ct, bool moveToCurrent = false, IReadOnlyList<string?>? controlRatings = null)
    {
        var (version, scorer) = moveToCurrent
            ? (await frameworks.CurrentAsync(ct).ConfigureAwait(false), await frameworks.ScorerAsync(ct).ConfigureAwait(false))
            : await frameworks.ForAsync(risk.FrameworkVersionId, ct).ConfigureAwait(false);
        var ratings = await ComputeAsync(risk, scorer, version.Id, controlRatings, ct).ConfigureAwait(false);
        var changed = risk.ApplyRatings(ratings);
        ApplyEscalation(risk, settings, scorer, now);
        return changed;
    }

    public static void ApplyEscalation(RiskRecord risk, RiskSettings settings, RiskScorer scorer, DateTimeOffset now)
    {
        var highest = scorer.Content.Bands.OrderBy(b => b.Min).Last().Code;
        if (settings.EscalateCritical && risk.ResidualBand == highest && risk.Status != RiskStatuses.Closed)
        {
            risk.Escalate(EscalationReasons.Critical, now);
        }
        else if (settings.EscalateOutsideAppetite && risk.WithinAppetite == false && risk.Status != RiskStatuses.Closed)
        {
            risk.Escalate(EscalationReasons.OutsideAppetite, now);
        }
        else if (risk.Escalated && risk.EscalationReason != EscalationReasons.Manual)
        {
            risk.ClearEscalation();
        }
    }

    /// <summary>The smallest appetite among the categories and their ancestors that have one (Q-R3: none → null).</summary>
    public async Task<decimal?> AppetiteAsync(IReadOnlyList<string> codes, CancellationToken ct)
    {
        _categories ??= await db.Categories.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return Appetite(_categories, codes);
    }

    public static decimal? Appetite(IReadOnlyList<Category> categories, IReadOnlyList<string> codes)
    {
        var byId = categories.ToDictionary(c => c.Id);
        decimal? result = null;
        foreach (var code in codes)
        {
            var category = categories.FirstOrDefault(c => c.Code == code);
            for (var depth = 0; category is not null && depth < 10; depth++)
            {
                if (category.Appetite is { } a && (result is null || a < result))
                {
                    result = a;
                }

                category = category.ParentId is { } p ? byId.GetValueOrDefault(p) : null;
            }
        }

        return result;
    }
}

internal sealed class FrameworkQueries(RiskDbContext db, FrameworkStore store)
    : IQueryHandler<GetRiskFramework, FrameworkVersionDto>, IQueryHandler<ListRiskFrameworkVersions, IReadOnlyList<FrameworkSummaryDto>>,
        IQueryHandler<GetRiskFrameworkVersion, FrameworkVersionDto>, IQueryHandler<ListRiskCategories, IReadOnlyList<CategoryDto>>
{
    public async Task<Result<FrameworkVersionDto>> HandleAsync(GetRiskFramework query, CancellationToken cancellationToken) =>
        ToDto(await store.CurrentAsync(cancellationToken).ConfigureAwait(false));

    public async Task<Result<IReadOnlyList<FrameworkSummaryDto>>> HandleAsync(ListRiskFrameworkVersions query, CancellationToken cancellationToken)
    {
        var versions = await db.Frameworks.AsNoTracking().OrderByDescending(f => f.VersionNo).ToListAsync(cancellationToken).ConfigureAwait(false);
        return versions.Select(v =>
        {
            var c = v.Read();
            return new FrameworkSummaryDto(v.Id, v.VersionNo, c.ScoringMethod, c.Formula, v.Reason, v.Recalculation, v.CreatedAt);
        }).ToList();
    }

    public async Task<Result<FrameworkVersionDto>> HandleAsync(GetRiskFrameworkVersion query, CancellationToken cancellationToken) =>
        await db.Frameworks.AsNoTracking().FirstOrDefaultAsync(f => f.VersionNo == query.VersionNo, cancellationToken).ConfigureAwait(false) is { } v
            ? ToDto(v)
            : Error.NotFound();

    public async Task<Result<IReadOnlyList<CategoryDto>>> HandleAsync(ListRiskCategories query, CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking().Where(c => query.IncludeInactive || c.Active).OrderBy(c => c.Sort).ThenBy(c => c.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return categories.Select(ToDto).ToList();
    }

    public static FrameworkVersionDto ToDto(FrameworkVersion v) => new(v.Id, v.VersionNo, v.Read(), v.Reason, v.Recalculation, v.CreatedAt, v.CreatedBy);

    public static CategoryDto ToDto(Category c) => new(c.Id, c.Code, c.NameAr, c.NameEn, c.ParentId, c.Appetite, c.Sort, c.Active, c.Version);
}

/// <summary>Preview, save (new version + recalculation, ADR 0100) and categories.</summary>
internal sealed class FrameworkCommands(
    RiskDbContext db, FrameworkStore store, RiskRater rater, RiskReader reader, IIntegrationEventPublisher events, IClock clock, IRequestContext context,
    IServiceProvider services)
    : IQueryHandler<PreviewRiskFramework, FrameworkPreviewDto>, ICommandHandler<SaveRiskFramework, FrameworkVersionDto>, ICommandHandler<SaveRiskCategory, CategoryDto>
{
    public async Task<Result<FrameworkPreviewDto>> HandleAsync(PreviewRiskFramework query, CancellationToken cancellationToken)
    {
        var errors = FrameworkRules.Validate(query.Content);
        if (errors.Count > 0)
        {
            return new FrameworkPreviewDto(false, errors, [], 0, 0, 0, []);
        }

        var scorer = new RiskScorer(query.Content);
        var open = await db.Risks.AsNoTracking().Where(r => RiskStatuses.Open.Contains(r.Status)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var affected = new List<AffectedRiskDto>();
        foreach (var risk in open)
        {
            var ratings = await rater.ComputeAsync(risk, scorer, Guid.Empty, null, cancellationToken).ConfigureAwait(false);
            if (ratings.InherentBand != risk.InherentBand || ratings.ResidualBand != risk.ResidualBand || ratings.TargetBand != risk.TargetBand)
            {
                affected.Add(new AffectedRiskDto(risk.Id, risk.Number, risk.TitleAr, risk.TitleEn, "enterprise", risk.ResidualBand, ratings.ResidualBand));
            }
        }

        var projectAffected = 0;
        if (services.GetService<Portfolio.Contracts.IProjectRiskDirectory>() is { } projects
            && await reader.CanAsync(context.User, RiskPermissions.ProjectRisksRead, cancellationToken).ConfigureAwait(false))
        {
            foreach (var p in await projects.ListAsync(context.User, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                var (_, inherentBand, _, residualBand) = scorer.ScoreProject(p.Probability, p.Impact, p.ControlEvaluation);
                if (inherentBand.Code != p.InherentBand || residualBand.Code != p.ResidualBand)
                {
                    projectAffected++;
                }
            }
        }

        return new FrameworkPreviewDto(true, [], scorer.Matrix(), open.Count, affected.Count, projectAffected, affected.Take(20).ToList());
    }

    public async Task<Result<FrameworkVersionDto>> HandleAsync(SaveRiskFramework command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var errors = FrameworkRules.Validate(input.Content);
        if (!RiskRecalculation.All.Contains(input.Recalculation))
        {
            errors.Add(new FieldError("recalculation", "validation.invalid", "validation.invalid"));
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            errors.Add(new FieldError("reason", "validation.required", "validation.required"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var last = await db.Frameworks.AsNoTracking().MaxAsync(f => (int?)f.VersionNo, cancellationToken).ConfigureAwait(false) ?? 0;
        var version = FrameworkVersion.Create(last + 1, input.Content, input.Reason!.Trim(), input.Recalculation);
        db.Frameworks.Add(version);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        store.Reset();
        if (input.Recalculation == RiskRecalculation.Now)
        {
            await RecalculateAllAsync(db, rater, store, reader, clock, context.User.UserId(), cancellationToken).ConfigureAwait(false);
        }

        await events.PublishAsync(new RiskFrameworkChanged(version.Id, version.VersionNo, version.Recalculation, clock.UtcNow), db, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FrameworkQueries.ToDto(version);
    }

    /// <summary>Re-rates every open risk on the current version; each changed risk gets its audit entry (EF) and a snapshot.</summary>
    public static async Task<int> RecalculateAllAsync(RiskDbContext db, RiskRater rater, FrameworkStore store, RiskReader reader, IClock clock, Guid? by, CancellationToken ct)
    {
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var current = await store.CurrentAsync(ct).ConfigureAwait(false);
        var risks = await db.Risks.Where(r => RiskStatuses.Open.Contains(r.Status)).ToListAsync(ct).ConfigureAwait(false);
        var changed = 0;
        foreach (var risk in risks)
        {
            if (await rater.RateAsync(risk, settings, clock.UtcNow, ct, moveToCurrent: true).ConfigureAwait(false))
            {
                changed++;
                db.Assessments.Add(Assessment.Of(risk, AssessmentKinds.Recalculation, current.VersionNo, $"framework v{current.VersionNo}", clock.UtcNow, by));
                if (risk.Status == RiskStatuses.Active)
                {
                    risk.FlagReassessment(ReassessmentReasons.Framework);
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return changed;
    }

    public async Task<Result<CategoryDto>> HandleAsync(SaveRiskCategory command, CancellationToken cancellationToken)
    {
        var v = command.Input;
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.Code) || v.Code.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(v.Code, "^[a-z0-9][a-z0-9-]*$"))
        {
            errors.Add(new FieldError("code", "validation.invalid", "validation.invalid"));
        }

        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        if (v.Appetite is < 0 or > 1000)
        {
            errors.Add(new FieldError("appetite", "validation.invalid", "validation.invalid"));
        }

        if (v.ParentId is { } parent && (parent == command.Id || !await db.Categories.AnyAsync(c => c.Id == parent, cancellationToken).ConfigureAwait(false)))
        {
            errors.Add(new FieldError("parentId", "validation.invalid", "validation.invalid"));
        }

        if (await db.Categories.AnyAsync(c => c.Code == v.Code && c.Id != command.Id, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new FieldError("code", "validation.duplicate", "validation.duplicate"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        Category category;
        if (command.Id is { } id)
        {
            category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException();
            if (category.Code != v.Code)
            {
                return Error.Rule("risk.categoryCodeFixed", "risk.errors.categoryCodeFixed");
            }

            db.Entry(category).Property(c => c.Version).OriginalValue = v.Version ?? category.Version;
        }
        else
        {
            category = Category.Create(v.Code);
            db.Categories.Add(category);
        }

        category.Update(v.NameAr?.Trim(), v.NameEn?.Trim(), v.ParentId, v.Appetite, v.Sort, v.Active);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FrameworkQueries.ToDto(category);
    }
}

/// <summary>Seeds the framework (version 1) and categories once (Q-R3: appetite empty); publishes <c>RiskFrameworkChanged</c> for version 1.</summary>
internal sealed class RiskSeeder : IDatabaseSeeder
{
    public int Order => 520;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<RiskDbContext>();
        var clock = services.GetRequiredService<IClock>();
        services.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        var codes = await db.Categories.Select(c => c.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        var sort = 10;
        foreach (var (code, ar, en) in new[]
                 {
                     ("strategic", "استراتيجية", "Strategic"), ("operational", "تشغيلية", "Operational"), ("financial", "مالية", "Financial"),
                     ("legal-compliance", "قانونية والتزام", "Legal & Compliance"), ("technical", "فنية", "Technical"),
                     ("it-security", "تقنية المعلومات وأمن المعلومات", "IT & Information Security"), ("reputation", "السمعة", "Reputation"),
                     ("health-safety", "الصحة والسلامة", "Health & Safety"), ("environmental", "بيئية", "Environmental"),
                 })
        {
            if (!codes.Contains(code))
            {
                var category = Category.Create(code);
                category.Update(ar, en, null, null, sort, true);
                db.Categories.Add(category);
            }

            sort += 10;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await db.Frameworks.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var version = FrameworkVersion.Create(1, DefaultFramework.Content, "SPEC §3.3 defaults", RiskRecalculation.Now);
            db.Frameworks.Add(version);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await services.GetRequiredService<IIntegrationEventPublisher>()
                .PublishAsync(new RiskFrameworkChanged(version.Id, 1, RiskRecalculation.Now, clock.UtcNow), db, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
