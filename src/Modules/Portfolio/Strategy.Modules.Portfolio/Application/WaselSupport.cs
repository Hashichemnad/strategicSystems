using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Modules.Portfolio.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Files;
using Strategy.Platform.Identity.Wasel;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Notifications;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Files of the vendor portal (docs/design/portfolio.md §48). Uploaded through the Files service: allow-list, magic number, size, virus scan.</summary>
public static class WaselFilePurposes
{
    private const long MaxBytes = 20 * 1024 * 1024;

    private static readonly string[] Kinds =
        [FileKinds.Pdf, FileKinds.Docx, FileKinds.Pptx, FileKinds.Xlsx, FileKinds.Doc, FileKinds.Ppt, FileKinds.Xls, FileKinds.Png, FileKinds.Jpeg, FileKinds.Text];

    /// <summary>Vendor's evidence for a milestone update (owner = the milestone; counts as evidence once the PM accepts the value).</summary>
    public static readonly FilePurpose VendorEvidence =
        new("portfolio.vendor-evidence", PortfolioPermissions.MilestoneResource, PortfolioPermissions.SystemCode, Kinds, MaxBytes);

    public static readonly FilePurpose Deliverable =
        new("portfolio.vendor-deliverable", VendorDeliverable.EntityType, PortfolioPermissions.SystemCode, Kinds, MaxBytes);
}

/// <summary>Deliverable files: read by whoever reads the project; internal users never write them (vendors upload through Wasel).</summary>
internal sealed class DeliverableFiles(PortfolioDbContext db, ProjectLoader loader) : IFileOwnerAccess
{
    public string OwnerType => VendorDeliverable.EntityType;

    public string AttachmentPurpose => WaselFilePurposes.Deliverable.Key;

