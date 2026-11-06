using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Identity.Wasel;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

// ---------------------------------------------------------------- DTOs (internal side)

public sealed record VendorListItemDto(
    Guid Id, string Number, string? NameAr, string? NameEn, string Status, string? TradeLicenceNo, DateOnly? LicenceExpiry, string? Email, int Users, int ActiveProjects,
    decimal? AverageRating);

public sealed record VendorUserDto(
    Guid Id, Guid UserId, string Email, string? Name, string Status, DateTimeOffset InvitedAt, DateTimeOffset? LastSignInAt, bool MfaEnabled, bool HasPassword,
    int ActiveProjects, string? DisabledReason);

public sealed record VendorRatingDto(
    Guid Id, Guid ProjectId, string? ProjectNumber, string? ProjectNameAr, string? ProjectNameEn, Guid? MilestoneId, int Timeliness, int Quality, int Responsiveness,
    int Reporting, decimal Overall, string? Comment, DateTimeOffset RatedAt);

public sealed record VendorPerformanceDto(int Count, decimal? Timeliness, decimal? Quality, decimal? Responsiveness, decimal? Reporting, decimal? Overall);

public sealed record VendorDetailDto(
    Guid Id, string Number, string? NameAr, string? NameEn, string? TradeLicenceNo, DateOnly? LicenceExpiry, string? CountryCode, string? Address, string? ContactPerson,
    string? Email, string? Phone, string Status, string? StatusReason, string? Notes, IReadOnlyList<VendorUserDto> Users, IReadOnlyList<VendorRatingDto> Ratings,
    VendorPerformanceDto Performance, uint Version);

public sealed record VendorInput(VendorValues Values, uint? Version = null);

public sealed record VendorStatusInput(string Status, string? Reason);

public sealed record ReasonOnlyInput(string? Reason);

public sealed record WaselMemberDto(
    Guid Id, Guid VendorUserId, string Email, string? Name, string Role, string Status, DateTimeOffset? ValidTo, DateTimeOffset? LastSignInAt, string UserStatus);

public sealed record WaselAssignmentDto(
    Guid Id, Guid VendorId, string VendorNumber, string? VendorNameAr, string? VendorNameEn, string VendorStatus, Guid? ContractId, DateOnly? AccessUntil, string ExpiryRule,
    string Status, string? EndedReason, IReadOnlyList<WaselMemberDto> Members);

public sealed record WaselPanelDto(
    string UpdateMode, bool CanManage, IReadOnlyList<WaselAssignmentDto> Assignments, IReadOnlyList<PmProposalDto> Proposals, IReadOnlyList<PmSubmissionDto> Submissions,
    IReadOnlyList<PmDeliverableDto> Deliverables, IReadOnlyList<PmVendorItemDto> Items, IReadOnlyList<VendorRatingDto> Ratings, IReadOnlyList<WaselMilestoneRefDto> Milestones);

public sealed record WaselMilestoneRefDto(Guid Id, string Code, string? TitleAr, string? TitleEn, bool IsHeader, Guid? VendorId, decimal Weight);

public sealed record PmProposalDto(
    Guid Id, Guid VendorId, string? VendorNameAr, string? VendorNameEn, int Sequence, string Status, string Kind, IReadOnlyList<ProposalItem> Items,
    IReadOnlyList<ProposalItem>? AcceptedItems, IReadOnlyList<ProposalDiffEntry> Diff, string? VendorNote, string? PmComment, DateTimeOffset? SubmittedAt,
    DateTimeOffset? DecidedAt, uint Version);

public sealed record PreviousValuesDto(decimal Approved, DateOnly? ActualStart, DateOnly? ActualEnd);

public sealed record PmSubmissionItemDto(
    Guid Id, Guid MilestoneId, string Code, string? TitleAr, string? TitleEn, decimal Weight, DateOnly? PlannedStart, DateOnly? PlannedEnd, PreviousValuesDto? Previous,
    VendorProgressValues Vendor, VendorProgressValues? Final, string ReviewStatus, IReadOnlyList<DiffEntry> Diff, string? PmComment, IReadOnlyList<VendorFileDto> Evidence,
    IReadOnlyList<string> Flags, uint Version);

public sealed record PmSubmissionDto(
    Guid Id, Guid VendorId, string? VendorNameAr, string? VendorNameEn, string Period, string Status, DateTimeOffset? SubmittedAt, DateTimeOffset? ReviewedAt,
    string? SharedOutcome, string? SharedNote, IReadOnlyList<PmSubmissionItemDto> Items, uint Version);

public sealed record PmDeliverableDto(
    Guid Id, Guid VendorId, string? VendorNameAr, string? VendorNameEn, Guid? MilestoneId, string Title, string? Description, string Status, string? PmComment,
    IReadOnlyList<VendorFileDto> Files, DateTimeOffset CreatedAt, uint Version);

public sealed record PmVendorItemDto(
    Guid Id, Guid VendorId, string Kind, string Title, string? Description, string? Impact, int? Probability, int? ImpactLevel, Guid? MilestoneId, string Status,
    string? PmComment, Guid? CreatedRecordId, DateTimeOffset CreatedAt, uint Version);

public sealed record VendorFileDto(Guid Id, string Name, string ContentType, long SizeBytes, string ScanStatus);

public sealed record VendorAssignmentInput(Guid VendorId, Guid? ContractId, DateOnly? AccessUntil, string? ExpiryRule);

public sealed record InvitationInput(Guid VendorId, string Email, string? Name, string Role, string? Language);

public sealed record UpdateModeInput(string Mode, uint Version);

public sealed record MilestoneVendorInput(Guid? VendorId);

public sealed record ProposalDecisionInput(IReadOnlyList<ProposalItem>? Items, string? Comment, uint Version);

public sealed record ItemReviewInput(VendorProgressValues? Values, string? Comment, uint Version);

public sealed record ShareOutcomeInput(string? Note);

public sealed record WaselDecisionInput(string? Comment, uint Version);

public sealed record VendorRatingInput(Guid VendorId, Guid? MilestoneId, VendorRatingValues Values);

public sealed record MeetingVendorInput(Guid? VendorId);

public sealed record WaselCommentInput(string SubjectType, Guid SubjectId, string Body, Guid? VendorId = null);

