using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Sample.Application;
using Strategy.Modules.Sample.Domain;
using Strategy.Modules.Sample.Infrastructure;
using Strategy.Platform.AI;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Text;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Modules.Sample.Ai;

/// <summary>
/// The Sample module's AI plug-ins (Phase 5 worked example, docs/design/sample.md): the item form can be filled from a document, the
/// approver brief and the <c>get_record</c> tool read items through <see cref="SampleRecordSource"/>, search and similar records through
/// <see cref="SampleSearchSource"/>. Each one checks the item's read access itself (404 outside the scope).
/// </summary>
public static class SampleAiForm
{
    public const string Key = "sample.item";

    public static readonly AiFormDefinition Definition = new(Key, "sample", "بند تجريبي", "Sample item", SamplePermissions.Create,
    [
        new("nameAr", AiFieldTypes.Text, "الاسم بالعربية", "Name (Arabic)", "The item's title in Arabic", SampleItem.NameMaxLength),
        new("nameEn", AiFieldTypes.Text, "الاسم بالإنجليزية", "Name (English)", "The item's title in English", SampleItem.NameMaxLength),
        new("descriptionAr", AiFieldTypes.LongText, "الوصف بالعربية", "Description (Arabic)", "What the item is about, in Arabic", SampleItem.DescriptionMaxLength),
        new("descriptionEn", AiFieldTypes.LongText, "الوصف بالإنجليزية", "Description (English)", "What the item is about, in English", SampleItem.DescriptionMaxLength),
        new("amount", AiFieldTypes.Number, "المبلغ (درهم)", "Amount (AED)", "The total amount or budget requested, in AED"),
    ]);
}

internal sealed class SampleRecordSource(SampleDbContext db, IAccessService access, IEmployeeDirectory employees) : IAiRecordSource
{
    public string ResourceType => SamplePermissions.ResourceType;

    public async Task<AiRecordContent?> GetAsync(Guid id, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (item is null || !await access.CanAsync(user, SamplePermissions.Read, SampleScope.Map.RefOf(item), cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var owner = item.OwnerEmployeeId is { } ownerId ? await employees.GetAsync(ownerId, cancellationToken).ConfigureAwait(false) : null;
        return new AiRecordContent(ResourceType, item.Id, "sample", SampleItem.EntityType, item.Number, item.NameAr, item.NameEn, $"/sample/items/{item.Id}",
        [
            new(nameof(SampleItem.NameAr), "الاسم بالعربية", "Name (Arabic)", item.NameAr),
            new(nameof(SampleItem.NameEn), "الاسم بالإنجليزية", "Name (English)", item.NameEn),
            new(nameof(SampleItem.DescriptionAr), "الوصف بالعربية", "Description (Arabic)", item.DescriptionAr),
            new(nameof(SampleItem.DescriptionEn), "الوصف بالإنجليزية", "Description (English)", item.DescriptionEn),
            new(nameof(SampleItem.Amount), "المبلغ (درهم)", "Amount (AED)", item.Amount?.ToString("0.##", CultureInfo.InvariantCulture)),
            new(nameof(SampleItem.Status), "الحالة", "Status", item.Status),
            new(nameof(SampleItem.ApprovalStatus), "حالة الموافقة", "Approval status", item.ApprovalStatus),
        ],
        new[] { owner?.NameAr, owner?.NameEn }.Where(n => n is not null).Select(n => n!).ToList());
    }
}

internal sealed class SampleSearchSource(SampleDbContext db, IAccessService access) : IRecordSearchSource
{
    public string ResourceType => SamplePermissions.ResourceType;

    public string SystemCode => "sample";

    public string ReadPermission => SamplePermissions.Read;

    public async Task<IReadOnlyList<RecordSearchHit>> SearchAsync(string text, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var query = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Read, SampleScope.Map, cancellationToken).ConfigureAwait(false);
        var items = await query
            .Where(x => EF.Functions.ILike(x.NameAr ?? string.Empty, pattern, "\\") || EF.Functions.ILike(x.NameEn ?? string.Empty, pattern, "\\")
                || EF.Functions.ILike(x.Number ?? string.Empty, pattern, "\\"))
            .OrderByDescending(x => x.UpdatedAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(x => Hit(x, 1)).ToList();
    }

    /// <summary>Trigram similarity of the normalised names (pg_trgm, no model, no tokens), within the user's read scope.</summary>
    public async Task<IReadOnlyList<RecordSearchHit>> SimilarAsync(string text, Guid? excludeId, ClaimsPrincipal user, int limit, CancellationToken cancellationToken)
    {
        var needle = SearchText.Normalise(text);
        var query = await access.ApplyScopeAsync(db.Items.AsNoTracking(), user, SamplePermissions.Read, SampleScope.Map, cancellationToken).ConfigureAwait(false);
        var candidates = await query
            .Where(x => excludeId == null || x.Id != excludeId)
            .Select(x => new { Item = x, Score = Math.Max(EF.Functions.TrigramsSimilarity(x.NameAr ?? string.Empty, needle), EF.Functions.TrigramsSimilarity(x.NameEn ?? string.Empty, needle)) })
            .Where(x => x.Score >= 0.3)
            .OrderByDescending(x => x.Score).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return candidates.Select(x => Hit(x.Item, Math.Round(x.Score, 2))).ToList();
    }

    private RecordSearchHit Hit(SampleItem x, double score) =>
        new(ResourceType, x.Id, SystemCode, x.Number, x.NameAr, x.NameEn, x.ApprovalStatus, $"/sample/items/{x.Id}", score);
}
