using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Links;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Application;

public sealed record TeamTypeDto(
    Guid Id, string Code, string NameAr, string NameEn, string Permanence, bool RequiresApproval, bool RequiresMemo, IReadOnlyList<string> LinkableTypes, string DefaultCadence,
    string EvaluationTrigger, int AnnualMonth, int AnnualDay, string EvaluatorRule, Guid? EvaluatorEmployeeId, string? EvaluatorMemberRole, string LeaderEvaluatorRule,
    Guid? LeaderEvaluatorEmployeeId, string? LeaderEvaluatorMemberRole, int Sort, bool Active, uint Version);

public sealed record MemberRoleDto(
    Guid Id, string Code, string NameAr, string NameEn, int Sort, bool IsLeader, bool CanViewResults, bool CountsTowardQuorum, bool IsSecretary, bool Active, uint Version);

public sealed record TemplateDto(Guid Id, int Year, int VersionNo, string Status, TemplateContent Content, string? Notes, DateTimeOffset? PublishedAt, uint Version);

public sealed record RewardMatrixDto(Guid Id, int Year, int VersionNo, string Status, RewardMatrixContent Content, DateTimeOffset? PublishedAt, uint Version);

public sealed record TeamConfigurationDto(
    TeamSettings Settings, uint SettingsVersion, IReadOnlyList<TeamTypeDto> Types, IReadOnlyList<MemberRoleDto> Roles, IReadOnlyList<TemplateDto> Templates,
    IReadOnlyList<RewardMatrixDto> RewardMatrices, IReadOnlyList<string> TeamAbleTypes);

/// <summary>What the formation wizard and team pages need (any signed-in user): active types and roles, the published template's grades.</summary>
public sealed record TeamOptionsDto(IReadOnlyList<TeamTypeDto> Types, IReadOnlyList<MemberRoleDto> Roles, IReadOnlyList<GradeBand> Grades);

public sealed record GetTeamConfiguration : IQuery<TeamConfigurationDto>;

public sealed record GetTeamOptions : IQuery<TeamOptionsDto>;

public sealed record SaveTeamSettings(TeamSettings Settings, uint Version) : ICommand<TeamConfigurationDto>;

public sealed record SaveTeamType(Guid? Id, string Code, TeamTypeValues Values, uint? Version) : ICommand<TeamConfigurationDto>;

public sealed record SaveMemberRole(
    Guid? Id, string Code, string NameAr, string NameEn, int Sort, bool IsLeader, bool CanViewResults, bool CountsTowardQuorum, bool IsSecretary, bool Active, uint? Version)
    : ICommand<TeamConfigurationDto>;

/// <summary>Create (no id: a new draft version of the year, copied from the newest when no content) or edit a draft template.</summary>
public sealed record SaveTemplate(Guid? Id, int Year, TemplateContent? Content, string? Notes, uint? Version) : ICommand<TemplateDto>;

public sealed record PublishTemplate(Guid Id) : ICommand<TemplateDto>;

public sealed record SaveRewardMatrix(Guid? Id, int Year, RewardMatrixContent? Content, uint? Version) : ICommand<RewardMatrixDto>;

public sealed record PublishRewardMatrix(Guid Id) : ICommand<RewardMatrixDto>;

