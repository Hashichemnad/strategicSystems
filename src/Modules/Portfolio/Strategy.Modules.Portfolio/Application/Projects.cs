using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Modules.Portfolio.Application;

public sealed record ProjectDto(
    Guid Id,
    string Number,
    string Classification,
    string? NameAr,
    string? NameEn,
    string? Description,
    string? ScopeOfWork,
    string Stage,
    string Status,
    PortfolioUnitDto? Unit,
    PortfolioUnitDto? SectionUnit,
    string? StrategicObjectiveCode,
    string? ProgramTypeCode,
    decimal? Budget,
    decimal? Year1PlannedCost,
    decimal? Year2PlannedCost,
    decimal? Year3PlannedCost,
    decimal? Year4PlannedCost,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    PortfolioPersonDto? Owner,
    PortfolioPersonDto? Requester,
    Guid RequestId,
    string? RequestNumber,
    IReadOnlyList<string> StagePath,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record ProjectListItemDto(
    Guid Id, string Number, string Classification, string? NameAr, string? NameEn, string Stage, string Status, PortfolioUnitDto? Unit, decimal? Budget, DateOnly? PlannedStart,
    DateOnly? PlannedEnd, DateTimeOffset CreatedAt, string SubState, PortfolioPersonDto? Pm, decimal Performance, decimal PlannedProgress, string ProgressStatus);

public sealed record GetProject(Guid Id) : IQuery<ProjectDto>;

public sealed record ListProjects(
    PageRequest Page, string? Classification = null, string? Stage = null, Guid? UnitId = null, string? Search = null, string? Status = null, string? View = null,
    string? ProgressStatus = null)
    : IQuery<PagedResult<ProjectListItemDto>>;

public sealed record PortfolioOptionDto(string Code, string? LabelAr, string? LabelEn);

/// <summary>Everything the request screens need to render choices and live calculations (labels are configuration).</summary>
public sealed record PortfolioOptionsDto(
    IReadOnlyList<PortfolioOptionDto> Objectives,
    IReadOnlyList<PortfolioOptionDto> Sources,
    IReadOnlyList<PortfolioOptionDto> ProgramTypes,
    IReadOnlyList<PortfolioOptionDto> DeliveryOptions,
    IReadOnlyList<PriorityCriterion> Criteria,
    IReadOnlyList<PriorityBand> Bands,
    decimal ThresholdAmount,
    string CurrencyCode,
    int StartYearsAhead,
    int EndYearsAhead,
    DateOnly Today,
    Guid? ProjectScopeTemplateFileId,
    bool AssessmentOnCreate,
    bool CanCreate,
    bool CanManageSettings,
    IReadOnlyList<PortfolioOptionDto> Themes,
    IReadOnlyList<PortfolioOptionDto> RiskCategories,
    IReadOnlyList<PortfolioOptionDto> StakeholderClasses,
    IReadOnlyList<PortfolioOptionDto> LessonTypes,
    IReadOnlyList<PortfolioOptionDto> KpiIndexClasses,
    StageTemplate ProjectTemplate,
    StageTemplate InitiativeTemplate,
    decimal ProgressTolerance);

public sealed record GetPortfolioOptions : IQuery<PortfolioOptionsDto>;

public sealed record GetPortfolioSettings : IQuery<SettingsDocument<PortfolioSettings>>;

public sealed record SavePortfolioSettings(PortfolioSettings Settings, uint Version) : ICommand<SettingsDocument<PortfolioSettings>>;

public sealed record UploadProjectScopeTemplate(string FileName, Stream Content) : ICommand<SettingsDocument<PortfolioSettings>>;

internal sealed class ProjectHandlers(PortfolioDbContext db, RequestReader reader, ProjectLoader loader, IRequestContext context, CostVisibility costs)
    : IQueryHandler<GetProject, ProjectDto>, IQueryHandler<ListProjects, PagedResult<ProjectListItemDto>>
{
    public async Task<Result<ProjectDto>> HandleAsync(GetProject query, CancellationToken cancellationToken)
    {
        var p = await (await reader.VisibleProjectsAsync(context.User, cancellationToken).ConfigureAwait(false))
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken).ConfigureAwait(false);
        if (p is null)
        {
            return Error.NotFound();
        }

        var people = await reader.PeopleAsync([p.OwnerEmployeeId, p.RequesterEmployeeId], cancellationToken).ConfigureAwait(false);
        var units = await reader.UnitsAsync([p.UnitId, p.SectionUnitId], cancellationToken).ConfigureAwait(false);
        var requestNumber = await db.Requests.AsNoTracking().Where(r => r.Id == p.RequestId).Select(r => r.Number).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var show = await costs.CanSeeAsync(context.User, p, cancellationToken).ConfigureAwait(false);
        return new ProjectDto(
            p.Id, p.Number, p.Classification, p.NameAr, p.NameEn, p.Description, p.ScopeOfWork, p.Stage, p.Status,
            p.UnitId is { } u ? units.GetValueOrDefault(u) : null, p.SectionUnitId is { } s ? units.GetValueOrDefault(s) : null, p.StrategicObjectiveCode,
            p.ProgramTypeCode, show ? p.Budget : null, show ? p.Year1PlannedCost : null, show ? p.Year2PlannedCost : null, show ? p.Year3PlannedCost : null,
            show ? p.Year4PlannedCost : null, p.PlannedStart, p.PlannedEnd,
            p.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, p.RequesterEmployeeId is { } q ? people.GetValueOrDefault(q) : null, p.RequestId, requestNumber,
            p.Classification == Classifications.Project ? ProjectStages.ProjectPath : ProjectStages.InitiativePath, p.CreatedAt, p.Version);
    }

    public async Task<Result<PagedResult<ProjectListItemDto>>> HandleAsync(ListProjects query, CancellationToken cancellationToken)
    {
        var page = query.Page.Normalised();
        var source = await loader.VisibleAsync(context.User, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(p => p.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.ProgressStatus))
        {
            source = source.Where(p => p.ProgressStatus == query.ProgressStatus);
        }

        if (query.View == "mine" && context.User.EmployeeIdOf() is { } me)
        {
            source = source.Where(p => p.PmEmployeeId == me || p.OwnerEmployeeId == me);
        }
        else if (query.View == "unclaimed")
        {
            source = source.Where(p => p.PmEmployeeId == null && p.Status == ProjectStatuses.Active);
        }

        if (!string.IsNullOrWhiteSpace(query.Classification))
        {
            source = source.Where(p => p.Classification == query.Classification);
        }

        if (!string.IsNullOrWhiteSpace(query.Stage))
        {
            source = source.Where(p => p.Stage == query.Stage);
        }

        if (query.UnitId is { } unit)
        {
            source = source.Where(p => p.UnitId == unit);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = Like.Pattern(query.Search);
            source = source.Where(p => EF.Functions.ILike(p.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(p.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(p.Number, pattern, "\\"));
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        source = page.SortOrder switch
        {
            ("number", false) => source.OrderBy(p => p.Number),
            ("number", true) => source.OrderByDescending(p => p.Number),
            ("name", false) => source.OrderBy(p => p.NameEn).ThenBy(p => p.NameAr),
            ("name", true) => source.OrderByDescending(p => p.NameEn).ThenByDescending(p => p.NameAr),
            _ => source.OrderByDescending(p => p.CreatedAt),
        };
        var projects = await source.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        var units = await reader.UnitsAsync(projects.Select(p => p.UnitId), cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(projects.Select(p => p.PmEmployeeId), cancellationToken).ConfigureAwait(false);
        var visibleCosts = await costs.VisibleSetAsync(context.User, projects.Select(p => p.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return new PagedResult<ProjectListItemDto>(projects.Select(p => new ProjectListItemDto(
            p.Id, p.Number, p.Classification, p.NameAr, p.NameEn, p.Stage, p.Status, p.UnitId is { } u ? units.GetValueOrDefault(u) : null,
            visibleCosts.Contains(p.Id) ? p.Budget : null, p.PlannedStart,
            p.PlannedEnd, p.CreatedAt, p.SubState, p.PmEmployeeId is { } pm ? people.GetValueOrDefault(pm) : null, p.Performance, p.PlannedProgress, p.ProgressStatus))
            .ToList(), page.Page, page.PageSize, total);
    }
}

/// <summary>Options for the request screens and Settings → Portfolio.</summary>
internal sealed class PortfolioSettingsHandlers(
    RequestReader reader, ILookupService lookups, ISettingsStore store, ISettingsEditor editor, IAccessService access, IRequestContext context, IFileService files,
    StrategicObjectives objectives)
    : IQueryHandler<GetPortfolioOptions, PortfolioOptionsDto>,
        IQueryHandler<GetPortfolioSettings, SettingsDocument<PortfolioSettings>>,
        ICommandHandler<SavePortfolioSettings, SettingsDocument<PortfolioSettings>>,
        ICommandHandler<UploadProjectScopeTemplate, SettingsDocument<PortfolioSettings>>
{
    public async Task<Result<PortfolioOptionsDto>> HandleAsync(GetPortfolioOptions query, CancellationToken cancellationToken)
    {
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        async Task<IReadOnlyList<PortfolioOptionDto>> Values(string type) =>
            (await lookups.GetValuesAsync(type, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(v => v.Active).OrderBy(v => v.Sort).Select(v => new PortfolioOptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();

        return new PortfolioOptionsDto(
            await objectives.OptionsAsync(cancellationToken).ConfigureAwait(false), await Values(PortfolioPermissions.SourceLookup).ConfigureAwait(false),
            await Values(PortfolioPermissions.ProgramTypeLookup).ConfigureAwait(false), await Values(PortfolioPermissions.DeliveryOptionLookup).ConfigureAwait(false),
            settings.ActiveCriteria, settings.PriorityBands.OrderBy(b => b.Minimum).ToList(), settings.ThresholdAmount,
            (await store.GetAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false)).CurrencyCode, settings.StartYearsAhead, settings.EndYearsAhead,
            await reader.TodayAsync(cancellationToken).ConfigureAwait(false), settings.ProjectScopeTemplateFileId, settings.AssessmentOnCreate,
            await access.CanAsync(context.User, PortfolioPermissions.RequestCreate, cancellationToken: cancellationToken).ConfigureAwait(false),
            await access.CanAsync(context.User, PortfolioPermissions.SettingsManage, cancellationToken: cancellationToken).ConfigureAwait(false),
            await Values(PortfolioPermissions.ThemeLookup).ConfigureAwait(false), await Values(PortfolioPermissions.RiskCategoryLookup).ConfigureAwait(false),
            await Values(PortfolioPermissions.StakeholderClassLookup).ConfigureAwait(false), await Values(PortfolioPermissions.LessonTypeLookup).ConfigureAwait(false),
            await Values(PortfolioPermissions.KpiIndexClassLookup).ConfigureAwait(false), settings.ProjectTemplate, settings.InitiativeTemplate, settings.ProgressTolerance);
    }

    public async Task<Result<SettingsDocument<PortfolioSettings>>> HandleAsync(GetPortfolioSettings query, CancellationToken cancellationToken) =>
        await editor.GetDocumentAsync<PortfolioSettings>(cancellationToken).ConfigureAwait(false);

    public Task<Result<SettingsDocument<PortfolioSettings>>> HandleAsync(SavePortfolioSettings command, CancellationToken cancellationToken) =>
        editor.SaveAsync(command.Settings, command.Version, PortfolioSettings.Validate, cancellationToken);

    public async Task<Result<SettingsDocument<PortfolioSettings>>> HandleAsync(UploadProjectScopeTemplate command, CancellationToken cancellationToken)
    {
        var current = await editor.GetDocumentAsync<PortfolioSettings>(cancellationToken).ConfigureAwait(false);
        var uploaded = await files.UploadAsync(
            new FileUpload(RequestFilePurposes.Template.Key, TemplateFiles.OwnerId, command.FileName, command.Content, ReplacesFileId: current.Value.ProjectScopeTemplateFileId),
            cancellationToken).ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            return uploaded.Error;
        }

        return await editor.SaveAsync(current.Value with { ProjectScopeTemplateFileId = uploaded.Value.Id }, current.Version, PortfolioSettings.Validate, cancellationToken)
            .ConfigureAwait(false);
    }
}
