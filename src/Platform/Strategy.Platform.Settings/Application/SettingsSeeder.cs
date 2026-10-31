using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Persistence;
using Strategy.Platform.Settings.Domain;
using Strategy.Platform.Settings.Infrastructure;

namespace Strategy.Platform.Settings.Application;

/// <summary>
/// Inserts code-declared lookup types (with seed values), numbering formats and feature flags when missing; names follow the
/// code, values and patterns stay as administrators set them. Then adds codes already in use (backfill sources).
/// </summary>
internal sealed class SettingsSeeder : IDatabaseSeeder
{
    public int Order => 20;

    public async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<SettingsDbContext>();

        var types = await db.LookupTypes.ToDictionaryAsync(t => t.Code, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        foreach (var definition in services.GetServices<LookupTypeDefinition>().DistinctBy(d => d.Code))
        {
            if (types.TryGetValue(definition.Code, out var type))
            {
                type.Sync(definition);
                continue;
            }

            db.LookupTypes.Add(LookupType.Create(definition.Code, definition.SystemCode, definition.NameAr, definition.NameEn, definition.ParentTypeCode, builtin: true));
            var sort = 0;
            foreach (var value in definition.Values)
            {
                db.LookupValues.Add(LookupValue.Create(definition.Code, value.Code, value.LabelAr, value.LabelEn, sort += 10, null, null));
            }
        }

        var formats = await db.NumberingFormats.ToDictionaryAsync(f => f.Key, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        foreach (var definition in services.GetServices<NumberingDefinition>().DistinctBy(d => d.Key))
        {
            if (NumberingPattern.Validate(definition.DefaultPattern) is { } problem)
            {
                throw new InvalidOperationException($"Default numbering pattern of '{definition.Key}' is invalid ({problem}).");
            }

            if (formats.TryGetValue(definition.Key, out var format))
            {
                format.Sync(definition);
            }
            else
            {
                db.NumberingFormats.Add(NumberingFormat.Create(definition));
            }
        }

        var flags = await db.FeatureFlags.ToDictionaryAsync(f => f.Code, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        foreach (var definition in services.GetServices<FeatureDefinition>().DistinctBy(d => d.Code))
        {
            if (flags.TryGetValue(definition.Code, out var flag))
            {
                flag.Sync(definition);
            }
            else
            {
                db.FeatureFlags.Add(FeatureFlag.Create(definition));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var source in services.GetServices<ILookupBackfillSource>())
        {
            var known = await db.LookupValues.Where(v => v.TypeCode == source.TypeCode).Select(v => v.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
            var max = await db.LookupValues.Where(v => v.TypeCode == source.TypeCode).MaxAsync(v => (int?)v.Sort, cancellationToken).ConfigureAwait(false) ?? 0;
            foreach (var code in (await source.GetCodesInUseAsync(cancellationToken).ConfigureAwait(false)).Except(known, StringComparer.Ordinal))
            {
                db.LookupValues.Add(LookupValue.Create(source.TypeCode, code, code, code, max += 10, null, null));
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        services.GetRequiredService<SettingsCache>().Invalidate();
    }
}
