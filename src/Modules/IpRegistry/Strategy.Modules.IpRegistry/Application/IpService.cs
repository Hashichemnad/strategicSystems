using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.IpRegistry.Contracts;
using Strategy.Modules.IpRegistry.Domain;
using Strategy.Modules.IpRegistry.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Audit;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Modules.IpRegistry.Application;

public sealed record IpUnitDto(Guid Id, string Code, string? NameAr, string? NameEn);

public sealed record IpPersonDto(Guid Id, string? NameAr, string? NameEn);

public sealed record IpListItemDto(
    Guid Id, string Number, string? NameAr, string? NameEn, string? PropertyType, string? ProtectionType, string Source, string Status, IpUnitDto? OwnerUnit,
    DateOnly? RegistrationDate, DateOnly? ExpiryDate, bool ExpiringSoon, int Year, DateTimeOffset CreatedAt);

/// <summary>A contributor as the API shows it: the Emirates ID is masked for everyone (reveal is a separate, audited call — ADR 0086).</summary>
public sealed record ContributorDto(
    Guid Id, IpPersonDto? Employee, string? ExternalName, string? ExternalOrganisation, string? EmiratesIdMasked, bool HasEmiratesId, string? Role, decimal SharePercent,
    string SignatureStatus, DateTimeOffset? CertificateSentAt);

public sealed record PeriodDto(Guid Id, string Kind, DateOnly From, DateOnly? To, string RegistrationNo, Guid? CertificateFileId);

public sealed record IpCapabilities(bool File, bool RegisterInternally, bool Register, bool MoetRejection, bool Renew, bool EditContributors, bool Sensitive, bool Close);

public sealed record IpRecordDto(
    Guid Id,
    string Number,
    string? NameAr,
    string? NameEn,
    string? Description,
    string? DescriptionAr,
    string? DescriptionEn,
    string? PropertyType,
    string? ProtectionType,
    string Source,
    Guid? RequestId,
    Guid? DocumentId,
    string? DocumentNo,
    IpUnitDto? OwnerUnit,
    IpPersonDto? Requester,
    string Status,
    DateOnly? FilingDate,
    string? ApplicationNo,
    string? RegistrationNo,
    DateOnly? RegistrationDate,
    DateOnly? ExpiryDate,
    DateOnly? FirstPublicationDate,
    DateOnly? ProposedExpiry,
    int? TermYears,
    string TermFrom,
    Guid? CertificateFileId,
    bool Internal,
    string? Notes,
    string? MoetRejectionReason,
    IReadOnlyList<ContributorDto> Contributors,
    IReadOnlyList<PeriodDto> Periods,
    IpCapabilities Can,
    uint Version);

public sealed record FilingInput(DateOnly? FilingDate, string? ApplicationNo, uint Version);

public sealed record RegistrationInput(
    string? RegistrationNo, DateOnly? RegistrationDate, DateOnly? ExpiryDate, Guid? CertificateFileId, uint Version, DateOnly? FirstPublicationDate = null);

public sealed record MoetRejectionInput(string? Reason, uint Version);

public sealed record InternalInput(string? Notes, uint Version);

public sealed record RenewalInput(string? RegistrationNo, DateOnly? From, DateOnly? To, Guid? CertificateFileId, uint Version);

public sealed record ContributorInput(Guid? Id, Guid? EmployeeId, string? ExternalName, string? ExternalOrganisation, string? EmiratesId, string? Role, decimal SharePercent,
    string? SignatureStatus);

public sealed record ContributorsInput(IReadOnlyList<ContributorInput>? Contributors, uint Version);

public sealed record MoetDescriptionInput(string? DescriptionAr, string? DescriptionEn, uint Version);

public sealed record RevealDto(Guid ContributorId, string EmiratesId);

public sealed record IpDashboardDto(int Total, int Registered, int Pending, int ExpiringSoon, int Expired, IReadOnlyList<IpCountDto> ByType, IReadOnlyList<IpCountDto> ByProtection,
    IReadOnlyList<IpCountDto> ByYear, IReadOnlyList<UnitCountDto> ByUnit, IReadOnlyList<IpListItemDto> UpcomingExpiries);

public sealed record IpCountDto(string Code, int Count);