public sealed record WaselCommentDto(Guid Id, string SubjectType, Guid SubjectId, string AuthorKind, string? AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record VendorReviewAiDto(IReadOnlyList<VendorReviewFlagDto> Flags, string? Summary, string? ReturnCommentAr, string? ReturnCommentEn, bool AiUsed);

public sealed record VendorReviewFlagDto(Guid MilestoneId, string Code, string Flag);

/// <summary>Vendor register (ADR 0112): PM Office manages vendors and their Wasel accounts.</summary>
internal sealed class VendorRegisterService(
    PortfolioDbContext db, INumberingService numbering, IVendorAccounts accounts, IAccessService access, IRequestContext context, IClock clock, WaselGrants grants)
{
    public async Task<IReadOnlyList<VendorListItemDto>> ListAsync(string? search, string? status, CancellationToken ct)
    {
        var query = db.Vendors.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(v => v.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(v => (v.NameAr != null && EF.Functions.ILike(v.NameAr, $"%{term}%")) || (v.NameEn != null && EF.Functions.ILike(v.NameEn, $"%{term}%"))
                || EF.Functions.ILike(v.Number, $"%{term}%"));
        }

        var vendors = await query.OrderBy(v => v.Number).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var ids = vendors.Select(v => v.Id).ToList();
        var users = await db.VendorUsers.AsNoTracking().Where(u => ids.Contains(u.VendorId)).GroupBy(u => u.VendorId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var projects = await db.VendorAssignments.AsNoTracking().Where(a => ids.Contains(a.VendorId) && a.Status == AssignmentStatuses.Active).GroupBy(a => a.VendorId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var ratings = await db.VendorRatings.AsNoTracking().Where(r => ids.Contains(r.VendorId)).GroupBy(r => r.VendorId)
            .Select(g => new { g.Key, Avg = g.Average(r => r.Overall) }).ToDictionaryAsync(x => x.Key, x => x.Avg, ct).ConfigureAwait(false);
        return vendors.Select(v => new VendorListItemDto(v.Id, v.Number, v.NameAr, v.NameEn, v.Status, v.TradeLicenceNo, v.LicenceExpiry, v.Email,
            users.GetValueOrDefault(v.Id), projects.GetValueOrDefault(v.Id), ratings.TryGetValue(v.Id, out var a) ? decimal.Round(a, 2) : null)).ToList();
    }

    public async Task<Result<VendorDetailDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct).ConfigureAwait(false);
        if (vendor is null)
        {
            return Error.NotFound();
        }

        var users = await db.VendorUsers.AsNoTracking().Where(u => u.VendorId == id).OrderBy(u => u.Email).ToListAsync(ct).ConfigureAwait(false);
        var accountsById = await accounts.GetManyAsync(users.Select(u => u.UserId).ToList(), ct).ConfigureAwait(false);
        var userIds = users.Select(u => u.Id).ToList();
        var activeProjects = await db.VendorMembers.AsNoTracking().Where(m => userIds.Contains(m.VendorUserId) && m.Status == VendorMemberStatuses.Active)
            .GroupBy(m => m.VendorUserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);
        var ratings = await RatingsAsync(r => r.VendorId == id, ct).ConfigureAwait(false);
        return new VendorDetailDto(vendor.Id, vendor.Number, vendor.NameAr, vendor.NameEn, vendor.TradeLicenceNo, vendor.LicenceExpiry, vendor.CountryCode, vendor.Address,
            vendor.ContactPerson, vendor.Email, vendor.Phone, vendor.Status, vendor.StatusReason, vendor.Notes,
            users.Select(u => UserDto(u, accountsById.GetValueOrDefault(u.UserId), activeProjects.GetValueOrDefault(u.Id))).ToList(), ratings, Performance(ratings),
            vendor.Version);
    }

    public async Task<Result<VendorDetailDto>> SaveAsync(Guid? id, VendorInput input, CancellationToken ct)
    {
        var v = input.Values;
        var errors = Validate(v);
        if (v is null)
        {
            return Error.Validation(errors);
        }

        if (v.TradeLicenceNo is { } licence && await db.Vendors.AnyAsync(x => x.TradeLicenceNo == licence.Trim() && x.Id != id, ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("tradeLicenceNo", "portfolio.errors.vendorDuplicate", "portfolio.errors.vendorDuplicate"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        Vendor vendor;
        if (id is { } existing)
        {
            vendor = (await db.Vendors.FirstOrDefaultAsync(x => x.Id == existing, ct).ConfigureAwait(false))!;
            if (vendor is null)
            {
                return Error.NotFound();
            }

            if (input.Version is { } version)
            {
                db.Entry(vendor).Property(x => x.Version).OriginalValue = version;
            }

            vendor.Edit(v);
            if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
            {
                return conflict;
            }
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var number = await numbering.NextAsync(db, PortfolioPermissions.VendorNumbering, new NumberingContext(clock.Today), ct).ConfigureAwait(false);
            vendor = Vendor.Create(number, v);
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return await GetAsync(vendor.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Suspended / blacklisted vendors lose portal access at once (ADR 0112); their users' sessions are revoked.</summary>
    public async Task<Result<VendorDetailDto>> SetStatusAsync(Guid id, VendorStatusInput input, CancellationToken ct)
    {
        if (!VendorStatuses.All.Contains(input.Status))
        {
            return Error.Validation([new FieldError("status", "validation.unknownCode", "validation.unknownCode")]);
        }

        if (input.Status != VendorStatuses.Active && string.IsNullOrWhiteSpace(input.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct).ConfigureAwait(false);
        if (vendor is null)
        {
            return Error.NotFound();
        }

        vendor.SetStatus(input.Status, input.Reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (input.Status != VendorStatuses.Active)
        {
            foreach (var user in await db.VendorUsers.AsNoTracking().Where(u => u.VendorId == id).ToListAsync(ct).ConfigureAwait(false))
            {
                await accounts.RevokeSessionsAsync(user.UserId, ct).ConfigureAwait(false);
            }
        }

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<Result<VendorDetailDto>> SetUserActiveAsync(Guid vendorUserId, bool active, string? reason, CancellationToken ct)
    {
        var user = await db.VendorUsers.FirstOrDefaultAsync(u => u.Id == vendorUserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound();
        }

        if (!active && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (active)
        {
            user.Reactivate();
        }
        else
        {
            user.Disable(VendorUserStatuses.Disabled, reason, clock.UtcNow);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await accounts.SetActiveAsync(user.UserId, active, ct).ConfigureAwait(false);
        return await GetAsync(user.VendorId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VendorRatingDto>> RatingsAsync(System.Linq.Expressions.Expression<Func<VendorRating, bool>> filter, CancellationToken ct)
    {
        var ratings = await db.VendorRatings.AsNoTracking().Where(filter).OrderByDescending(r => r.RatedAt).ToListAsync(ct).ConfigureAwait(false);
        var projectIds = ratings.Select(r => r.ProjectId).Distinct().ToList();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        return ratings.Select(r =>
        {
            var p = projects.GetValueOrDefault(r.ProjectId);
            return new VendorRatingDto(r.Id, r.ProjectId, p?.Number, p?.NameAr, p?.NameEn, r.MilestoneId, r.Timeliness, r.Quality, r.Responsiveness, r.Reporting, r.Overall,
                r.Comment, r.RatedAt);
        }).ToList();
    }

    public static VendorPerformanceDto Performance(IReadOnlyList<VendorRatingDto> ratings) =>
        ratings.Count == 0
            ? new VendorPerformanceDto(0, null, null, null, null, null)
            : new VendorPerformanceDto(ratings.Count, Avg(ratings.Select(r => (decimal)r.Timeliness)), Avg(ratings.Select(r => (decimal)r.Quality)),
                Avg(ratings.Select(r => (decimal)r.Responsiveness)), Avg(ratings.Select(r => (decimal)r.Reporting)), Avg(ratings.Select(r => r.Overall)));

    private static decimal Avg(IEnumerable<decimal> values) => decimal.Round(values.Average(), 2, MidpointRounding.AwayFromZero);

    private static VendorUserDto UserDto(VendorUser u, VendorAccountDto? account, int projects) =>
        new(u.Id, u.UserId, u.Email, u.Name, u.Status, u.InvitedAt, account?.LastSignInAt, account?.MfaEnabled ?? false, account?.HasPassword ?? false, projects, u.DisabledReason);

    internal static List<FieldError> Validate(VendorValues v)
    {
        if (v is null)
        {
            return [new FieldError("values", "validation.required", "validation.required")];
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(v.NameAr) && string.IsNullOrWhiteSpace(v.NameEn))
        {
            errors.Add(new FieldError("nameEn", "validation.atLeastOneLanguage", "validation.atLeastOneLanguage"));
        }

        foreach (var (field, text, max) in new[]
                 {
                     ("nameAr", v.NameAr, 300), ("nameEn", v.NameEn, 300), ("tradeLicenceNo", v.TradeLicenceNo, 100), ("countryCode", v.CountryCode, 20),
                     ("address", v.Address, 2000), ("contactPerson", v.ContactPerson, 300), ("email", v.Email, 300), ("phone", v.Phone, 100), ("notes", v.Notes, 2000),
                 })
        {
            if (text is { } t && t.Length > max)
            {
                errors.Add(new FieldError(field, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["max"] = max }));
            }
        }

        if (v.Email is { } email && !string.IsNullOrWhiteSpace(email) && !IsEmail(email))
        {
            errors.Add(new FieldError("email", "validation.email", "validation.email"));
        }

        return errors;
    }

    internal static bool IsEmail(string value)
    {
        var trimmed = value.Trim();
        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < trimmed.Length - 3 && trimmed.IndexOf('.', at) > at + 1 && !trimmed.Contains(' ', StringComparison.Ordinal);
    }

    private async Task<Error?> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }
    }

    // Kept for symmetry with the project service: only PM Office reaches the register endpoints.
    internal Task<bool> CanManageAsync(CancellationToken ct) => access.CanAsync(context.User, PortfolioPermissions.VendorManage, cancellationToken: ct);

    internal WaselGrants Grants => grants;
}

/// <summary>The PM's side of Wasel on one project (docs/design/portfolio.md §47–§57): vendors, invitations, reviews, ratings.</summary>
internal sealed class WaselManagementService(
    PortfolioDbContext db,
    ProjectLoader loader,
    RequestReader reader,
    IRequestContext context,
    IClock clock,
    IVendorAccounts accounts,
    WaselGrants grants,
    WaselNotifier notifier,
    IFileService files,
    MilestoneService milestones,
    AgileService agile,
    ProjectItems items,
    RaidService raid,
    CostVisibility costs,
    VendorRegisterService register,
    Microsoft.AspNetCore.Http.IHttpContextAccessor http)
{
    private async Task<(Project? Project, Error? Error)> ProjectAsync(Guid projectId, bool manage, CancellationToken ct, bool tracked = false)
    {
        var project = await loader.FindAsync(projectId, context.User, ct, tracked).ConfigureAwait(false);
        if (project is null)
        {
            return (null, Error.NotFound());
        }

        if (manage && !await loader.CanAsync(context.User, PortfolioPermissions.ProjectVendors, project, ct).ConfigureAwait(false))
        {
            return (null, Error.Forbidden());
        }

        return (project, null);
    }

    public async Task<Result<WaselPanelDto>> PanelAsync(Guid projectId, CancellationToken ct)
    {
        var (project, error) = await ProjectAsync(projectId, manage: false, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var canManage = await loader.CanAsync(context.User, PortfolioPermissions.ProjectVendors, project, ct).ConfigureAwait(false);
        var assignments = await db.VendorAssignments.AsNoTracking().Where(a => a.ProjectId == projectId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var vendorIds = assignments.Select(a => a.VendorId).Distinct().ToList();
        var vendors = await db.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct).ConfigureAwait(false);
        var members = await db.VendorMembers.AsNoTracking().Where(m => m.ProjectId == projectId).ToListAsync(ct).ConfigureAwait(false);
        var vendorUserIds = members.Select(m => m.VendorUserId).Distinct().ToList();
        var vendorUsers = await db.VendorUsers.AsNoTracking().Where(u => vendorUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct).ConfigureAwait(false);
        var signIns = await accounts.GetManyAsync(vendorUsers.Values.Select(u => u.UserId).ToList(), ct).ConfigureAwait(false);
        var assignmentDtos = assignments.Select(a =>
        {
            var vendor = vendors[a.VendorId];
            return new WaselAssignmentDto(a.Id, a.VendorId, vendor.Number, vendor.NameAr, vendor.NameEn, vendor.Status, a.ContractId, a.AccessUntil, a.ExpiryRule, a.Status,
                a.EndedReason, members.Where(m => m.AssignmentId == a.Id).OrderBy(m => m.CreatedAt).Select(m =>
                {
                    var u = vendorUsers[m.VendorUserId];
                    return new WaselMemberDto(m.Id, u.Id, u.Email, u.Name, m.Role, m.Status, m.ValidTo, signIns.GetValueOrDefault(u.UserId)?.LastSignInAt, u.Status);
                }).ToList());
        }).ToList();
        var proposalRows = await db.PlanProposals.AsNoTracking().Where(p => p.ProjectId == projectId && p.Status != ProposalStatuses.Draft)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var allVendorIds = vendorIds.Concat(proposalRows.Select(p => p.VendorId)).Distinct().ToList();
        var names = await db.Vendors.AsNoTracking().Where(v => allVendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => (v.NameAr, v.NameEn), ct).ConfigureAwait(false);
        var proposals = proposalRows.Select(p => ProposalDto(p, names)).ToList();
        var submissions = await SubmissionsAsync(project, names, ct).ConfigureAwait(false);
        var deliverableRows = await db.VendorDeliverables.AsNoTracking().Where(d => d.ProjectId == projectId).OrderByDescending(d => d.CreatedAt).ToListAsync(ct)
            .ConfigureAwait(false);
        var deliverables = new List<PmDeliverableDto>();
        foreach (var d in deliverableRows)
        {
            var n = names.GetValueOrDefault(d.VendorId);
            deliverables.Add(new PmDeliverableDto(d.Id, d.VendorId, n.NameAr, n.NameEn, d.MilestoneId, d.Title, d.Description, d.Status, d.PmComment,
                await FilesAsync(VendorDeliverable.EntityType, d.Id, WaselFilePurposes.Deliverable.Key, ct).ConfigureAwait(false), d.CreatedAt, d.Version));
        }

        var vendorItems = await db.VendorItems.AsNoTracking().Where(i => i.ProjectId == projectId).OrderByDescending(i => i.CreatedAt)
            .Select(i => new PmVendorItemDto(i.Id, i.VendorId, i.Kind, i.Title, i.Description, i.Impact, i.Probability, i.ImpactLevel, i.MilestoneId, i.Status, i.PmComment,
                i.CreatedRecordId, i.CreatedAt, i.Version)).ToListAsync(ct).ConfigureAwait(false);
        var ratings = await register.RatingsAsync(r => r.ProjectId == projectId, ct).ConfigureAwait(false);
        var milestoneRefs = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled)
            .OrderBy(m => m.Sort).ThenBy(m => m.Code).Select(m => new WaselMilestoneRefDto(m.Id, m.Code, m.TitleAr, m.TitleEn, m.IsHeader, m.VendorId, m.Weight))
            .ToListAsync(ct).ConfigureAwait(false);
        return new WaselPanelDto(project.MilestoneUpdateMode, canManage, assignmentDtos, proposals, submissions, deliverables, vendorItems, ratings, milestoneRefs);
    }

    public async Task<Result<WaselPanelDto>> SetModeAsync(Guid projectId, UpdateModeInput input, CancellationToken ct)
    {
        if (!UpdateModes.All.Contains(input.Mode))
        {
            return Error.Validation([new FieldError("mode", "validation.unknownCode", "validation.unknownCode")]);
        }

        var (project, error) = await ProjectAsync(projectId, manage: true, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        db.Entry(project).Property(x => x.Version).OriginalValue = input.Version;
        project.SetUpdateMode(input.Mode); // audited field change (SPEC §1.18 "switching is logged")
        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return await PanelAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<WaselPanelDto>> AssignAsync(Guid projectId, VendorAssignmentInput input, CancellationToken ct)
    {
        var (project, error) = await ProjectAsync(projectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (project.Status is ProjectStatuses.Cancelled or ProjectStatuses.Completed)
        {
            return ProjectService.NotInStage(project);
        }

        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == input.VendorId, ct).ConfigureAwait(false);
        var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
        var rule = input.ExpiryRule ?? settings.Wasel.ExpiryRule;
        var errors = new List<FieldError>();
        if (vendor is null || !vendor.IsActive)
        {
            errors.Add(new FieldError("vendorId", "portfolio.errors.vendorNotActive", "portfolio.errors.vendorNotActive"));
        }

        if (!ExpiryRules.All.Contains(rule))
        {
            errors.Add(new FieldError("expiryRule", "validation.unknownCode", "validation.unknownCode"));
        }

        Contract? contract = null;
        if (input.ContractId is { } contractId)
        {
            contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && c.ProjectId == projectId, ct).ConfigureAwait(false);
            if (contract is null)
            {
                errors.Add(new FieldError("contractId", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        var until = input.AccessUntil ?? (rule == ExpiryRules.ContractEnd ? contract?.EndDate : null);
        if (until is null && rule != ExpiryRules.ProjectClosure)
        {
            errors.Add(new FieldError("accessUntil", "portfolio.errors.accessUntilRequired", "portfolio.errors.accessUntilRequired"));
        }

        if (until is { } u && u < await reader.TodayAsync(ct).ConfigureAwait(false))
        {
            errors.Add(new FieldError("accessUntil", "portfolio.errors.accessUntilPast", "portfolio.errors.accessUntilPast"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var assignment = await db.VendorAssignments.FirstOrDefaultAsync(a => a.ProjectId == projectId && a.VendorId == input.VendorId && a.Status == AssignmentStatuses.Active, ct)
            .ConfigureAwait(false);
        var validTo = await grants.ValidToAsync(until, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            assignment = VendorAssignment.Create(projectId, input.VendorId, input.ContractId, until, rule);
            db.VendorAssignments.Add(assignment);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            assignment.Extend(until, input.ContractId);
            foreach (var member in await db.VendorMembers.Where(m => m.AssignmentId == assignment.Id && m.Status == VendorMemberStatuses.Active).ToListAsync(ct).ConfigureAwait(false))
            {
                member.SetValidTo(validTo);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await grants.RevokeAsync(member, ct).ConfigureAwait(false);
                await grants.GrantAsync(member, ct).ConfigureAwait(false);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return await PanelAsync(projectId, ct).ConfigureAwait(false);
    }

    /// <summary>Invites a vendor user (new or existing account) to the project with a vendor role (SPEC §1.18 "invited by email by the PM / PM Office").</summary>
    public async Task<Result<WaselPanelDto>> InviteAsync(Guid projectId, InvitationInput input, CancellationToken ct)
    {
        var (project, error) = await ProjectAsync(projectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 300 || !VendorRegisterService.IsEmail(input.Email))
        {
            errors.Add(new FieldError("email", "validation.email", "validation.email"));
        }

        if (!VendorRoles.All.Contains(input.Role))
        {
            errors.Add(new FieldError("role", "validation.unknownCode", "validation.unknownCode"));
        }

        if (input.Name is { Length: > 300 })
        {
            errors.Add(new FieldError("name", "validation.maximumLength", "validation.maximumLength"));
        }

        var assignment = await db.VendorAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.VendorId == input.VendorId && a.Status == AssignmentStatuses.Active, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            errors.Add(new FieldError("vendorId", "portfolio.errors.vendorNotAssigned", "portfolio.errors.vendorNotAssigned"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var email = input.Email.Trim().ToLowerInvariant();
        var account = await accounts.EnsureAsync(email, input.Language, ct).ConfigureAwait(false);
        if (account.IsFailure)
        {
            return account.Error;
        }

        var vendorUser = await db.VendorUsers.FirstOrDefaultAsync(u => u.UserId == account.Value.UserId, ct).ConfigureAwait(false);
        if (vendorUser is not null && vendorUser.VendorId != input.VendorId)
        {
            return Error.Validation([new FieldError("email", "portfolio.errors.vendorUserOtherVendor", "portfolio.errors.vendorUserOtherVendor")]);
        }

        if (vendorUser is not null && !vendorUser.MayEnter)
        {
            return Error.Validation([new FieldError("email", "portfolio.errors.vendorUserDisabled", "portfolio.errors.vendorUserDisabled")]);
        }

        if (vendorUser is null)
        {
            vendorUser = VendorUser.Create(input.VendorId, account.Value.UserId, email, input.Name, input.Language is "ar" or "en" ? input.Language : null, clock.UtcNow,
                context.User.UserId());
            db.VendorUsers.Add(vendorUser);
        }

        var existing = await db.VendorMembers.FirstOrDefaultAsync(m => m.AssignmentId == assignment!.Id && m.VendorUserId == vendorUser.Id && m.Status == VendorMemberStatuses.Active,
            ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.End(VendorMemberStatuses.Revoked, "role-changed");
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await grants.RevokeAsync(existing, ct).ConfigureAwait(false);
        }

        var member = VendorMember.Create(assignment!, vendorUser, input.Role, await grants.ValidToAsync(assignment!.AccessUntil, ct).ConfigureAwait(false));
        db.VendorMembers.Add(member);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await grants.GrantAsync(member, ct).ConfigureAwait(false);

        var inviter = context.User.Identity?.Name ?? string.Empty;
        var projectName = $"{project.Number} — {project.NameEn ?? project.NameAr}";
        if (!account.Value.HasPassword)
        {
            await accounts.InviteAsync(account.Value.UserId, inviter, projectName, Origin(), ct).ConfigureAwait(false);
        }
        else
        {
            await notifier.ToVendorUsersAsync(PortfolioNotifications.WaselAssigned, project, [vendorUser], ct, input.Role == VendorRoles.Lead ? "Vendor Project Lead / قائد مشروع المورد"
                : "Vendor Contributor / مساهم من المورد", inviter).ConfigureAwait(false);
        }

        return await PanelAsync(projectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<WaselPanelDto>> RevokeMemberAsync(Guid memberId, ReasonOnlyInput input, CancellationToken ct)
    {
        var member = await db.VendorMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct).ConfigureAwait(false);
        if (member is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(member.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        if (member.IsActive)
        {
            member.End(VendorMemberStatuses.Revoked, input.Reason);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await grants.RevokeAsync(member, ct).ConfigureAwait(false);
            var user = await db.VendorUsers.AsNoTracking().FirstAsync(u => u.Id == member.VendorUserId, ct).ConfigureAwait(false);
            await notifier.ToVendorUsersAsync(PortfolioNotifications.WaselAccessEnded, project, [user], ct, input.Reason).ConfigureAwait(false);
        }

        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    public async Task<Result<WaselPanelDto>> RevokeAssignmentAsync(Guid assignmentId, ReasonOnlyInput input, CancellationToken ct)
    {
        var assignment = await db.VendorAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(assignment.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            return Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]);
        }

        await EndAssignmentAsync(project, assignment, AssignmentStatuses.Revoked, VendorMemberStatuses.Revoked, input.Reason!, ct).ConfigureAwait(false);
        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Shared by revoke and the access job: ends the assignment, its members and their record roles, and tells the vendor.</summary>
    internal async Task EndAssignmentAsync(Project project, VendorAssignment assignment, string status, string memberStatus, string reason, CancellationToken ct)
    {
        if (!assignment.IsActive)
        {
            return;
        }

        assignment.End(status, reason, clock.UtcNow);
        var members = await db.VendorMembers.Where(m => m.AssignmentId == assignment.Id && m.Status == VendorMemberStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        foreach (var member in members)
        {
            member.End(memberStatus, reason);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var member in members)
        {
            await grants.RevokeAsync(member, ct).ConfigureAwait(false);
        }

        var userIds = members.Select(m => m.VendorUserId).ToList();
        var users = await db.VendorUsers.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync(ct).ConfigureAwait(false);
        await notifier.ToVendorUsersAsync(PortfolioNotifications.WaselAccessEnded, project, users, ct, reason).ConfigureAwait(false);
    }

    public async Task<Result<WaselPanelDto>> AssignMilestoneAsync(Guid milestoneId, MilestoneVendorInput input, CancellationToken ct)
    {
        var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId, ct).ConfigureAwait(false);
        if (milestone is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(milestone.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (milestone.IsHeader || milestone.HighLevel)
        {
            return Error.Rule("portfolio.wasel.milestoneNotLeaf", "portfolio.errors.vendorMilestoneLeaf");
        }

        if (input.VendorId is { } vendorId && !await db.VendorAssignments.AnyAsync(a => a.ProjectId == project.Id && a.VendorId == vendorId && a.Status == AssignmentStatuses.Active, ct)
                .ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("vendorId", "portfolio.errors.vendorNotAssigned", "portfolio.errors.vendorNotAssigned")]);
        }

        milestone.AssignVendor(input.VendorId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- plan proposals (§51)

    public async Task<Result<WaselPanelDto>> DecideProposalAsync(Guid proposalId, bool accept, ProposalDecisionInput input, CancellationToken ct)
    {
        var proposal = await db.PlanProposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct).ConfigureAwait(false);
        if (proposal is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(proposal.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!accept && string.IsNullOrWhiteSpace(input.Comment))
        {
            return Error.Validation([new FieldError("comment", "validation.notEmpty", "validation.notEmpty")]);
        }

        db.Entry(proposal).Property(x => x.Version).OriginalValue = input.Version;
        if (!accept)
        {
            var moved = proposal.Move(ProposalActions.Return, clock.UtcNow, context.User.UserId());
            if (moved.IsFailure)
            {
                return moved.Error;
            }

            proposal.Decide(input.Comment, null, null);
            if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
            {
                return conflict;
            }

            await notifier.ToVendorAsync(PortfolioNotifications.WaselProposalDecided, project, proposal.VendorId, ct, input.Comment,
                stage: new BilingualText("معادة للتعديل", "Returned")).ConfigureAwait(false);
            return await PanelAsync(project.Id, ct).ConfigureAwait(false);
        }

        if (!PlanProposalLifecycle.Next(proposal.Status, ProposalActions.Accept).IsSuccess)
        {
            return PlanProposalLifecycle.Next(proposal.Status, ProposalActions.Accept).Error!;
        }

        if (!AgileService.CanPlan(project))
        {
            return ProjectService.NotInStage(project);
        }

        var vendorItems = ProposalRules.Parse(proposal.Items);
        var final = input.Items ?? vendorItems;
        var remaining = await RemainingWeightAsync(project, proposal.Kind, ct).ConfigureAwait(false);
        var errors = ProposalRules.Validate(final, proposal.Kind, remaining, project.PlannedStart, project.PlannedEnd);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var diff = ProposalRules.Diff(vendorItems, final);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var created = proposal.Kind == Methodologies.Agile
            ? await CreateAgileAsync(project, final, ct).ConfigureAwait(false)
            : await CreateWaterfallAsync(project, proposal.VendorId, final, ct).ConfigureAwait(false);
        if (created is { } failure)
        {
            return failure;
        }

        proposal.Move(ProposalActions.Accept, clock.UtcNow, context.User.UserId());
        proposal.Decide(input.Comment, ProposalRules.Serialize(final), diff.Count == 0 ? null : ProposalRules.Serialize(diff));
        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } saveConflict)
        {
            return saveConflict;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await notifier.ToVendorAsync(PortfolioNotifications.WaselProposalDecided, project, proposal.VendorId, ct, input.Comment,
            stage: diff.Count == 0 ? new BilingualText("مقبولة", "Accepted") : new BilingualText("مقبولة مع تعديلات", "Accepted with changes")).ConfigureAwait(false);
        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    internal async Task<decimal> RemainingWeightAsync(Project project, string kind, CancellationToken ct)
    {
        var used = kind == Methodologies.Agile
            ? await db.Epics.Where(e => e.ProjectId == project.Id && e.MilestoneId == null && e.Status != "cancelled").SumAsync(e => (decimal?)e.Weight, ct).ConfigureAwait(false)
            : await db.Milestones.Where(m => m.ProjectId == project.Id && !m.IsHeader && !m.HighLevel && m.Status != MilestoneStatuses.Cancelled)
                .SumAsync(m => (decimal?)m.Weight, ct).ConfigureAwait(false);
        return 1m - (used ?? 0m);
    }

    private async Task<Error?> CreateWaterfallAsync(Project project, Guid vendorId, IReadOnlyList<ProposalItem> final, CancellationToken ct)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var phase in final.Where(i => i.Type == ProposalItemTypes.Phase))
        {
            var created = await milestones.CreateAsync(project.Id, new MilestoneInput(new MilestoneValues(phase.TitleAr, phase.TitleEn, null, true, false, phase.PlannedStart,
                phase.PlannedEnd, 0, null, false, null, null, null, false, null)), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return created.Error;
            }

            ids[phase.Key] = created.Value.Id;
        }

        foreach (var item in final.Where(i => i.Type == ProposalItemTypes.Milestone))
        {
            var created = await milestones.CreateAsync(project.Id, new MilestoneInput(new MilestoneValues(item.TitleAr, item.TitleEn,
                item.ParentKey is { } parent ? ids[parent] : null, false, false, item.PlannedStart, item.PlannedEnd, item.Weight ?? 0, null, item.IsDeliverable,
                item.MainDeliverable, item.AcceptanceCriteria, null, false, null)), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return created.Error;
            }

            var tracked = await db.Milestones.FirstAsync(m => m.Id == created.Value.Id, ct).ConfigureAwait(false);
            tracked.AssignVendor(vendorId);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return null;
    }

    private async Task<Error?> CreateAgileAsync(Project project, IReadOnlyList<ProposalItem> final, CancellationToken ct)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var epic in final.Where(i => i.Type == ProposalItemTypes.Epic))
        {
            var before = await db.Epics.Where(e => e.ProjectId == project.Id).Select(e => e.Id).ToListAsync(ct).ConfigureAwait(false);
            var saved = await agile.SaveEpicAsync(project.Id, null, new EpicInput(new EpicValues(epic.TitleAr, epic.TitleEn, epic.Weight ?? 0, null, null, epic.AcceptanceCriteria)),
                ct).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            ids[epic.Key] = await db.Epics.Where(e => e.ProjectId == project.Id && !before.Contains(e.Id)).Select(e => e.Id).FirstAsync(ct).ConfigureAwait(false);
        }

        foreach (var story in final.Where(i => i.Type == ProposalItemTypes.Story))
        {
            var saved = await agile.SaveStoryAsync(project.Id, null, new StoryInput(new StoryValues(ids[story.ParentKey!], story.TitleEn ?? story.TitleAr, story.AsA, story.IWant,
                story.SoThat, story.AcceptanceCriteria, story.StoryPoints ?? 0, null, null, null)), ct).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return saved.Error;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- submissions (§52)

    public async Task<Result<WaselPanelDto>> ReviewItemAsync(Guid submissionId, Guid itemId, bool accept, ItemReviewInput input, CancellationToken ct)
    {
        var submission = await db.VendorSubmissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct).ConfigureAwait(false);
        var item = submission is null ? null : await db.VendorSubmissionItems.FirstOrDefaultAsync(i => i.Id == itemId && i.SubmissionId == submissionId, ct).ConfigureAwait(false);
        if (submission is null || item is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(submission.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (submission.Status != SubmissionStatuses.Submitted || item.ReviewStatus != ItemReviewStatuses.Pending)
        {
            return Error.Rule("portfolio.wasel.notReviewable", "portfolio.errors.waselNotReviewable");
        }

        db.Entry(item).Property(x => x.Version).OriginalValue = input.Version;
        var now = clock.UtcNow;
        if (!accept)
        {
            if (string.IsNullOrWhiteSpace(input.Comment) || input.Comment.Length > MilestoneRules.TextMaxLength)
            {
                return Error.Validation([new FieldError("comment", "validation.notEmpty", "validation.notEmpty")]);
            }

            item.Return(input.Comment, context.User.UserId(), now);
        }
        else
        {
            var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == item.MilestoneId, ct).ConfigureAwait(false);
            if (milestone is null || milestone.UpdateState == UpdateStates.PendingApproval)
            {
                return Error.Rule("portfolio.milestone.notUpdatable", "portfolio.errors.milestoneNotUpdatable");
            }

            var final = input.Values ?? item.Values;
            var today = await reader.TodayAsync(ct).ConfigureAwait(false);
            var evidence = await EvidenceCountAsync(milestone.Id, ct).ConfigureAwait(false);
            var values = new MilestoneProgressValues(final.Achieve, final.ActualStart, final.ActualEnd, final.Causality, final.CorrectiveActions, final.ProgressNote);
            var errors = MilestoneRules.ValidateProgress(values, milestone.Weight, milestone.MaxAchieved, milestone.PlannedEnd, today, evidence);
            if (errors.Count > 0)
            {
                return Error.Validation(errors);
            }

            var diff = input.Values is null ? [] : VendorDiff.Of(item.Values, final);
            item.Accept(final, diff.Count == 0 ? null : JsonSerializer.Serialize(diff, ProposalRules.Json), context.User.UserId(), now);
            milestone.RecordVendorProgress(values, item.Id, diff.Count > 0);
        }

        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        // Every item reviewed → the submission is accepted, or returned when any item was returned.
        var all = await db.VendorSubmissionItems.Where(i => i.SubmissionId == submissionId).ToListAsync(ct).ConfigureAwait(false);
        if (all.All(i => i.ReviewStatus != ItemReviewStatuses.Pending))
        {
            var returned = all.Any(i => i.ReviewStatus == ItemReviewStatuses.Returned);
            submission.Move(returned ? SubmissionActions.SomeReturned : SubmissionActions.AllAccepted, now, context.User.UserId());
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await notifier.ToVendorAsync(PortfolioNotifications.WaselSubmissionReviewed, project, submission.VendorId, ct, submission.Period,
                stage: returned ? new BilingualText("معاد للتصحيح", "Returned for correction") : new BilingualText("مقبول من مدير المشروع", "Accepted by the PM"))
                .ConfigureAwait(false);
        }

        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    public async Task<Result<WaselPanelDto>> ShareOutcomeAsync(Guid submissionId, ShareOutcomeInput input, CancellationToken ct)
    {
        var submission = await db.VendorSubmissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(submission.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (string.IsNullOrWhiteSpace(input.Note) || input.Note.Length > 4000)
        {
            return Error.Validation([new FieldError("note", "validation.notEmpty", "validation.notEmpty")]);
        }

        var window = await db.MonthlyUpdates.AsNoTracking().FirstOrDefaultAsync(m => m.Id == submission.MonthlyUpdateId, ct).ConfigureAwait(false);
        var outcome = window?.Status switch
        {
            MonthlyStatuses.Approved => "approved",
            MonthlyStatuses.Returned => "returned",
            _ => submission.SharedOutcome,
        };
        submission.Share(outcome, input.Note, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.ToVendorAsync(PortfolioNotifications.WaselOutcomeShared, project, submission.VendorId, ct, input.Note, stage: OutcomeText(outcome), due: submission.Period)
            .ConfigureAwait(false);
        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    internal static BilingualText OutcomeText(string? outcome) => outcome switch
    {
        "approved" => new BilingualText("معتمد", "Approved"),
        "returned" => new BilingualText("معاد للتصحيح", "Returned"),
        "rejected" => new BilingualText("مرفوض", "Rejected"),
        _ => new BilingualText("قيد الاعتماد", "In approval"),
    };

    // ---------------------------------------------------------------- deliverables, proposed items, meetings

    public async Task<Result<WaselPanelDto>> DecideDeliverableAsync(Guid deliverableId, bool accept, WaselDecisionInput input, CancellationToken ct)
    {
        var deliverable = await db.VendorDeliverables.FirstOrDefaultAsync(d => d.Id == deliverableId, ct).ConfigureAwait(false);
        if (deliverable is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(deliverable.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!accept && string.IsNullOrWhiteSpace(input.Comment))
        {
            return Error.Validation([new FieldError("comment", "validation.notEmpty", "validation.notEmpty")]);
        }

        db.Entry(deliverable).Property(x => x.Version).OriginalValue = input.Version;
        var decided = deliverable.Decide(accept, input.Comment, context.User.UserId(), clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        await notifier.ToVendorAsync(PortfolioNotifications.WaselDeliverableDecided, project, deliverable.VendorId, ct, deliverable.Title,
            stage: accept ? new BilingualText("مقبول", "Accepted") : new BilingualText("معاد مع ملاحظات", "Returned with comments")).ConfigureAwait(false);
        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Accepting a vendor draft creates the real record through the normal services (RAID issue / challenge / project risk).</summary>
    public async Task<Result<WaselPanelDto>> DecideItemAsync(Guid itemId, bool accept, WaselDecisionInput input, CancellationToken ct)
    {
        var item = await db.VendorItems.FirstOrDefaultAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(item.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (item.Status != VendorItemStatuses.Proposed)
        {
            return Error.Rule("portfolio.wasel.decided", "portfolio.errors.waselAlreadyDecided");
        }

        if (!accept && string.IsNullOrWhiteSpace(input.Comment))
        {
            return Error.Validation([new FieldError("comment", "validation.notEmpty", "validation.notEmpty")]);
        }

        Guid? recordId = null;
        if (accept)
        {
            var settings = await reader.SettingsAsync(ct).ConfigureAwait(false);
            var text = item.Description is null ? item.Title : $"{item.Title}\n{item.Description}";
            if (item.Kind == VendorItemKinds.Risk)
            {
                var risk = await items.SaveRiskAsync(project.Id, null, new RiskInput(new ProjectRiskValues(item.Title, "vendor", item.Probability ?? 3, item.ImpactLevel ?? 3,
                    item.Description, null, null, null, null, null, null, null, null)), ct).ConfigureAwait(false);
                if (risk.IsFailure)
                {
                    return risk.Error;
                }

                recordId = risk.Value.Id;
            }
            else if (item.Kind == VendorItemKinds.Issue && settings.Features.Raid)
            {
                var issue = await raid.SaveAsync(project.Id, null, new RaidInput(new RaidValues(RaidTypes.Issue, item.Title, item.Description, null, "medium", null, null, null,
                    item.MilestoneId, null, null, null, null)), ct).ConfigureAwait(false);
                if (issue.IsFailure)
                {
                    return issue.Error;
                }

                recordId = issue.Value.Id;
            }
            else
            {
                var challenge = await items.SaveChallengeAsync(project.Id, null, new ChallengeInput(new ChallengeValues(text, item.Impact, null, null,
                    await reader.TodayAsync(ct).ConfigureAwait(false), item.MilestoneId)), ct).ConfigureAwait(false);
                if (challenge.IsFailure)
                {
                    return challenge.Error;
                }

                recordId = challenge.Value.Id;
            }
        }

        var tracked = await db.VendorItems.FirstAsync(i => i.Id == itemId, ct).ConfigureAwait(false);
        db.Entry(tracked).Property(x => x.Version).OriginalValue = input.Version;
        tracked.Decide(accept, input.Comment, recordId);
        if (await TrySaveAsync(ct).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    public async Task<Result<WaselPanelDto>> ShareMeetingAsync(Guid meetingId, MeetingVendorInput input, CancellationToken ct)
    {
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(meeting.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (input.VendorId is { } vendorId && !await db.VendorAssignments.AnyAsync(a => a.ProjectId == project.Id && a.VendorId == vendorId && a.Status == AssignmentStatuses.Active, ct)
                .ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("vendorId", "portfolio.errors.vendorNotAssigned", "portfolio.errors.vendorNotAssigned")]);
        }

        meeting.ShareWithVendor(input.VendorId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await PanelAsync(project.Id, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- comments and rating

    public async Task<Result<IReadOnlyList<WaselCommentDto>>> CommentsAsync(Guid projectId, string? subjectType, Guid? subjectId, CancellationToken ct)
    {
        var (project, error) = await ProjectAsync(projectId, manage: false, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var query = db.VendorComments.AsNoTracking().Where(c => c.ProjectId == projectId);
        if (subjectType is not null)
        {
            query = query.Where(c => c.SubjectType == subjectType);
        }

        if (subjectId is { } sid)
        {
            query = query.Where(c => c.SubjectId == sid);
        }

        return await query.OrderBy(c => c.CreatedAt).Take(500)
            .Select(c => new WaselCommentDto(c.Id, c.SubjectType, c.SubjectId, c.AuthorKind, c.AuthorName, c.Body, c.CreatedAt)).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<WaselCommentDto>>> AddCommentAsync(Guid projectId, WaselCommentInput input, CancellationToken ct)
    {
        var (project, error) = await ProjectAsync(projectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (WaselComments.Invalid(input) is { } invalid)
        {
            return invalid;
        }

        var vendorId = await WaselComments.SubjectVendorAsync(db, projectId, input.SubjectType, input.SubjectId, input.VendorId, ct).ConfigureAwait(false);
        if (vendorId is null)
        {
            return Error.Validation([new FieldError("subjectId", "validation.unknownCode", "validation.unknownCode")]);
        }

        var name = context.User.Identity?.Name;
        db.VendorComments.Add(VendorComment.Create(projectId, vendorId.Value, input.SubjectType, input.SubjectId, context.User.UserId()!.Value, "internal", name, input.Body));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.ToVendorAsync(PortfolioNotifications.WaselComment, project, vendorId.Value, ct, Truncate(input.Body), name).ConfigureAwait(false);
        return await CommentsAsync(projectId, input.SubjectType, input.SubjectId, ct).ConfigureAwait(false);
    }

    /// <summary>The PM rates the vendor: per milestone any time, the overall rating at closure or later (ADR 0115).</summary>
    public async Task<Result<WaselPanelDto>> RateAsync(Guid projectId, VendorRatingInput input, CancellationToken ct)
    {
        var (project, error) = await ProjectAsync(projectId, manage: true, ct, tracked: true).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        if (!VendorRatingCalculator.Valid(input.Values))
        {
            return Error.Validation([new FieldError("values", "validation.inclusiveBetween", "validation.inclusiveBetween")]);
        }

        if (input.Values.Comment is { Length: > 4000 })
        {
            return Error.Validation([new FieldError("comment", "validation.maximumLength", "validation.maximumLength")]);
        }

        if (!await db.VendorAssignments.AnyAsync(a => a.ProjectId == projectId && a.VendorId == input.VendorId, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("vendorId", "portfolio.errors.vendorNotAssigned", "portfolio.errors.vendorNotAssigned")]);
        }

        if (input.MilestoneId is null && project.Stage is not (ProjectStages.Closure or ProjectStages.Impact or ProjectStages.Completed))
        {
            return Error.Rule("portfolio.wasel.ratingStage", "portfolio.errors.vendorRatingStage");
        }

        if (input.MilestoneId is { } mid && !await db.Milestones.AnyAsync(m => m.Id == mid && m.ProjectId == projectId, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("milestoneId", "validation.unknownCode", "validation.unknownCode")]);
        }

        db.VendorRatings.Add(VendorRating.Create(input.VendorId, projectId, input.MilestoneId, input.Values, context.User.UserId(), clock.UtcNow));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (input.MilestoneId is null && project.VendorSatisfaction is null)
        {
            var overalls = await db.VendorRatings.Where(r => r.ProjectId == projectId && r.MilestoneId == null).Select(r => r.Overall).ToListAsync(ct).ConfigureAwait(false);
            project.EditImpact(project.CostPerformance, project.CommunitySatisfaction, project.EmployeeSatisfaction, VendorRatingCalculator.Satisfaction(overalls),
                project.PreviousOperationalCost, project.ImpactNotes);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return await PanelAsync(projectId, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- AI review on the PM side (§57)

    public async Task<Result<(PmSubmissionDto Submission, IReadOnlyList<VendorReviewFlagDto> Flags)>> ReviewFactsAsync(Guid submissionId, CancellationToken ct)
    {
        var submission = await db.VendorSubmissions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId, ct).ConfigureAwait(false);
        if (submission is null)
        {
            return Error.NotFound();
        }

        var (project, error) = await ProjectAsync(submission.ProjectId, manage: true, ct).ConfigureAwait(false);
        if (project is null)
        {
            return error!;
        }

        var names = await db.Vendors.AsNoTracking().Where(v => v.Id == submission.VendorId).ToDictionaryAsync(v => v.Id, v => (v.NameAr, v.NameEn), ct).ConfigureAwait(false);
        var dto = (await SubmissionsAsync(project, names, ct, submissionId).ConfigureAwait(false)).Single();
        var flags = dto.Items.SelectMany(i => i.Flags.Select(f => new VendorReviewFlagDto(i.MilestoneId, i.Code, f))).ToList();
        return (dto, flags);
    }

    // ---------------------------------------------------------------- helpers

    internal async Task<List<PmSubmissionDto>> SubmissionsAsync(
        Project project, IReadOnlyDictionary<Guid, (string? NameAr, string? NameEn)> names, CancellationToken ct, Guid? only = null)
    {
        var rows = await db.VendorSubmissions.AsNoTracking()
            .Where(s => s.ProjectId == project.Id && s.Status != SubmissionStatuses.Draft && (only == null || s.Id == only))
            .OrderByDescending(s => s.Period).ThenByDescending(s => s.CreatedAt).Take(24).ToListAsync(ct).ConfigureAwait(false);
        var submissionIds = rows.Select(r => r.Id).ToList();
        var itemRows = await db.VendorSubmissionItems.AsNoTracking().Where(i => submissionIds.Contains(i.SubmissionId)).ToListAsync(ct).ConfigureAwait(false);
        var milestoneIds = itemRows.Select(i => i.MilestoneId).Distinct().ToList();
        var ms = await db.Milestones.AsNoTracking().Where(m => milestoneIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
        var today = await reader.TodayAsync(ct).ConfigureAwait(false);
        var assignments = await db.VendorAssignments.AsNoTracking().Where(a => a.ProjectId == project.Id).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<PmSubmissionDto>();
        foreach (var s in rows)
        {
            var assignment = assignments.Where(a => a.VendorId == s.VendorId).OrderByDescending(a => a.CreatedAt).FirstOrDefault();
            var dtoItems = new List<PmSubmissionItemDto>();
            foreach (var i in itemRows.Where(i => i.SubmissionId == s.Id))
            {
                var m = ms.GetValueOrDefault(i.MilestoneId);
                var evidence = await FilesAsync(PortfolioPermissions.MilestoneResource, i.MilestoneId, WaselFilePurposes.VendorEvidence.Key, ct).ConfigureAwait(false);
                var previous = i.Previous is null ? null : JsonSerializer.Deserialize<PreviousValuesDto>(i.Previous, ProposalRules.Json);
                var flags = m is null
                    ? []
                    : VendorInconsistencies.Flags(i.Values, m.Weight, previous?.Approved ?? m.MaxAchieved, m.PlannedStart, m.PlannedEnd, today,
                        evidence.Count + (await files.ListAsync(PortfolioPermissions.MilestoneResource, m.Id.ToString("D"), ProjectFilePurposes.Evidence.Key, ct).ConfigureAwait(false)).Count,
                        assignment is null ? null : DateOnly.FromDateTime(assignment.CreatedAt.UtcDateTime), assignment?.AccessUntil);
                VendorProgressValues? final = i.FinalAchieve is { } fa
                    ? new VendorProgressValues(fa, i.FinalActualStart, i.FinalActualEnd, i.ProgressNote, i.FinalCausality, i.FinalCorrectiveActions)
                    : null;
                dtoItems.Add(new PmSubmissionItemDto(i.Id, i.MilestoneId, m?.Code ?? string.Empty, m?.TitleAr, m?.TitleEn, m?.Weight ?? 0, m?.PlannedStart, m?.PlannedEnd, previous,
                    i.Values, final, i.ReviewStatus, ParseDiff(i.Diff), i.PmComment, evidence, flags, i.Version));
            }

            var n = names.GetValueOrDefault(s.VendorId);
            result.Add(new PmSubmissionDto(s.Id, s.VendorId, n.NameAr, n.NameEn, s.Period, s.Status, s.SubmittedAt, s.ReviewedAt, s.SharedOutcome, s.SharedNote,
                dtoItems.OrderBy(x => x.Code, StringComparer.Ordinal).ToList(), s.Version));
        }

        return result;
    }

    internal static IReadOnlyList<DiffEntry> ParseDiff(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<DiffEntry>>(json, ProposalRules.Json) ?? [];

    private static PmProposalDto ProposalDto(PlanProposal p, IReadOnlyDictionary<Guid, (string? NameAr, string? NameEn)> names)
    {
        var n = names.GetValueOrDefault(p.VendorId);
        return new PmProposalDto(p.Id, p.VendorId, n.NameAr, n.NameEn, p.Sequence, p.Status, p.Kind, ProposalRules.Parse(p.Items),
            p.AcceptedItems is null ? null : ProposalRules.Parse(p.AcceptedItems),
            p.Diff is null ? [] : JsonSerializer.Deserialize<List<ProposalDiffEntry>>(p.Diff, ProposalRules.Json) ?? [], p.VendorNote, p.PmComment, p.SubmittedAt, p.DecidedAt,
            p.Version);
    }

    internal async Task<IReadOnlyList<VendorFileDto>> FilesAsync(string ownerType, Guid ownerId, string purpose, CancellationToken ct) =>
        (await files.ListAsync(ownerType, ownerId.ToString("D"), purpose, ct).ConfigureAwait(false))
        .Select(f => new VendorFileDto(f.Id, f.Name, f.ContentType, f.SizeBytes, f.ScanStatus)).ToList();

    private async Task<int> EvidenceCountAsync(Guid milestoneId, CancellationToken ct) =>
        (await files.ListAsync(PortfolioPermissions.MilestoneResource, milestoneId.ToString("D"), null, ct).ConfigureAwait(false))
        .Count(f => f.Purpose == ProjectFilePurposes.Evidence.Key || f.Purpose == WaselFilePurposes.VendorEvidence.Key);

    private Uri? Origin() =>
        http.HttpContext is { } h && Uri.TryCreate($"{h.Request.Scheme}://{h.Request.Host}", UriKind.Absolute, out var origin) ? origin : null;

    internal static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";

    private async Task<Error?> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }
    }

    internal CostVisibility Costs => costs;
}

/// <summary>Comment subjects must belong to the project (and to the vendor's side of it).</summary>
internal static class WaselComments
{
    public static Error? Invalid(WaselCommentInput input) =>
        !CommentSubjects.All.Contains(input.SubjectType)
            ? Error.Validation([new FieldError("subjectType", "validation.unknownCode", "validation.unknownCode")])
            : string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > VendorComment.MaxLength
                ? Error.Validation([new FieldError("body", "validation.notEmpty", "validation.notEmpty")])
                : null;

    /// <summary>The vendor the thread belongs to, or null when the subject is not on this project.</summary>
    public static async Task<Guid?> SubjectVendorAsync(PortfolioDbContext db, Guid projectId, string subjectType, Guid subjectId, Guid? vendorHint, CancellationToken ct) =>
        subjectType switch
        {
            CommentSubjects.Milestone => await db.Milestones.AsNoTracking().Where(m => m.Id == subjectId && m.ProjectId == projectId).Select(m => m.VendorId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? vendorHint,
            CommentSubjects.Submission => await db.VendorSubmissions.AsNoTracking().Where(s => s.Id == subjectId && s.ProjectId == projectId).Select(s => (Guid?)s.VendorId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false),
            CommentSubjects.Proposal => await db.PlanProposals.AsNoTracking().Where(s => s.Id == subjectId && s.ProjectId == projectId).Select(s => (Guid?)s.VendorId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false),
            CommentSubjects.Deliverable => await db.VendorDeliverables.AsNoTracking().Where(s => s.Id == subjectId && s.ProjectId == projectId).Select(s => (Guid?)s.VendorId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false),
            _ => null,
        };
}