    public async Task<bool> CanReadAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(ownerId, out var id))
        {
            return false;
        }

        var projectId = await db.VendorDeliverables.AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.ProjectId).FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return projectId is { } p && await loader.FindAsync(p, user, cancellationToken).ConfigureAwait(false) is not null;
    }

    public Task<bool> CanWriteAsync(ClaimsPrincipal user, string ownerId, CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>The vendor behind the current request and its active project scope (ADR 0111).</summary>
internal sealed record VendorCaller(VendorUser User, Vendor Vendor);

internal sealed record VendorScope(VendorUser User, Vendor Vendor, Project Project, VendorAssignment Assignment, VendorMember Member)
{
    public bool IsLead => Member.Role == VendorRoles.Lead;
}

/// <summary>
/// Resolves the vendor context of a Wasel request: an active vendor user of an active vendor, an active assignment on the project, and the record-role check
/// through <see cref="IAccessService"/> (expired or revoked grants fail there). Anything else is 404, a missing action permission 403.
/// </summary>
internal sealed class WaselContext(PortfolioDbContext db, IAccessService access, IRequestContext context, IClock clock)
{
    public ClaimsPrincipal User => context.User;

    public Guid? UserId => context.User.UserId();

    public bool IsVendor => context.User.FindFirst(StrategyClaims.PrincipalType)?.Value == StrategyClaims.VendorPrincipal;

    public async Task<VendorCaller?> CallerAsync(CancellationToken ct, bool markActive = false)
    {
        if (!IsVendor || UserId is not { } userId)
        {
            return null;
        }

        var user = await db.VendorUsers.FirstOrDefaultAsync(u => u.UserId == userId, ct).ConfigureAwait(false);
        if (user is null || !user.MayEnter)
        {
            return null;
        }

        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == user.VendorId, ct).ConfigureAwait(false);
        if (vendor is null || !vendor.IsActive)
        {
            return null;
        }

        if (markActive && user.Status == VendorUserStatuses.Invited)
        {
            user.MarkActive();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new VendorCaller(user, vendor);
    }

    /// <summary>Projects the caller may open: record roles valid now (database-filtered) and an active assignment of the caller's vendor.</summary>
    public async Task<IReadOnlyList<(Project Project, VendorMember Member, VendorAssignment Assignment)>> ProjectsAsync(VendorCaller caller, CancellationToken ct)
    {
        var scoped = await access.ApplyScopeAsync(db.Projects.AsNoTracking(), context.User, WaselPermissions.ProjectRead, ProjectScope.Map, ct).ConfigureAwait(false);
        var projects = await scoped.ToListAsync(ct).ConfigureAwait(false);
        var ids = projects.Select(p => p.Id).ToList();
        var members = await db.VendorMembers.AsNoTracking()
            .Where(m => ids.Contains(m.ProjectId) && m.VendorUserId == caller.User.Id && m.Status == VendorMemberStatuses.Active).ToListAsync(ct).ConfigureAwait(false);
        var assignmentIds = members.Select(m => m.AssignmentId).ToList();
        var assignments = await db.VendorAssignments.AsNoTracking()
            .Where(a => assignmentIds.Contains(a.Id) && a.Status == AssignmentStatuses.Active && a.VendorId == caller.Vendor.Id).ToDictionaryAsync(a => a.Id, ct)
            .ConfigureAwait(false);
        return members.Where(m => assignments.ContainsKey(m.AssignmentId))
            .Select(m => (projects.First(p => p.Id == m.ProjectId), m, assignments[m.AssignmentId]))
            .OrderBy(x => x.Item1.Number, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Result<VendorScope>> ProjectAsync(Guid projectId, string permission, CancellationToken ct, bool trackedProject = false)
    {
        var caller = await CallerAsync(ct).ConfigureAwait(false);
        if (caller is null)
        {
            return Error.NotFound();
        }

        var resource = new ResourceRef(PortfolioPermissions.ProjectResource, projectId);
        if (!await access.CanAsync(context.User, WaselPermissions.ProjectRead, resource, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var member = await db.VendorMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.VendorUserId == caller.User.Id && m.Status == VendorMemberStatuses.Active)
            .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var assignment = member is null
            ? null
            : await db.VendorAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == member.AssignmentId && a.Status == AssignmentStatuses.Active
                && a.VendorId == caller.Vendor.Id, ct).ConfigureAwait(false);
        var project = await (trackedProject ? db.Projects : db.Projects.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == projectId, ct).ConfigureAwait(false);
        if (member is null || assignment is null || project is null)
        {
            return Error.NotFound();
        }

        if (permission != WaselPermissions.ProjectRead && !await access.CanAsync(context.User, permission, resource, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return new VendorScope(caller.User, caller.Vendor, project, assignment, member);
    }

    /// <summary>A leaf milestone assigned to the caller's vendor on this project, else null (404 for the caller).</summary>
    public Task<Milestone?> AssignedMilestoneAsync(VendorScope scope, Guid milestoneId, CancellationToken ct, bool tracked = false) =>
        (tracked ? db.Milestones : db.Milestones.AsNoTracking()).FirstOrDefaultAsync(
            m => m.Id == milestoneId && m.ProjectId == scope.Project.Id && m.VendorId == scope.Vendor.Id && !m.IsHeader && !m.HighLevel
                && m.Status != MilestoneStatuses.Cancelled, ct);

    public DateTimeOffset Now => clock.UtcNow;
}

/// <summary>Grants and revokes the vendor record roles (ADR 0111); validity = access end (ADR 0115).</summary>
internal sealed class WaselGrants(IRecordRoleService recordRoles, RequestReader reader, IVendorAccounts accounts, PortfolioDbContext db)
{
    /// <summary>End of access: the day after <paramref name="accessUntil"/> at midnight in the installation time zone (null = no date).</summary>
    public async Task<DateTimeOffset?> ValidToAsync(DateOnly? accessUntil, CancellationToken ct)
    {
        if (accessUntil is not { } until)
        {
            return null;
        }

        var zone = await reader.ZoneAsync(ct).ConfigureAwait(false);
        var local = until.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public Task GrantAsync(VendorMember member, CancellationToken ct) =>
        recordRoles.GrantAsync(PortfolioPermissions.ProjectResource, member.ProjectId, RecordPrincipalKinds.User, member.UserId, member.Role, null, member.ValidTo, ct);

    public async Task RevokeAsync(VendorMember member, CancellationToken ct)
    {
        foreach (var role in VendorRoles.All)
        {
            await recordRoles.RevokeAsync(PortfolioPermissions.ProjectResource, member.ProjectId, RecordPrincipalKinds.User, member.UserId, role, ct).ConfigureAwait(false);
        }

        // Sessions end when the vendor user has no other active project.
        if (!await db.VendorMembers.AnyAsync(m => m.UserId == member.UserId && m.Status == VendorMemberStatuses.Active && m.Id != member.Id, ct).ConfigureAwait(false))
        {
            await accounts.RevokeSessionsAsync(member.UserId, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>Bilingual notifications of the vendor portal (§56): vendors by e-mail (Wasel link), internal users by e-mail + in-app.</summary>
internal sealed class WaselNotifier(INotificationService notifications, ProjectNotifier projects, PortfolioDbContext db)
{
    public async Task ToVendorAsync(
        NotificationTemplateDefinition template, Project project, Guid vendorId, CancellationToken ct, string? text = null, string? actor = null,
        BilingualText? stage = null, string? due = null, bool leadsOnly = false, object? transactionContext = null)
    {
        var members = await db.VendorMembers.AsNoTracking()
            .Where(m => m.ProjectId == project.Id && m.Status == VendorMemberStatuses.Active && (!leadsOnly || m.Role == VendorRoles.Lead))
            .Join(db.VendorUsers.AsNoTracking().Where(u => u.VendorId == vendorId), m => m.VendorUserId, u => u.Id, (m, u) => u)
            .ToListAsync(ct).ConfigureAwait(false);
        await ToVendorUsersAsync(template, project, members, ct, text, actor, stage, due, transactionContext).ConfigureAwait(false);
    }

    public Task ToVendorUsersAsync(
        NotificationTemplateDefinition template, Project project, IReadOnlyCollection<VendorUser> users, CancellationToken ct, string? text = null, string? actor = null,
        BilingualText? stage = null, string? due = null, object? transactionContext = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        var recipients = users.Where(u => u.MayEnter).DistinctBy(u => u.Id)
            .Select(u => NotificationRecipient.Address(u.Email, u.Name, u.Language)).ToList();
        if (recipients.Count == 0)
        {
            return Task.CompletedTask;
        }

        return notifications.SendAsync(new NotificationRequest(
            template.Key,
            recipients,
            Values(project, text, actor, stage, due),
            $"/wasel/projects/{project.Id:D}",
            NotificationChannels.Email), transactionContext, ct);
    }

    /// <summary>The PM (and owner when asked) of the project, deep link to the Wasel tab.</summary>
    public Task ToPmAsync(NotificationTemplateDefinition template, Project project, CancellationToken ct, string? text = null, string? actor = null) =>
        projects.ToEmployeesAsync(template, project, [project.PmEmployeeId], null, ct, text, actor);

    private static Dictionary<string, string> Values(Project project, string? text, string? actor, BilingualText? stage, string? due) => new(StringComparer.Ordinal)
    {
        ["projectAr"] = project.NameAr ?? project.NameEn ?? string.Empty,
        ["projectEn"] = project.NameEn ?? project.NameAr ?? string.Empty,
        ["number"] = project.Number,
        ["actor"] = actor ?? string.Empty,
        ["text"] = text ?? string.Empty,
        ["stageAr"] = stage?.Ar ?? string.Empty,
        ["stageEn"] = stage?.En ?? string.Empty,
        ["due"] = due ?? string.Empty,
    };
}

/// <summary>Who may enter Wasel (ADR 0111): Wasel on, an active vendor user of an active vendor.</summary>
internal sealed class VendorSignInPolicy(PortfolioDbContext db, ISystemState systems) : IVendorSignInPolicy
{
    public async Task<bool> MayEnterAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await systems.IsEnabledAsync(WaselPermissions.SystemCode, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var user = await db.VendorUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken).ConfigureAwait(false);
        return user is { MayEnter: true }
            && await db.Vendors.AsNoTracking().AnyAsync(v => v.Id == user.VendorId && v.Status == VendorStatuses.Active, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Settings → Portfolio → Wasel → "UAE Pass for vendors" (ADR 0154), only while the Wasel system is on.</summary>
internal sealed class VendorUaePassPolicy(Strategy.Platform.Settings.ISettingsStore settings, ISystemState systems) : Strategy.Platform.Identity.Wasel.IVendorUaePassPolicy
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        await systems.IsEnabledAsync(WaselPermissions.SystemCode, cancellationToken).ConfigureAwait(false)
        && (await settings.GetAsync<PortfolioSettings>(cancellationToken).ConfigureAwait(false)).Wasel.UaePassForVendors;
}

/// <summary>The shared supplier register for other modules (ADR 0112).</summary>
internal sealed class VendorDirectory(PortfolioDbContext db) : IVendorDirectory
{
    public async Task<VendorSummary?> GetAsync(Guid vendorId, CancellationToken cancellationToken = default) =>
        await db.Vendors.AsNoTracking().Where(v => v.Id == vendorId)
            .Select(v => new VendorSummary(v.Id, v.Number, v.NameAr, v.NameEn, v.Status, v.TradeLicenceNo, v.Email)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<VendorSummary>> SearchAsync(string? search, bool activeOnly, CancellationToken cancellationToken = default)
    {
        var query = db.Vendors.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(v => v.Status == VendorStatuses.Active);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(v => (v.NameAr != null && EF.Functions.ILike(v.NameAr, $"%{term}%")) || (v.NameEn != null && EF.Functions.ILike(v.NameEn, $"%{term}%"))
                || EF.Functions.ILike(v.Number, $"%{term}%") || (v.TradeLicenceNo != null && EF.Functions.ILike(v.TradeLicenceNo, $"%{term}%")));
        }

        return await query.OrderBy(v => v.Number).Take(50)
            .Select(v => new VendorSummary(v.Id, v.Number, v.NameAr, v.NameEn, v.Status, v.TradeLicenceNo, v.Email)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Plan proposal item (waterfall: phase / milestone with tasks; agile: epic / story). Keys link children to parents inside the proposal.</summary>
public sealed record ProposalItem(
    string Key,
    string Type,
    string? ParentKey,
    string? TitleAr,
    string? TitleEn,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    decimal? Weight,
    bool IsDeliverable = false,
    string? MainDeliverable = null,
    string? AcceptanceCriteria = null,
    IReadOnlyList<string>? Tasks = null,
    int? StoryPoints = null,
    string? AsA = null,
    string? IWant = null,
    string? SoThat = null);

public static class ProposalItemTypes
{
    public const string Phase = "phase";
    public const string Milestone = "milestone";
    public const string Epic = "epic";
    public const string Story = "story";
}

/// <summary>One change the PM made to a proposed item before accepting (or an added / removed item).</summary>
public sealed record ProposalDiffEntry(string Key, string Field, string? Vendor, string? Pm);

/// <summary>Pure rules of plan proposals (§51), unit-tested.</summary>
public static class ProposalRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static List<FieldError> Validate(IReadOnlyList<ProposalItem> items, string kind, decimal remainingWeight, DateOnly? projectStart, DateOnly? projectEnd)
    {
        ArgumentNullException.ThrowIfNull(items);
        var errors = new List<FieldError>();
        void Add(string field, string key, IReadOnlyDictionary<string, object?>? p = null) => errors.Add(new FieldError(field, key, key, p));
        var agile = kind == Methodologies.Agile;
        var parentType = agile ? ProposalItemTypes.Epic : ProposalItemTypes.Phase;
        var leafType = agile ? ProposalItemTypes.Story : ProposalItemTypes.Milestone;
        var weighted = agile ? ProposalItemTypes.Epic : ProposalItemTypes.Milestone;
        if (items.Count == 0)
        {
            Add("items", "portfolio.errors.proposalEmpty");
            return errors;
        }

        if (items.Count > 300 || items.Any(i => string.IsNullOrWhiteSpace(i.Key) || i.Key.Length > 64)
            || items.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count() != items.Count)
        {
            Add("items", "portfolio.errors.proposalKeys");
            return errors;
        }

        var keys = items.ToDictionary(i => i.Key, StringComparer.Ordinal);
        foreach (var (item, index) in items.Select((x, i) => (x, i)))
        {
            var path = $"items[{index}]";
            if (item.Type != parentType && item.Type != leafType)
            {
                Add($"{path}.type", "validation.unknownCode");
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.TitleAr) && string.IsNullOrWhiteSpace(item.TitleEn))
            {
                Add($"{path}.titleEn", "validation.atLeastOneLanguage");
            }

            if ((item.TitleAr?.Length ?? 0) > RequestRules.NameMaxLength || (item.TitleEn?.Length ?? 0) > RequestRules.NameMaxLength)
            {
                Add($"{path}.titleEn", "validation.maximumLength");
            }

            if (item.ParentKey is { } parent && (!keys.TryGetValue(parent, out var p) || p.Type != parentType))
            {
                Add($"{path}.parentKey", "portfolio.errors.parentInvalid");
            }

            if (agile && item.Type == ProposalItemTypes.Story && item.ParentKey is null)
            {
                Add($"{path}.parentKey", "portfolio.errors.parentInvalid");
            }

            if (item.Type == weighted && (item.Weight is not { } w || w <= 0 || w > 1 || decimal.Round(w, 4) != w))
            {
                Add($"{path}.weight", "portfolio.errors.weightRange");
            }

            if (item.Type == ProposalItemTypes.Milestone)
            {
                if (item.PlannedStart is null || item.PlannedEnd is null)
                {
                    Add($"{path}.plannedStart", "validation.notEmpty");
                }
                else if (item.PlannedEnd < item.PlannedStart)
                {
                    Add($"{path}.plannedEnd", "portfolio.errors.endBeforeStart");
                }
                else if ((projectStart is { } ps && item.PlannedStart < ps) || (projectEnd is { } pe && item.PlannedEnd > pe))
                {
                    Add($"{path}.plannedEnd", "portfolio.errors.outsideProjectDates");
                }

                if (item.IsDeliverable && (string.IsNullOrWhiteSpace(item.MainDeliverable) || string.IsNullOrWhiteSpace(item.AcceptanceCriteria)))
                {
                    Add($"{path}.mainDeliverable", "portfolio.errors.deliverableFields");
                }

                if (item.Tasks is { } tasks && (tasks.Count > 50 || tasks.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > RequestRules.NameMaxLength)))
                {
                    Add($"{path}.tasks", "validation.maximumLength");
                }
            }

            if (item.Type == ProposalItemTypes.Story && item.StoryPoints is { } points && points is < 0 or > 100)
            {
                Add($"{path}.storyPoints", "validation.inclusiveBetween");
            }
        }

        var total = items.Where(i => i.Type == weighted).Sum(i => i.Weight ?? 0);
        if (total > remainingWeight)
        {
            Add("items", "portfolio.errors.weightsOver100", new Dictionary<string, object?> { ["total"] = ProjectGates.Percent(total), ["available"] = ProjectGates.Percent(remainingWeight) });
        }

        return errors;
    }

    /// <summary>Field changes between the vendor's items and the PM's accepted items, plus added / removed items.</summary>
    public static IReadOnlyList<ProposalDiffEntry> Diff(IReadOnlyList<ProposalItem> vendor, IReadOnlyList<ProposalItem> pm)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(pm);
        var diff = new List<ProposalDiffEntry>();
        var mine = pm.GroupBy(i => i.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var v in vendor)
        {
            if (!mine.TryGetValue(v.Key, out var p))
            {
                diff.Add(new ProposalDiffEntry(v.Key, "removed", Title(v), null));
                continue;
            }

            void Field(string name, string? a, string? b)
            {
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    diff.Add(new ProposalDiffEntry(v.Key, name, a, b));
                }
            }

            Field("titleAr", v.TitleAr, p.TitleAr);
            Field("titleEn", v.TitleEn, p.TitleEn);
            Field("parentKey", v.ParentKey, p.ParentKey);
            Field("plannedStart", v.PlannedStart?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                p.PlannedStart?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Field("plannedEnd", v.PlannedEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                p.PlannedEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            Field("weight", v.Weight?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                p.Weight?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            Field("storyPoints", v.StoryPoints?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                p.StoryPoints?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Field("tasks", string.Join(" | ", v.Tasks ?? []), string.Join(" | ", p.Tasks ?? []));
        }

        var theirs = vendor.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        diff.AddRange(pm.Where(p => !theirs.Contains(p.Key)).Select(p => new ProposalDiffEntry(p.Key, "added", null, Title(p))));
        return diff;
    }

    public static IReadOnlyList<ProposalItem> Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<ProposalItem>>(json, Json) ?? [];

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static string? Title(ProposalItem item) => item.TitleEn ?? item.TitleAr;
}
