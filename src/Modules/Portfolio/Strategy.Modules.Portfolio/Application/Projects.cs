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
    UnitDto? Unit,
    UnitDto? SectionUnit,
    string? StrategicObjectiveCode,
    string? ProgramTypeCode,
    decimal? Budget,
    decimal? Year1PlannedCost,
    decimal? Year2PlannedCost,
    decimal? Year3PlannedCost,
    decimal? Year4PlannedCost,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    PersonDto? Owner,
    PersonDto? Requester,
    Guid RequestId,
    string? RequestNumber,
    IReadOnlyList<string> StagePath,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record ProjectListItemDto(
    Guid Id, string Number, string Classification, string? NameAr, string? NameEn, string Stage, string Status, UnitDto? Unit, decimal? Budget, DateOnly? PlannedStart,
    DateOnly? PlannedEnd, DateTimeOffset CreatedAt);

public sealed record GetProject(Guid Id) : IQuery<ProjectDto>;

public sealed record ListProjects(PageRequest Page, string? Classification = null, string? Stage = null, Guid? UnitId = null, string? Search = null)
    : IQuery<PagedResult<ProjectListItemDto>>;

public sealed record OptionDto(string Code, string? LabelAr, string? LabelEn);

/// <summary>Everything the request screens need to render choices and live calculations (labels are configuration).</summary>
public sealed record PortfolioOptionsDto(
    IReadOnlyList<OptionDto> Objectives,
    IReadOnlyList<OptionDto> Sources,
    IReadOnlyList<OptionDto> ProgramTypes,
    IReadOnlyList<OptionDto> DeliveryOptions,
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
    bool CanManageSettings);

public sealed record GetPortfolioOptions : IQuery<PortfolioOptionsDto>;

public sealed record GetPortfolioSettings : IQuery<SettingsDocument<PortfolioSettings>>;

public sealed record SavePortfolioSettings(PortfolioSettings Settings, uint Version) : ICommand<SettingsDocument<PortfolioSettings>>;

public sealed record UploadProjectScopeTemplate(string FileName, Stream Content) : ICommand<SettingsDocument<PortfolioSettings>>;

internal sealed class ProjectHandlers(PortfolioDbContext db, RequestReader reader, IRequestContext context)
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
        return new ProjectDto(
            p.Id, p.Number, p.Classification, p.NameAr, p.NameEn, p.Description, p.ScopeOfWork, p.Stage, p.Status,
            p.UnitId is { } u ? units.GetValueOrDefault(u) : null, p.SectionUnitId is { } s ? units.GetValueOrDefault(s) : null, p.StrategicObjectiveCode,
            p.ProgramTypeCode, p.Budget, p.Year1PlannedCost, p.Year2PlannedCost, p.Year3PlannedCost, p.Year4PlannedCost, p.PlannedStart, p.PlannedEnd,
            p.OwnerEmployeeId is { } o ? people.GetValueOrDefault(o) : null, p.RequesterEmployeeId is { } q ? people.GetValueOrDefault(q) : null, p.RequestId, requestNumber,
            p.Classification == Classifications.Project ? ProjectStages.ProjectPath : ProjectStages.InitiativePath, p.CreatedAt, p.Version);
    }

    public async Task<Result<PagedResult<ProjectListItemDto>>> HandleAsync(ListProjects query, CancellationToken cancellationToken)
    {
        var page = query.Page.Normalised();
        var source = await reader.VisibleProjectsAsync(context.User, cancellationToken).ConfigureAwait(false);
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
        return new PagedResult<ProjectListItemDto>(projects.Select(p => new ProjectListItemDto(
            p.Id, p.Number, p.Classification, p.NameAr, p.NameEn, p.Stage, p.Status, p.UnitId is { } u ? units.GetValueOrDefault(u) : null, p.Budget, p.PlannedStart,
            p.PlannedEnd, p.CreatedAt)).ToList(), page.Page, page.PageSize, total);
    }
}

/// <summary>Options for the request screens and Settings → Portfolio.</summary>
internal sealed class PortfolioSettingsHandlers(
    RequestReader reader, ILookupService lookups, ISettingsStore store, ISettingsEditor editor, IAccessService access, IRequestContext context, IFileService files)
    : IQueryHandler<GetPortfolioOptions, PortfolioOptionsDto>,
        IQueryHandler<GetPortfolioSettings, SettingsDocument<PortfolioSettings>>,
        ICommandHandler<SavePortfolioSettings, SettingsDocument<PortfolioSettings>>,
        ICommandHandler<UploadProjectScopeTemplate, SettingsDocument<PortfolioSettings>>
{
    public async Task<Result<PortfolioOptionsDto>> HandleAsync(GetPortfolioOptions query, CancellationToken cancellationToken)
    {
        var settings = await reader.SettingsAsync(cancellationToken).ConfigureAwait(false);
        async Task<IReadOnlyList<OptionDto>> Values(string type) =>
            (await lookups.GetValuesAsync(type, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(v => v.Active).OrderBy(v => v.Sort).Select(v => new OptionDto(v.Code, v.LabelAr, v.LabelEn)).ToList();

        return new PortfolioOptionsDto(
            await Values(PortfolioPermissions.ObjectiveLookup).ConfigureAwait(false), await Values(PortfolioPermissions.SourceLookup).ConfigureAwait(false),
            await Values(PortfolioPermissions.ProgramTypeLookup).ConfigureAwait(false), await Values(PortfolioPermissions.DeliveryOptionLookup).ConfigureAwait(false),
            settings.ActiveCriteria, settings.PriorityBands.OrderBy(b => b.Minimum).ToList(), settings.ThresholdAmount,
            (await store.GetAsync<RegionalSettings>(cancellationToken).ConfigureAwait(false)).CurrencyCode, settings.StartYearsAhead, settings.EndYearsAhead,
            await reader.TodayAsync(cancellationToken).ConfigureAwait(false), settings.ProjectScopeTemplateFileId, settings.AssessmentOnCreate,
            await access.CanAsync(context.User, PortfolioPermissions.RequestCreate, cancellationToken: cancellationToken).ConfigureAwait(false),
            await access.CanAsync(context.User, PortfolioPermissions.SettingsManage, cancellationToken: cancellationToken).ConfigureAwait(false));
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