/// <summary>Settings → Teams (ADR 0041). Every handler needs <c>teams.settings.manage</c> (endpoint) and writes audited rows.</summary>
internal sealed class ConfigurationHandlers(TeamsDbContext db, ISettingsEditor editor, ILinkRegistry links, IClock clock)
    : IQueryHandler<GetTeamConfiguration, TeamConfigurationDto>,
        IQueryHandler<GetTeamOptions, TeamOptionsDto>,
        ICommandHandler<SaveTeamSettings, TeamConfigurationDto>,
        ICommandHandler<SaveTeamType, TeamConfigurationDto>,
        ICommandHandler<SaveMemberRole, TeamConfigurationDto>,
        ICommandHandler<SaveTemplate, TemplateDto>,
        ICommandHandler<PublishTemplate, TemplateDto>,
        ICommandHandler<SaveRewardMatrix, RewardMatrixDto>,
        ICommandHandler<PublishRewardMatrix, RewardMatrixDto>
{
    public async Task<Result<TeamConfigurationDto>> HandleAsync(GetTeamConfiguration query, CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task<Result<TeamOptionsDto>> HandleAsync(GetTeamOptions query, CancellationToken cancellationToken)
    {
        var types = await db.Types.AsNoTracking().Where(t => t.Active).OrderBy(t => t.Sort).ToListAsync(cancellationToken).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().Where(r => r.Active).OrderBy(r => r.Sort).ToListAsync(cancellationToken).ConfigureAwait(false);
        var template = await db.Templates.AsNoTracking().Where(t => t.Status == VersionStatuses.Published).OrderByDescending(t => t.Year).ThenByDescending(t => t.VersionNo)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new TeamOptionsDto(types.Select(ToDto).ToList(), roles.Select(ToDto).ToList(), template?.Read().GradeBands ?? []);
    }

    public async Task<Result<TeamConfigurationDto>> HandleAsync(SaveTeamSettings command, CancellationToken cancellationToken)
    {
        var saved = await editor.SaveAsync(command.Settings, command.Version, TeamSettings.Validate, cancellationToken).ConfigureAwait(false);
        return saved.IsFailure ? saved.Error : await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamConfigurationDto>> HandleAsync(SaveTeamType command, CancellationToken cancellationToken)
    {
        var v = command.Values;
        var roles = await db.Roles.AsNoTracking().Select(r => r.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        var teamAble = (await links.TypesAsync(LinkCapabilities.TeamAble, cancellationToken).ConfigureAwait(false)).Select(t => t.ResourceType).ToList();
        var errors = new List<FieldError>();
        void Check(bool ok, string field, string key = "validation.unknownCode")
        {
            if (!ok)
            {
                errors.Add(new FieldError(field, key, key));
            }
        }

        Check(System.Text.RegularExpressions.Regex.IsMatch(command.Code ?? string.Empty, "^[a-z0-9][a-z0-9-]{0,99}$"), "code", "validation.invalid");
        Check(!string.IsNullOrWhiteSpace(v.NameAr) && !string.IsNullOrWhiteSpace(v.NameEn), "name", "validation.notEmpty");
        Check(Permanence.All.Contains(v.Permanence), "permanence");
        Check(Cadences.All.Contains(v.DefaultCadence), "defaultCadence");
        Check(EvaluationTriggers.Configurable.Contains(v.EvaluationTrigger), "evaluationTrigger");
        Check(v.AnnualMonth is >= 1 and <= 12 && v.AnnualDay >= 1 && v.AnnualDay <= DateTime.DaysInMonth(2024, Math.Clamp(v.AnnualMonth, 1, 12)), "annualDay",
            "validation.invalid");
        Check(EvaluatorRules.All.Contains(v.EvaluatorRule), "evaluatorRule");
        Check(EvaluatorRules.ForLeader.Contains(v.LeaderEvaluatorRule), "leaderEvaluatorRule");
        Check(v.EvaluatorRule != EvaluatorRules.Employee || v.EvaluatorEmployeeId is not null, "evaluatorEmployeeId", "validation.notEmpty");
        Check(v.EvaluatorRule != EvaluatorRules.MemberRole || roles.Contains(v.EvaluatorMemberRole ?? string.Empty), "evaluatorMemberRole");
        Check(v.LeaderEvaluatorRule != EvaluatorRules.Employee || v.LeaderEvaluatorEmployeeId is not null, "leaderEvaluatorEmployeeId", "validation.notEmpty");
        Check(v.LeaderEvaluatorRule != EvaluatorRules.MemberRole || roles.Contains(v.LeaderEvaluatorMemberRole ?? string.Empty), "leaderEvaluatorMemberRole");
        Check(v.LinkableTypes.All(teamAble.Contains), "linkableTypes");
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        TeamType type;
        if (command.Id is { } id)
        {
            var found = await db.Types.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            type = found;
            db.Entry(type).Property(x => x.Version).OriginalValue = command.Version ?? type.Version;
        }
        else
        {
            if (await db.Types.AnyAsync(t => t.Code == command.Code, cancellationToken).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("code", "validation.duplicate", "validation.duplicate")]);
            }

            type = TeamType.Create(command.Code!);
            db.Types.Add(type);
        }

        type.Update(v);
        return await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TeamConfigurationDto>> HandleAsync(SaveMemberRole command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.NameAr) || string.IsNullOrWhiteSpace(command.NameEn)
            || !System.Text.RegularExpressions.Regex.IsMatch(command.Code ?? string.Empty, "^[a-z0-9][a-z0-9-]{0,99}$"))
        {
            return Error.Validation([new FieldError("name", "validation.notEmpty", "validation.notEmpty")]);
        }

        MemberRole role;
        if (command.Id is { } id)
        {
            var found = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return Error.NotFound();
            }

            role = found;
            db.Entry(role).Property(x => x.Version).OriginalValue = command.Version ?? role.Version;
        }
        else
        {
            if (await db.Roles.AnyAsync(r => r.Code == command.Code, cancellationToken).ConfigureAwait(false))
            {
                return Error.Validation([new FieldError("code", "validation.duplicate", "validation.duplicate")]);
            }

            role = MemberRole.Create(command.Code!);
            db.Roles.Add(role);
        }

        role.Update(command.NameAr, command.NameEn, command.Sort, command.IsLeader, command.CanViewResults, command.CountsTowardQuorum, command.IsSecretary, command.Active);
        return await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TemplateDto>> HandleAsync(SaveTemplate command, CancellationToken cancellationToken)
    {
        if (command.Year is < 2000 or > 2100)
        {
            return Error.Validation([new FieldError("year", "validation.invalid", "validation.invalid")]);
        }

        if (command.Id is { } id)
        {
            var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
            if (template is null)
            {
                return Error.NotFound();
            }

            var errors = TemplateRules.Validate(command.Content);
            if (errors.Count > 0)
            {
                return Error.Validation(errors);
            }

            db.Entry(template).Property(x => x.Version).OriginalValue = command.Version ?? template.Version;
            if (!template.Edit(command.Content!, command.Notes))
            {
                return Error.Rule("teams.versionPublished", "teams.errors.versionPublished");
            }

            return await SaveTemplateAsync(template, cancellationToken).ConfigureAwait(false);
        }

        var content = command.Content
            ?? (await db.Templates.AsNoTracking().OrderByDescending(t => t.Year).ThenByDescending(t => t.VersionNo).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false))
            ?.Read() ?? DefaultTemplate.Content;
        var validation = TemplateRules.Validate(content);
        if (validation.Count > 0)
        {
            return Error.Validation(validation);
        }

        var next = (await db.Templates.Where(t => t.Year == command.Year).MaxAsync(t => (int?)t.VersionNo, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var created = EvaluationTemplate.Create(command.Year, next, content, command.Notes);
        db.Templates.Add(created);
        return await SaveTemplateAsync(created, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<TemplateDto>> HandleAsync(PublishTemplate command, CancellationToken cancellationToken)
    {
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return Error.NotFound();
        }

        if (template.Status != VersionStatuses.Draft)
        {
            return Error.Rule("teams.versionPublished", "teams.errors.versionPublished");
        }

        var errors = TemplateRules.Validate(template.Read());
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        foreach (var old in await db.Templates.Where(t => t.Year == template.Year && t.Status == VersionStatuses.Published).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            old.Retire();
        }

        template.Publish(clock.UtcNow);
        return await SaveTemplateAsync(template, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RewardMatrixDto>> HandleAsync(SaveRewardMatrix command, CancellationToken cancellationToken)
    {
        if (command.Year is < 2000 or > 2100)
        {
            return Error.Validation([new FieldError("year", "validation.invalid", "validation.invalid")]);
        }

        var grades = await GradesAsync(cancellationToken).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().Select(r => r.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (command.Id is { } id)
        {
            var matrix = await db.RewardMatrices.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
            if (matrix is null)
            {
                return Error.NotFound();
            }

            var errors = RewardCalculator.Validate(command.Content, grades, roles);
            if (errors.Count > 0)
            {
                return Error.Validation(errors);
            }

            db.Entry(matrix).Property(x => x.Version).OriginalValue = command.Version ?? matrix.Version;
            if (!matrix.Edit(command.Content!))
            {
                return Error.Rule("teams.versionPublished", "teams.errors.versionPublished");
            }

            return await SaveMatrixAsync(matrix, cancellationToken).ConfigureAwait(false);
        }

        var content = command.Content
            ?? (await db.RewardMatrices.AsNoTracking().OrderByDescending(m => m.Year).ThenByDescending(m => m.VersionNo).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false))
            ?.Read() ?? new RewardMatrixContent([], [], 0, "AED");
        var validation = RewardCalculator.Validate(content, grades, roles);
        if (validation.Count > 0)
        {
            return Error.Validation(validation);
        }

        var next = (await db.RewardMatrices.Where(m => m.Year == command.Year).MaxAsync(m => (int?)m.VersionNo, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var created = RewardMatrix.Create(command.Year, next, content);
        db.RewardMatrices.Add(created);
        return await SaveMatrixAsync(created, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RewardMatrixDto>> HandleAsync(PublishRewardMatrix command, CancellationToken cancellationToken)
    {
        var matrix = await db.RewardMatrices.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken).ConfigureAwait(false);
        if (matrix is null)
        {
            return Error.NotFound();
        }

        if (matrix.Status != VersionStatuses.Draft)
        {
            return Error.Rule("teams.versionPublished", "teams.errors.versionPublished");
        }

        foreach (var old in await db.RewardMatrices.Where(m => m.Year == matrix.Year && m.Status == VersionStatuses.Published).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            old.Retire();
        }

        matrix.Publish(clock.UtcNow);
        return await SaveMatrixAsync(matrix, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<string>> GradesAsync(CancellationToken ct)
    {
        var templates = await db.Templates.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        return templates.SelectMany(t => t.Read().GradeBands.Select(b => b.Code)).Distinct().ToList();
    }

    private async Task<Result<TeamConfigurationDto>> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await LoadAsync(ct).ConfigureAwait(false);
    }

    private async Task<Result<TemplateDto>> SaveTemplateAsync(EvaluationTemplate template, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(template);
    }

    private async Task<Result<RewardMatrixDto>> SaveMatrixAsync(RewardMatrix matrix, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return ToDto(matrix);
    }

    private async Task<TeamConfigurationDto> LoadAsync(CancellationToken ct)
    {
        var document = await editor.GetDocumentAsync<TeamSettings>(ct).ConfigureAwait(false);
        var types = await db.Types.AsNoTracking().OrderBy(t => t.Sort).ToListAsync(ct).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Sort).ToListAsync(ct).ConfigureAwait(false);
        var templates = await db.Templates.AsNoTracking().OrderByDescending(t => t.Year).ThenByDescending(t => t.VersionNo).ToListAsync(ct).ConfigureAwait(false);
        var matrices = await db.RewardMatrices.AsNoTracking().OrderByDescending(t => t.Year).ThenByDescending(t => t.VersionNo).ToListAsync(ct).ConfigureAwait(false);
        var teamAble = (await links.TypesAsync(LinkCapabilities.TeamAble, ct).ConfigureAwait(false)).Select(t => t.ResourceType).ToList();
        return new TeamConfigurationDto(document.Value, document.Version, types.Select(ToDto).ToList(), roles.Select(ToDto).ToList(), templates.Select(ToDto).ToList(),
            matrices.Select(ToDto).ToList(), teamAble);
    }

    public static TeamTypeDto ToDto(TeamType t) => new(
        t.Id, t.Code, t.NameAr, t.NameEn, t.Permanence, t.RequiresApproval, t.RequiresMemo, t.LinkableTypes, t.DefaultCadence, t.EvaluationTrigger, t.AnnualMonth, t.AnnualDay,
        t.EvaluatorRule, t.EvaluatorEmployeeId, t.EvaluatorMemberRole, t.LeaderEvaluatorRule, t.LeaderEvaluatorEmployeeId, t.LeaderEvaluatorMemberRole, t.Sort, t.Active, t.Version);

    public static MemberRoleDto ToDto(MemberRole r) =>
        new(r.Id, r.Code, r.NameAr, r.NameEn, r.Sort, r.IsLeader, r.CanViewResults, r.CountsTowardQuorum, r.IsSecretary, r.Active, r.Version);

    public static TemplateDto ToDto(EvaluationTemplate t) => new(t.Id, t.Year, t.VersionNo, t.Status, t.Read(), t.Notes, t.PublishedAt, t.Version);

    public static RewardMatrixDto ToDto(RewardMatrix m) => new(m.Id, m.Year, m.VersionNo, m.Status, m.Read(), m.PublishedAt, m.Version);
}

/// <summary>Seeds SPEC §4.3 defaults once (ADR 0041): inserts missing codes, never overwrites administrators' edits.</summary>
internal sealed class TeamsSeeder : IDatabaseSeeder
{
    public int Order => 500;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TeamsDbContext>();
        var clock = services.GetRequiredService<IClock>();
        services.GetRequiredService<RequestContext>().Initialise(Guid.CreateVersion7().ToString("N"), Channels.System, StrategyClaims.CreateSystemPrincipal());
        var roles = await db.Roles.Select(r => r.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (code, ar, en, leader, results, quorum, secretary, sort) in new[]
                 {
                     ("president", "الرئيس", "President", true, true, true, false, 10),
                     ("deputy", "نائب الرئيس", "Deputy", false, false, true, false, 20),
                     ("project-manager", "مدير المشروع", "Project Manager", true, true, true, false, 30),
                     ("member", "عضو", "Member", false, false, true, false, 40),
                     ("rapporteur", "مقرر", "Rapporteur", false, false, true, true, 50),
                     ("member-rapporteur", "عضو ومقرر", "Member & Rapporteur", false, false, true, true, 60),
                     ("secretary", "أمين السر", "Secretary", false, false, false, true, 70),
                 })
        {
            if (!roles.Contains(code))
            {
                var role = MemberRole.Create(code);
                role.Update(ar, en, sort, leader, results, quorum, secretary, true);
                db.Roles.Add(role);
            }
        }

        var types = await db.Types.Select(t => t.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (code, ar, en, permanence, trigger, cadence, sort) in new[]
                 {
                     ("work-team", "فريق عمل", "Work team", Permanence.Either, EvaluationTriggers.Auto, Cadences.Quarterly, 10),
                     ("project-team", "فريق مشروع", "Project team", Permanence.Temporary, EvaluationTriggers.LinkedRecordClosed, Cadences.Monthly, 20),
                     ("standing-committee", "لجنة دائمة", "Standing committee", Permanence.Permanent, EvaluationTriggers.Annual, Cadences.Quarterly, 30),
                     ("temporary-committee", "لجنة مؤقتة", "Temporary committee", Permanence.Temporary, EvaluationTriggers.EndDate, Cadences.Monthly, 40),
                 })
        {
            if (!types.Contains(code))
            {
                var type = TeamType.Create(code);
                type.Update(new TeamTypeValues(ar, en, permanence, true, true, [], cadence, trigger, 1, 1, EvaluatorRules.Leader, null, null, EvaluatorRules.Same, null, null, sort,
                    true));
                db.Types.Add(type);
            }
        }

        var year = clock.Today.Year;
        if (!await db.Templates.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var template = EvaluationTemplate.Create(year, 1, DefaultTemplate.Content, null);
            template.Publish(clock.UtcNow);
            db.Templates.Add(template);
        }

        if (!await db.RewardMatrices.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var matrix = RewardMatrix.Create(year, 1, new RewardMatrixContent([], ["very-active"], 0, "AED"));
            matrix.Publish(clock.UtcNow);
            db.RewardMatrices.Add(matrix);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
