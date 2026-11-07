using Microsoft.EntityFrameworkCore;
using Npgsql;
using Strategy.Platform.Integration.Domain;
using Strategy.Platform.Integration.Infrastructure;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;

namespace Strategy.Platform.Integration.Application;

public sealed record SourceSystemDto(
    Guid Id, string Code, string? NameAr, string? NameEn, string TypeCode, string? Description, bool Active, int ReferenceCount, uint Version);

/// <summary><c>Code</c> is set on create only (it is the stable key in files, URLs and references).</summary>
public sealed record SourceSystemInput(string? Code, string? NameAr, string? NameEn, string? TypeCode, string? Description, uint Version = 0);

public sealed record SourceSystemVersionInput(uint Version);

/// <summary>Settings → Integrations → Source systems (ADR 0164). Audited by the interceptor; never deleted (deactivated).</summary>
internal sealed class SourceSystemAdministration(IntegrationDbContext db, ILookupService lookups)
{
    public const string TypeLookup = "integration.source_type";

    public async Task<IReadOnlyList<SourceSystemDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.References.AsNoTracking().GroupBy(r => r.SourceSystem).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct).ConfigureAwait(false);
        var rows = await db.SourceSystems.AsNoTracking().Where(s => includeInactive || s.Active).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(s => ToDto(s, counts.GetValueOrDefault(s.Code))).ToList();
    }

    public async Task<Result<SourceSystemDto>> CreateAsync(SourceSystemInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code?.Trim();
        var errors = await ValidateAsync(input, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(code))
        {
            errors.Add(Field("code", "validation.notEmpty"));
        }
        else if (!SourceSystem.IsValidCode(code))
        {
            errors.Add(Field("code", "integration.errors.codeFormat"));
        }
        else if (await db.SourceSystems.AnyAsync(s => s.Code == code, ct).ConfigureAwait(false))
        {
            errors.Add(Field("code", "integration.errors.codeTaken"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var source = SourceSystem.Create(code!, Clean(input.NameAr), Clean(input.NameEn), input.TypeCode!.Trim(), Clean(input.Description));
        db.SourceSystems.Add(source);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Error.Validation([Field("code", "integration.errors.codeTaken")]);
        }

        return ToDto(source, 0);
    }

    public async Task<Result<SourceSystemDto>> UpdateAsync(Guid id, SourceSystemInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = await ValidateAsync(input, ct).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        return await ChangeAsync(id, input.Version, s => s.Update(Clean(input.NameAr), Clean(input.NameEn), input.TypeCode!.Trim(), Clean(input.Description)), ct)
            .ConfigureAwait(false);
    }

    public Task<Result<SourceSystemDto>> SetActiveAsync(Guid id, bool active, uint version, CancellationToken ct) =>
        ChangeAsync(id, version, s => s.SetActive(active), ct);

    private async Task<Result<SourceSystemDto>> ChangeAsync(Guid id, uint version, Action<SourceSystem> change, CancellationToken ct)
    {
        var source = await db.SourceSystems.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (source is null)
        {
            return Error.NotFound();
        }

        db.Entry(source).Property(x => x.Version).OriginalValue = version;
        change(source);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict();
        }

        var count = await db.References.CountAsync(r => r.SourceSystem == source.Code, ct).ConfigureAwait(false);
        return ToDto(source, count);
    }

    private async Task<List<FieldError>> ValidateAsync(SourceSystemInput input, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.NameAr) && string.IsNullOrWhiteSpace(input.NameEn))
        {
            errors.Add(Field("nameAr", "validation.atLeastOneLanguage"));
        }

        foreach (var (name, value, max) in new[] { ("nameAr", input.NameAr, SourceSystem.NameMaxLength), ("nameEn", input.NameEn, SourceSystem.NameMaxLength),
                     ("description", input.Description, SourceSystem.DescriptionMaxLength) })
        {
            if (value is { } text && text.Trim().Length > max)
            {
                errors.Add(new FieldError(name, "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = max }));
            }
        }

        if (string.IsNullOrWhiteSpace(input.TypeCode))
        {
            errors.Add(Field("typeCode", "validation.notEmpty"));
        }
        else if (!await lookups.IsActiveCodeAsync(TypeLookup, input.TypeCode.Trim(), ct).ConfigureAwait(false))
        {
            errors.Add(Field("typeCode", "validation.unknownCode"));
        }

        return errors;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FieldError Field(string field, string key) => new(field, key, key);

    private static SourceSystemDto ToDto(SourceSystem s, int count) =>
        new(s.Id, s.Code, s.NameAr, s.NameEn, s.TypeCode, s.Description, s.Active, count, s.Version);
}
