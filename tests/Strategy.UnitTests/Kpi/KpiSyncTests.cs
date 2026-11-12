using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Kpi.Application;
using Strategy.Modules.Kpi.Contracts;
using Strategy.Modules.Kpi.Domain;
using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;
using Strategy.Platform.Settings;

namespace Strategy.UnitTests.Kpi;

/// <summary>Phase 23 KPI sync targets: the Range rule (SPEC-ADDENDUM G3 text targets with "-"), default period targets, and the ADoF pack part.</summary>
public class KpiSyncTests
{
    private static readonly ResolvedTarget NoFallback = new(TargetTypes.SingleValue, null, null, null);

    [Fact]
    public void Both_range_bounds_make_a_range_target()
    {
        var t = KpiSyncRules.Resolve(null, null, 80m, 90m, NoFallback);
        t.ShouldBe(new ResolvedTarget(TargetTypes.Range, null, 80m, 90m));
    }

    [Fact]
    public void Range_bounds_win_over_a_numeric_target_and_reversed_bounds_are_swapped()
    {
        KpiSyncRules.Resolve(null, 85m, 95m, 85m, NoFallback).ShouldBe(new ResolvedTarget(TargetTypes.Range, null, 85m, 95m));
    }

    [Fact]
    public void A_single_value_without_bounds_is_a_single_target()
    {
        KpiSyncRules.Resolve(null, 75m, 80m, null, NoFallback).ShouldBe(new ResolvedTarget(TargetTypes.SingleValue, 75m, null, null));
    }

    [Fact]
    public void An_explicit_valid_type_wins_and_an_invalid_one_is_ignored()
    {
        KpiSyncRules.Resolve(TargetTypes.SingleValue, 50m, 40m, 60m, NoFallback).ShouldBe(new ResolvedTarget(TargetTypes.SingleValue, 50m, null, null));
        KpiSyncRules.Resolve("banded", null, 40m, 60m, NoFallback).TargetType.ShouldBe(TargetTypes.Range);
    }

    [Fact]
    public void Without_own_values_the_fallback_is_used()
    {
        var plan = new ResolvedTarget(TargetTypes.Range, null, 85m, 95m);
        KpiSyncRules.Resolve(null, null, null, null, plan).ShouldBe(plan);
        KpiSyncRules.Resolve(TargetTypes.Range, null, null, null, plan).ShouldBe(plan);
        KpiSyncRules.Resolve(TargetTypes.SingleValue, null, null, null, new(TargetTypes.SingleValue, 25m, null, null)).Target.ShouldBe(25m);
    }

    [Fact]
    public void Period_default_splits_a_sum_target_and_repeats_an_average_one()
    {
        var annual = new ResolvedTarget(TargetTypes.SingleValue, 100m, null, null);
        KpiSyncRules.PeriodDefault(AggregationRules.Sum, MeasuringPeriods.Quarterly, PeriodCodes.Q2, annual).Target.ShouldBe(25m);
        KpiSyncRules.PeriodDefault(AggregationRules.Average, MeasuringPeriods.Quarterly, PeriodCodes.Q2, annual).Target.ShouldBe(100m);
        KpiSyncRules.PeriodDefault(AggregationRules.Sum, MeasuringPeriods.HalfYearly, PeriodCodes.H2, new(TargetTypes.Range, null, 80m, 90m))
            .ShouldBe(new ResolvedTarget(TargetTypes.Range, null, 40m, 45m));
    }

    [Fact]
    public void Period_default_outside_the_measuring_period_is_the_annual_target()
    {
        var annual = new ResolvedTarget(TargetTypes.SingleValue, 100m, null, null);
        KpiSyncRules.PeriodDefault(AggregationRules.Sum, MeasuringPeriods.Quarterly, PeriodCodes.FullYear, annual).ShouldBe(annual);
    }

    [Fact]
    public void A_range_result_is_recalculated_by_the_performance_calculator()
    {
        var t = KpiSyncRules.Resolve(null, null, 85m, 95m, NoFallback);
        PerformanceCalculator.Calculate(t.TargetType, Directions.Increase, t.Target, t.RangeMin, t.RangeMax, 68m).ShouldBe(new Performance(0.80m, 0.80m));
    }

    [Fact]
    public void The_pack_part_matches_the_kpi_sync_targets()
    {
        var targets = SyncPackChecks.Targets(s => s.AddKpiSyncTargets());
        var objects = SyncPackChecks.Objects("kpi.");
        objects.Select(o => o.Key).ShouldBe(["kpi-card", "kpi-plan", "kpi-result"], ignoreOrder: true);
        objects.Select(o => o.LoadOrder).ShouldAllBe(order => order >= 60 && order <= 62);
        SyncPackChecks.Check(objects, targets, KpiAccessCatalogue.Lookups);
    }