public sealed record UnitCountDto(Guid Id, string? NameAr, string? NameEn, int Count);

public sealed record IpPdf(byte[] Content, string FileName);

public sealed record IpSettingsInput(IpSettings Settings, uint Version);

/// <summary>IP register, record page, filing / registration / MOET outcome / internal registration / renewal, contributors, Emirates ID reveal, MOET form.</summary>
internal sealed class IpService(
    IpDbContext db,
    IpReader reader,
    EmiratesIds ids,
    IpNotifier notifier,
    INumberingService numbering,
    ISettingsStore settings,
    ISettingsEditor editor,
    ILookupService lookups,
    IEmployeeDirectory employees,
    IOrgDirectory org,
    IFileService files,
    IPdfRenderer renderer,
    IAuditLog audit,
    IRequestContext context,
    IClock clock)
{
    public const int SoonDays = 180;

    public async Task<PagedResult<IpListItemDto>> ListAsync(PageRequest page, string? propertyType, string? protectionType, string? status, Guid? unitId, int? year,
        int? expiringWithin, bool mine, string? search, CancellationToken ct)
    {
        var query = await reader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        if (mine && context.User.EmployeeIdOf() is { } me)
        {
            var ids = db.Contributors.Where(c => c.EmployeeId == me).Select(c => c.IpRecordId);
            query = query.Where(r => ids.Contains(r.Id) || r.RequesterEmployeeId == me);
        }

        query = propertyType is null ? query : query.Where(r => r.PropertyType == propertyType);
        query = protectionType is null ? query : query.Where(r => r.ProtectionType == protectionType);
        query = status is null ? query : query.Where(r => r.Status == status);
        query = unitId is null ? query : query.Where(r => r.OwnerUnitId == unitId);
        if (year is { } y)
        {
            var from = new DateTimeOffset(y, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var to = from.AddYears(1);
            query = query.Where(r => r.CreatedAt >= from && r.CreatedAt < to);
        }

        if (expiringWithin is > 0)
        {
            var until = clock.Today.AddDays(expiringWithin.Value);
            query = query.Where(r => r.ExpiryDate != null && r.ExpiryDate <= until);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = "%" + SearchText.Normalise(search) + "%";
            query = query.Where(r => r.SearchText != null && EF.Functions.ILike(r.SearchText, term));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(r => r.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<IpListItemDto>(await ItemsAsync(rows, ct).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    private async Task<IReadOnlyList<IpListItemDto>> ItemsAsync(IReadOnlyList<IpRecord> rows, CancellationToken ct)
    {
        var list = new List<IpListItemDto>();
        foreach (var r in rows)
        {
            list.Add(new IpListItemDto(r.Id, r.Number, r.NameAr, r.NameEn, r.PropertyType, r.ProtectionType, r.Source, r.Status, await UnitAsync(r.OwnerUnitId, ct).ConfigureAwait(false),
                r.RegistrationDate, r.ExpiryDate, r.ExpiryDate is { } e && e.DayNumber - clock.Today.DayNumber <= SoonDays && IpStatuses.IsProtected(r.Status), r.CreatedAt.Year,
                r.CreatedAt));
        }

        return list;
    }

    public async Task<Result<IpRecordDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var record = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        return record is null ? Error.NotFound() : await ToDtoAsync(record, ct).ConfigureAwait(false);
    }

    public async Task<IpRecordDto> ToDtoAsync(IpRecord r, CancellationToken ct)
    {
        var contributors = await db.Contributors.AsNoTracking().Where(c => c.IpRecordId == r.Id).OrderBy(c => c.Sort).ToListAsync(ct).ConfigureAwait(false);
        var periods = await db.Periods.AsNoTracking().Where(p => p.IpRecordId == r.Id).OrderBy(p => p.From).ToListAsync(ct).ConfigureAwait(false);
        var people = await employees.GetManyAsync(contributors.Where(c => c.EmployeeId is not null).Select(c => c.EmployeeId!.Value)
            .Append(r.RequesterEmployeeId ?? Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList(), ct).ConfigureAwait(false);
        IpPersonDto? Person(Guid? e) => e is { } k && people.TryGetValue(k, out var p) ? new IpPersonDto(p.Id, p.NameAr ?? p.NameEn, p.NameEn ?? p.NameAr) : null;
        var office = await reader.CanAsync(context.User, IpPermissions.Process, ct).ConfigureAwait(false)
            || await reader.CanAsync(context.User, IpPermissions.Manage, ct).ConfigureAwait(false);
        var sensitive = await reader.CanAsync(context.User, IpPermissions.Sensitive, ct).ConfigureAwait(false);
        var configuration = await settings.GetAsync<IpSettings>(ct).ConfigureAwait(false);
        var rule = configuration.RuleFor(r.ProtectionType);
        var can = new IpCapabilities(
            File: office && r.Status is IpStatuses.ApprovedToFile or IpStatuses.RejectedByMoet && !rule.InternalOnly,
            RegisterInternally: office && r.Status == IpStatuses.ApprovedToFile && rule.InternalOnly,
            Register: office && r.Status == IpStatuses.Filed,
            MoetRejection: office && r.Status == IpStatuses.Filed,
            Renew: office && r.Status is IpStatuses.Registered or IpStatuses.Renewed or IpStatuses.Expired && !r.Internal,
            EditContributors: (office || sensitive) && r.Status is IpStatuses.ApprovedToFile or IpStatuses.RejectedByMoet or IpStatuses.Filed,
            Sensitive: sensitive,
            Close: office && r.Status == IpStatuses.RejectedByMoet);
        return new IpRecordDto(r.Id, r.Number, r.NameAr, r.NameEn, r.Description, r.DescriptionAr, r.DescriptionEn, r.PropertyType, r.ProtectionType, r.Source, r.RequestId,
            r.DocumentId, r.DocumentNo, await UnitAsync(r.OwnerUnitId, ct).ConfigureAwait(false), Person(r.RequesterEmployeeId), r.Status, r.FilingDate, r.ApplicationNo,
            r.RegistrationNo, r.RegistrationDate, r.ExpiryDate, r.FirstPublicationDate,
            IpRules.ProposedExpiry(r.RegistrationDate ?? clock.Today, r.FirstPublicationDate, rule.TermYears, rule.TermFrom), rule.TermYears, rule.TermFrom, r.CertificateFileId, r.Internal, r.Notes,
            r.MoetRejectionReason,
            contributors.Select(c => new ContributorDto(c.Id, Person(c.EmployeeId), c.ExternalName, c.ExternalOrganisation, c.EmiratesIdMasked, c.EmiratesIdEncrypted is not null,
                c.Role, Math.Round(c.Share * 100m, 2), c.SignatureStatus, c.CertificateSentAt)).ToList(),
            periods.Select(p => new PeriodDto(p.Id, p.Kind, p.From, p.To, p.RegistrationNo, p.CertificateFileId)).ToList(), can, r.Version);
    }

    // ------------------------------------------------------------------ actions (office)

    public Task<Result<IpRecordDto>> FileAsync(Guid id, FilingInput input, CancellationToken ct) => ActAsync(id, input.Version, async (record, rule) =>
    {
        if (record.Status is not (IpStatuses.ApprovedToFile or IpStatuses.RejectedByMoet) || rule.InternalOnly)
        {
            return Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed");
        }

        if (input.FilingDate is null || string.IsNullOrWhiteSpace(input.ApplicationNo) || input.ApplicationNo.Length > 100 || input.FilingDate > clock.Today)
        {
            return Error.Validation([new FieldError("applicationNo", "validation.notEmpty", "validation.notEmpty")]);
        }

        record.File(input.FilingDate.Value, input.ApplicationNo);
        return await Task.FromResult(Result.Success()).ConfigureAwait(false);
    }, ct);

    public Task<Result<IpRecordDto>> RegisterInternallyAsync(Guid id, InternalInput input, CancellationToken ct) => ActAsync(id, input.Version, async (record, rule) =>
    {
        if (record.Status != IpStatuses.ApprovedToFile || !rule.InternalOnly)
        {
            return Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed");
        }

        var number = await numbering.NextAsync(db, IpPermissions.InternalNumbering, new NumberingContext(clock.Today), ct).ConfigureAwait(false);
        record.Register(number, clock.Today, IpRules.ProposedExpiry(clock.Today, rule.TermYears), null, internalOnly: true);
        db.Periods.Add(RegistrationPeriod.Create(record.Id, "internal", clock.Today, record.ExpiryDate, number, null));
        return Result.Success();
    }, ct, transactional: true);

    public Task<Result<IpRecordDto>> RegisterAsync(Guid id, RegistrationInput input, CancellationToken ct) => ActAsync(id, input.Version, async (record, rule) =>
    {
        if (record.Status != IpStatuses.Filed)
        {
            return Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed");
        }

        if (string.IsNullOrWhiteSpace(input.RegistrationNo) || input.RegistrationNo.Length > 100 || input.RegistrationDate is null)
        {
            return Error.Validation([new FieldError("registrationNo", "validation.notEmpty", "validation.notEmpty")]);
        }

        var published = input.FirstPublicationDate ?? record.FirstPublicationDate;
        if (published is { } p && (p > input.RegistrationDate || p > clock.Today))
        {
            return Error.Validation([new FieldError("firstPublicationDate", "ip.errors.publicationDate", "ip.errors.publicationDate")]);
        }

        if (input.ExpiryDate is null && rule.TermFrom == TermBases.Publication && rule.TermYears is not null && published is null)
        {
            // A publication-based term (copyright) needs the first publication date, or an explicit expiry from the office.
            return Error.Validation([new FieldError("firstPublicationDate", "ip.errors.publicationRequired", "ip.errors.publicationRequired")]);
        }

        var expiry = input.ExpiryDate ?? IpRules.ProposedExpiry(input.RegistrationDate.Value, published, rule.TermYears, rule.TermFrom);
        if (expiry is { } e && e <= input.RegistrationDate)
        {
            return Error.Validation([new FieldError("expiryDate", "ip.errors.expiryBeforeRegistration", "ip.errors.expiryBeforeRegistration")]);
        }

        if (input.CertificateFileId is { } certificate && !await IsRecordFileAsync(record.Id, certificate, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("certificateFileId", "ip.errors.file", "ip.errors.file")]);
        }

        record.Register(input.RegistrationNo, input.RegistrationDate.Value, expiry, input.CertificateFileId, internalOnly: false, published);
        db.Periods.Add(RegistrationPeriod.Create(record.Id, "initial", input.RegistrationDate.Value, expiry, input.RegistrationNo, input.CertificateFileId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // The certificate goes to every contributor (employees by e-mail and in-app; externals are listed for the office).
        var contributors = await db.Contributors.Where(c => c.IpRecordId == record.Id).ToListAsync(ct).ConfigureAwait(false);
        await notifier.CertificateAsync(record, contributors.Where(c => c.EmployeeId is not null).Select(c => c.EmployeeId!.Value).ToList(), db, ct).ConfigureAwait(false);
        foreach (var contributor in contributors.Where(c => c.EmployeeId is not null))
        {
            contributor.MarkCertificateSent(clock.UtcNow);
        }

        return Result.Success();
    }, ct, transactional: true);

    public Task<Result<IpRecordDto>> MoetRejectionAsync(Guid id, MoetRejectionInput input, CancellationToken ct) => ActAsync(id, input.Version, (record, _) =>
    {
        if (record.Status != IpStatuses.Filed)
        {
            return Task.FromResult<Result>(Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed"));
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000)
        {
            return Task.FromResult<Result>(Error.Validation([new FieldError("reason", "validation.notEmpty", "validation.notEmpty")]));
        }

        record.RejectByMoet(input.Reason);
        return Task.FromResult(Result.Success());
    }, ct);

    public Task<Result<IpRecordDto>> CloseAsync(Guid id, InternalInput input, CancellationToken ct) => ActAsync(id, input.Version, (record, _) =>
    {
        if (record.Status != IpStatuses.RejectedByMoet)
        {
            return Task.FromResult<Result>(Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed"));
        }

        record.SetStatus(IpStatuses.Withdrawn);
        return Task.FromResult(Result.Success());
    }, ct);

    public Task<Result<IpRecordDto>> RenewAsync(Guid id, RenewalInput input, CancellationToken ct) => ActAsync(id, input.Version, async (record, _) =>
    {
        if (record.Status is not (IpStatuses.Registered or IpStatuses.Renewed or IpStatuses.Expired) || record.Internal)
        {
            return Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed");
        }

        if (string.IsNullOrWhiteSpace(input.RegistrationNo) || input.From is null || input.To is null || input.To <= input.From)
        {
            return Error.Validation([new FieldError("to", "ip.errors.period", "ip.errors.period")]);
        }

        if (input.CertificateFileId is { } certificate && !await IsRecordFileAsync(record.Id, certificate, ct).ConfigureAwait(false))
        {
            return Error.Validation([new FieldError("certificateFileId", "ip.errors.file", "ip.errors.file")]);
        }

        record.Renew(input.RegistrationNo, input.To.Value, input.CertificateFileId);
        db.Periods.Add(RegistrationPeriod.Create(record.Id, "renewal", input.From.Value, input.To, input.RegistrationNo, input.CertificateFileId));
        return Result.Success();
    }, ct);

    public Task<Result<IpRecordDto>> SetMoetDescriptionAsync(Guid id, MoetDescriptionInput input, CancellationToken ct) => ActAsync(id, input.Version, (record, _) =>
    {
        record.SetMoetDescriptions(input.DescriptionAr, input.DescriptionEn);
        return Task.FromResult(Result.Success());
    }, ct);

    /// <summary>The office (or IP administrators) complete contributors before filing — e.g. Emirates IDs of a record created from a document.</summary>
    public async Task<Result<IpRecordDto>> SetContributorsAsync(Guid id, ContributorsInput input, CancellationToken ct)
    {
        var record = await reader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, IpPermissions.Manage, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        if (record.Status is not (IpStatuses.ApprovedToFile or IpStatuses.RejectedByMoet or IpStatuses.Filed))
        {
            return Error.Rule("ip.lifecycle.notAllowed", "ip.errors.notAllowed");
        }

        var rows = input.Contributors ?? [];
        var errors = new List<FieldError>();
        var roles = (await lookups.GetValuesAsync(IpPermissions.ContributorRoleLookup, false, ct).ConfigureAwait(false)).Select(v => v.Code).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.EmployeeId is null && string.IsNullOrWhiteSpace(row.ExternalName))
            {
                errors.Add(new FieldError($"contributors[{i}].employeeId", "ip.errors.contributorWho", "ip.errors.contributorWho"));
            }

            if (row.EmiratesId is { Length: > 0 } eid && !eid.Contains('*', StringComparison.Ordinal)
                && !System.Text.RegularExpressions.Regex.IsMatch(eid, @"^784-\d{4}-\d{7}-\d$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                errors.Add(new FieldError($"contributors[{i}].emiratesId", "requests.errors.emiratesId", "requests.errors.emiratesId"));
            }

            if (row.Role is not null && !roles.Contains(row.Role))
            {
                errors.Add(new FieldError($"contributors[{i}].role", "validation.unknownCode", "validation.unknownCode"));
            }

            if (row.SignatureStatus is not (null or SignatureStatuses.Pending or SignatureStatuses.SignedOnPaper or SignatureStatuses.Uploaded))
            {
                errors.Add(new FieldError($"contributors[{i}].signatureStatus", "validation.unknownCode", "validation.unknownCode"));
            }
        }

        if (rows.Count == 0 || !IpRules.SharesTotal100(rows.Select(r => r.SharePercent)))
        {
            errors.Add(new FieldError("contributors", "ip.errors.sharesTotal", "ip.errors.sharesTotal"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        db.Entry(record).Property(x => x.Version).OriginalValue = input.Version;
        var existing = await db.Contributors.Where(c => c.IpRecordId == record.Id).ToListAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var contributor = row.Id is { } cid ? existing.FirstOrDefault(c => c.Id == cid) : null;
            if (contributor is null)
            {
                contributor = Contributor.Create(record.Id, row.EmployeeId, row.ExternalName, row.ExternalOrganisation, row.Role, row.SharePercent / 100m, i);
                db.Contributors.Add(contributor);
            }
            else
            {
                contributor.Edit(row.EmployeeId, row.ExternalName, row.ExternalOrganisation, row.Role, row.SharePercent / 100m, i);
            }

            if (row.EmiratesId is { Length: > 0 } eid && !eid.Contains('*', StringComparison.Ordinal))
            {
                var (encrypted, last) = ids.Protect(eid);
                contributor.SetEmiratesId(encrypted, last);
            }

            contributor.SetSignature(row.SignatureStatus ?? contributor.SignatureStatus);
        }

        foreach (var removed in existing.Where(c => rows.All(r => r.Id != c.Id)))
        {
            // A contributor removed before filing: share 0 keeps the history (no hard delete).
            removed.Edit(removed.EmployeeId, removed.ExternalName, removed.ExternalOrganisation, "removed", 0, 999);
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        return await ToDtoAsync(record, ct).ConfigureAwait(false);
    }

    /// <summary>The clear Emirates ID, for IP administrators only, audited (ADR 0086).</summary>
    public async Task<Result<RevealDto>> RevealAsync(Guid id, Guid contributorId, CancellationToken ct)
    {
        var record = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, IpPermissions.Sensitive, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var contributor = await db.Contributors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contributorId && c.IpRecordId == id, ct).ConfigureAwait(false);
        if (contributor?.EmiratesIdEncrypted is null || ids.Unprotect(contributor.EmiratesIdEncrypted) is not { } plain)
        {
            return Error.NotFound();
        }

        await audit.WriteAsync(new AuditEvent("ip.emiratesId.revealed", true, IpPermissions.SystemCode, Contributor.EntityType, contributor.Id.ToString("D")), ct)
            .ConfigureAwait(false);
        return new RevealDto(contributor.Id, plain);
    }

    /// <summary>The auto-filled bilingual MOET registration form (docs/design/requests.md §4.5). Contains Emirates IDs: IP administrators only, audited.</summary>
    public async Task<Result<IpPdf>> MoetFormAsync(Guid id, CancellationToken ct)
    {
        var record = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, IpPermissions.Sensitive, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var configuration = await settings.GetAsync<IpSettings>(ct).ConfigureAwait(false);
        var branding = await settings.GetAsync<BrandingSettings>(ct).ConfigureAwait(false);
        var contributors = await db.Contributors.AsNoTracking().Where(c => c.IpRecordId == id && c.Share > 0).OrderBy(c => c.Sort).ToListAsync(ct).ConfigureAwait(false);
        var people = await employees.GetManyAsync(contributors.Where(c => c.EmployeeId is not null).Select(c => c.EmployeeId!.Value).ToList(), ct).ConfigureAwait(false);
        var propertyLabels = await lookups.GetValuesAsync(IpPermissions.PropertyTypeLookup, true, ct).ConfigureAwait(false);
        var protectionLabels = await lookups.GetValuesAsync(IpPermissions.ProtectionTypeLookup, true, ct).ConfigureAwait(false);
        var roleLabels = await lookups.GetValuesAsync(IpPermissions.ContributorRoleLookup, true, ct).ConfigureAwait(false);
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var rows = new StringBuilder();
        var index = 1;
        foreach (var c in contributors)
        {
            var person = c.EmployeeId is { } e && people.TryGetValue(e, out var p) ? p : null;
            var role = roleLabels.FirstOrDefault(r => r.Code == c.Role);
            rows.Append(CultureInfo.InvariantCulture, $"<tr><td>{index++}</td><td>{E(person?.NameAr ?? c.ExternalName)}<br/><span class=\"en\">{E(person?.NameEn ?? c.ExternalName)}</span></td>")
                .Append(CultureInfo.InvariantCulture, $"<td>{E(person is null ? c.ExternalOrganisation : branding.OrganisationNameAr)}</td>")
                .Append(CultureInfo.InvariantCulture, $"<td class=\"ltr\">{E(ids.Unprotect(c.EmiratesIdEncrypted) ?? "—")}</td>")
                .Append(CultureInfo.InvariantCulture, $"<td>{E(role?.LabelAr)}<br/><span class=\"en\">{E(role?.LabelEn)}</span></td>")
                .Append(CultureInfo.InvariantCulture, $"<td class=\"ltr\">{(c.Share * 100m).ToString("0.##", CultureInfo.InvariantCulture)}%</td><td class=\"sign\"></td></tr>");
        }

        var property = propertyLabels.FirstOrDefault(v => v.Code == record.PropertyType);
        var protection = protectionLabels.FirstOrDefault(v => v.Code == record.ProtectionType);
        var template = Template();
        var html = template
            .Replace("{{headerAr}}", E(configuration.MoetHeaderAr), StringComparison.Ordinal)
            .Replace("{{headerEn}}", E(configuration.MoetHeaderEn), StringComparison.Ordinal)
            .Replace("{{orgAr}}", E(branding.OrganisationNameAr), StringComparison.Ordinal)
            .Replace("{{orgEn}}", E(branding.OrganisationNameEn), StringComparison.Ordinal)
            .Replace("{{number}}", E(record.Number), StringComparison.Ordinal)
            .Replace("{{nameAr}}", E(record.NameAr), StringComparison.Ordinal)
            .Replace("{{nameEn}}", E(record.NameEn), StringComparison.Ordinal)
            .Replace("{{propertyAr}}", E(property?.LabelAr), StringComparison.Ordinal)
            .Replace("{{propertyEn}}", E(property?.LabelEn), StringComparison.Ordinal)
            .Replace("{{protectionAr}}", E(protection?.LabelAr), StringComparison.Ordinal)
            .Replace("{{protectionEn}}", E(protection?.LabelEn), StringComparison.Ordinal)
            .Replace("{{descriptionAr}}", E(record.DescriptionAr ?? record.Description), StringComparison.Ordinal)
            .Replace("{{descriptionEn}}", E(record.DescriptionEn ?? record.Description), StringComparison.Ordinal)
            .Replace("{{created}}", E(record.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{{today}}", E(clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), StringComparison.Ordinal)
            .Replace("{{rows}}", rows.ToString(), StringComparison.Ordinal);
        byte[] pdf;
        try
        {
            pdf = await renderer.RenderAsync(html, ct).ConfigureAwait(false);
        }
        catch (PdfRenderingException ex)
        {
            return Error.Rule("ip.pdf.failed", ex.MessageKey);
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, IpPermissions.SystemCode, IpRecord.EntityType, record.Id.ToString("D"), new { form = "moet" }), ct)
            .ConfigureAwait(false);
        return new IpPdf(pdf, $"{record.Number}-MOET.pdf");
    }

    /// <summary>
    /// The dashboard. <paramref name="from"/> / <paramref name="to"/> (inclusive month range, design note reporting.md §3.3): records filed in the
    /// range — records not filed yet count by their creation date.
    /// </summary>
    public async Task<IpDashboardDto> DashboardAsync(CancellationToken ct, DateOnly? from = null, DateOnly? to = null)
    {
        var visible = await reader.VisibleAsync(context.User, ct).ConfigureAwait(false);
        if (from is { } f)
        {
            var start = new DateOnly(f.Year, f.Month, 1);
            var startAt = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            visible = visible.Where(r => r.FilingDate != null ? r.FilingDate >= start : r.CreatedAt >= startAt);
        }

        if (to is { } t)
        {
            var end = new DateOnly(t.Year, t.Month, 1).AddMonths(1);
            var endAt = new DateTimeOffset(end.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            visible = visible.Where(r => r.FilingDate != null ? r.FilingDate < end : r.CreatedAt < endAt);
        }

        var soon = clock.Today.AddDays(SoonDays);
        var total = await visible.CountAsync(ct).ConfigureAwait(false);
        var registered = await visible.CountAsync(r => r.Status == IpStatuses.Registered || r.Status == IpStatuses.Renewed, ct).ConfigureAwait(false);
        var pending = await visible.CountAsync(r => r.Status == IpStatuses.Submitted || r.Status == IpStatuses.UnderReview || r.Status == IpStatuses.ApprovedToFile
            || r.Status == IpStatuses.Filed, ct).ConfigureAwait(false);
        var expired = await visible.CountAsync(r => r.Status == IpStatuses.Expired, ct).ConfigureAwait(false);
        var upcoming = await visible.Where(r => (r.Status == IpStatuses.Registered || r.Status == IpStatuses.Renewed) && r.ExpiryDate != null && r.ExpiryDate <= soon)
            .OrderBy(r => r.ExpiryDate).Take(20).ToListAsync(ct).ConfigureAwait(false);
        var byType = await visible.GroupBy(r => r.PropertyType).Select(g => new IpCountDto(g.Key ?? string.Empty, g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var byProtection = await visible.GroupBy(r => r.ProtectionType).Select(g => new IpCountDto(g.Key ?? string.Empty, g.Count())).ToListAsync(ct).ConfigureAwait(false);
        var years = await visible.Select(r => r.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        var byYear = years.GroupBy(d => d.Year).OrderBy(g => g.Key).Select(g => new IpCountDto(g.Key.ToString(CultureInfo.InvariantCulture), g.Count())).ToList();
        var units = await visible.Where(r => r.OwnerUnitId != null).GroupBy(r => r.OwnerUnitId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct)
            .ConfigureAwait(false);
        var byUnit = new List<UnitCountDto>();
        foreach (var u in units)
        {
            var unit = await org.GetUnitAsync(u.Key, ct).ConfigureAwait(false);
            byUnit.Add(new UnitCountDto(u.Key, unit?.NameAr, unit?.NameEn, u.Count));
        }

        return new IpDashboardDto(total, registered, pending, upcoming.Count, expired, byType, byProtection, byYear, byUnit, await ItemsAsync(upcoming, ct).ConfigureAwait(false));
    }

    public async Task<Result<FileContent>> CertificateAsync(Guid id, CancellationToken ct)
    {
        var record = await reader.FindAsync(id, context.User, ct).ConfigureAwait(false);
        return record?.CertificateFileId is not { } file ? Error.NotFound() : await files.OpenAsync(file, cancellationToken: ct).ConfigureAwait(false);
    }

    public Task<Platform.Settings.Application.SettingsDocument<IpSettings>> SettingsAsync(CancellationToken ct) => editor.GetDocumentAsync<IpSettings>(ct);

    public Task<Result<Platform.Settings.Application.SettingsDocument<IpSettings>>> SaveSettingsAsync(IpSettingsInput input, CancellationToken ct) =>
        editor.SaveAsync(input.Settings, input.Version, IpSettings.Validate, ct);

    // ------------------------------------------------------------------ helpers

    private async Task<Result<IpRecordDto>> ActAsync(Guid id, uint version, Func<IpRecord, ProtectionRule, Task<Result>> action, CancellationToken ct, bool transactional = false)
    {
        var record = await reader.FindAsync(id, context.User, ct, tracked: true).ConfigureAwait(false);
        if (record is null)
        {
            return Error.NotFound();
        }

        if (!await reader.CanAsync(context.User, IpPermissions.Process, ct).ConfigureAwait(false)
            && !await reader.CanAsync(context.User, IpPermissions.Manage, ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        db.Entry(record).Property(x => x.Version).OriginalValue = version;
        var rule = (await settings.GetAsync<IpSettings>(ct).ConfigureAwait(false)).RuleFor(record.ProtectionType);
        await using var transaction = transactional ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;
        var outcome = await action(record, rule).ConfigureAwait(false);
        if (outcome.IsFailure)
        {
            return outcome.Error!;
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        return await ToDtoAsync(record, ct).ConfigureAwait(false);
    }

    private async Task<bool> IsRecordFileAsync(Guid recordId, Guid fileId, CancellationToken ct) =>
        await files.FindAsync(fileId, ct).ConfigureAwait(false) is { } file && file.OwnerType == IpRecord.EntityType && file.OwnerId == recordId.ToString("D");

    private async Task<IpUnitDto?> UnitAsync(Guid? id, CancellationToken ct) =>
        id is { } u && await org.GetUnitAsync(u, ct).ConfigureAwait(false) is { } unit ? new IpUnitDto(unit.Id, unit.Code, unit.NameAr ?? unit.NameEn, unit.NameEn ?? unit.NameAr) : null;

    private static string Template()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Strategy.Modules.IpRegistry.Templates.moet-form.html")
            ?? throw new InvalidOperationException("MOET template missing.");
        using var text = new StreamReader(stream);
        return text.ReadToEnd();
    }

    internal static string Link(Guid id) => IpResource.Link(id);
}