    [Fact]
    public async Task A_salesforce_kpi_result_maps_to_the_result_fields()
    {
        var targets = SyncPackChecks.Targets(s => s.AddKpiSyncTargets());
        var mapping = SyncPackChecks.Objects("kpi.").Single(o => o.Key == "kpi-result");
        var plan = Guid.CreateVersion7();
        var record = new SourceRecord(new Dictionary<string, string?>
        {
            ["Id"] = "a0K000000000001",
            ["Org_Kpi_Measure__c"] = "a0J000000000001",
            ["Quarter__c"] = "Q2",
            ["Target_Value__c"] = "25",
            ["Achieve_Value__c"] = "20.5",
            ["Performance__c"] = "82",
            ["Approved__c"] = "true",
            ["Causality__c"] = " late data ",
        });
        var mapped = await FieldMapper.MapAsync(record, mapping.Fields, mapping.Defaults, targets[KpiResult.EntityType], new SyncPackChecks.Resolver(plan), CancellationToken.None);
        mapped.Ok.ShouldBeTrue();
        mapped.Values["planId"].ShouldBe(plan);
        mapped.Values["periodCode"].ShouldBe(PeriodCodes.Q2);
        mapped.Values["status"].ShouldBe(ResultStatuses.Approved);
        mapped.Values["target"].ShouldBe(25m);
        mapped.Values["achieve"].ShouldBe(20.5m);
        mapped.Values.ShouldNotContainKey("performance");
    }
}

/// <summary>Checks a module's objects of the embedded ADoF pack against its sync-target declarations (shared by the module sync tests).</summary>
internal static class SyncPackChecks
{
    public static Dictionary<string, SyncTargetDefinition> Targets(Action<IServiceCollection> add)
    {
        var services = new ServiceCollection();
        add(services);
        return services.Where(d => d.ServiceType == typeof(SyncTargetDefinition)).Select(d => (SyncTargetDefinition)d.ImplementationInstance!)
            .ToDictionary(d => d.Code, StringComparer.Ordinal);
    }

    public static List<MappingPackObject> Objects(string targetPrefix) =>
        [.. SyncSeeder.LoadPackAsync(CancellationToken.None).GetAwaiter().GetResult().Objects.Where(o => o.TargetType?.StartsWith(targetPrefix, StringComparison.Ordinal) == true)];

    public static void Check(IReadOnlyList<MappingPackObject> objects, IReadOnlyDictionary<string, SyncTargetDefinition> targets, IEnumerable<LookupTypeDefinition> lookups)
    {
        var codes = lookups.ToDictionary(l => l.Code, l => l.Values.Select(v => v.Code).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var o in objects)
        {
            o.Provisional.ShouldBeTrue(o.Key);
            o.NotesAr.ShouldNotBeNullOrWhiteSpace(o.Key);
            o.NotesEn.ShouldNotBeNullOrWhiteSpace(o.Key);
            SyncAdministration.ValidateStructure(o).ShouldBeEmpty(o.Key);
            var target = targets.GetValueOrDefault(o.TargetType!).ShouldNotBeNull(o.Key);
            foreach (var f in o.Fields)
            {
                var field = target.Field(f.Target).ShouldNotBeNull($"{o.Key}.{f.Target}");
                var context = $"{o.Key}.{f.Source}→{f.Target}";
                switch (f.Kind)
                {
                    case FieldMappingKinds.Reference:
                        field.Kind.ShouldBeOneOf([SyncFieldKinds.Reference, SyncFieldKinds.Unit], context);
                        (f.ReferenceType ?? field.ReferenceType).ShouldNotBeNull(context);
                        break;
                    case FieldMappingKinds.Person:
                        field.Kind.ShouldBe(SyncFieldKinds.Person, context);
                        break;
                    case FieldMappingKinds.Picklist or FieldMappingKinds.RecordType:
                        f.Values.ShouldNotBeNull(context).ShouldNotBeEmpty(context);
                        foreach (var value in f.Values!.Values)
                        {
                            if (field.Kind == SyncFieldKinds.Code)
                            {
                                field.Choices!.ShouldContain(value, context);
                            }
                            else if (field.Kind is SyncFieldKinds.Lookup or SyncFieldKinds.Codes)
                            {
                                codes[field.LookupType!].ShouldContain(value, context);
                            }
                        }

                        break;
                    default:
                        field.Kind.ShouldNotBeOneOf([SyncFieldKinds.Reference, SyncFieldKinds.Person, SyncFieldKinds.Unit], context);
                        break;
                }
            }

            foreach (var (code, value) in o.Defaults)
            {
                var field = target.Field(code).ShouldNotBeNull($"{o.Key} default {code}");
                if (field.Kind == SyncFieldKinds.Code)
                {
                    field.Choices!.ShouldContain(value.GetString()!, $"{o.Key} default {code}");
                }
            }

            foreach (var required in target.Fields.Where(f => f.Required))
            {
                (o.Fields.Any(f => f.Target == required.Code) || o.Defaults.ContainsKey(required.Code) || target.Defaults.ContainsKey(required.Code))
                    .ShouldBeTrue($"{o.Key} must map {required.Code}");
            }
        }
    }

    /// <summary>Resolves every reference to one record and every person to a new employee; all lookup codes are active.</summary>
    internal sealed class Resolver(Guid reference) : IMappingResolver
    {
        public Task<Guid?> ReferenceAsync(string targetType, string externalId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(reference);

        public Task<Guid?> PersonAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(Guid.CreateVersion7());

        public Task<bool> IsActiveLookupCodeAsync(string lookupType, string code, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
