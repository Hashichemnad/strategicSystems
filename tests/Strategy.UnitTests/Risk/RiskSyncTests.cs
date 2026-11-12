using Strategy.Modules.Kpi.Infrastructure;
using Strategy.Modules.Risk.Application;
using Strategy.Modules.Risk.Domain;
using Strategy.Modules.Risk.Infrastructure;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;
using Strategy.UnitTests.Kpi;

namespace Strategy.UnitTests.Risk;

/// <summary>Phase 23 Risk sync targets: re-rating of the Salesforce additive score by the framework, control effectiveness codes, and the ADoF pack part.</summary>
public class RiskSyncTests
{
    private static RiskRecord Risk(int l, int i) =>
        RiskRecord.Create("GRD26-R001", 2026, new RiskValues("خطر", "Risk", null, null, null, null, new DateOnly(2026, 1, 10), null, RiskTypes.Internal,
            [], false, false, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, null, null, l, i, null, null, null, null, null, null, null, false));

    [Fact]
    public void The_additive_source_score_is_re_rated_by_the_framework()
    {
        // Salesforce: 4 + 5 = 9 (additive); the default framework multiplies: 4 × 5 = 20 → critical, escalated.
        var risk = Risk(4, 5);
        var scorer = new RiskScorer(DefaultFramework.Content);
        RiskSyncRules.ReRate(risk, scorer, Guid.Empty, [], appetite: null, new RiskSettings(), DateTimeOffset.UnixEpoch).ShouldBeTrue();
        risk.InherentScore.ShouldBe(20);
        risk.InherentBand.ShouldBe("critical");
        risk.ResidualBand.ShouldBe("critical");
        risk.Escalated.ShouldBeTrue();
        risk.EscalationReason.ShouldBe(EscalationReasons.Critical);
    }

    [Fact]
    public void Re_rating_twice_is_stable()
    {
        var risk = Risk(2, 2);
        var scorer = new RiskScorer(DefaultFramework.Content);
        RiskSyncRules.ReRate(risk, scorer, Guid.Empty, [], null, new RiskSettings(), DateTimeOffset.UnixEpoch).ShouldBeTrue();
        RiskSyncRules.ReRate(risk, scorer, Guid.Empty, [], null, new RiskSettings(), DateTimeOffset.UnixEpoch).ShouldBeFalse();
        risk.InherentBand.ShouldBe("low");
        risk.Escalated.ShouldBeFalse();
    }

    [Theory]
    [InlineData("effective", "effective")]
    [InlineData("partially-effective", "partially-effective")]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void Only_framework_effectiveness_codes_are_kept(string? code, string? expected) =>
        RiskSyncRules.KnownEffectiveness(new RiskScorer(DefaultFramework.Content), code).ShouldBe(expected);

    [Fact]
    public void The_pack_part_matches_the_risk_sync_targets()
    {
        var targets = SyncPackChecks.Targets(s => s.AddRiskSyncTargets().AddKpiSyncTargets());
        var objects = SyncPackChecks.Objects("risk.");
        objects.Select(o => o.Key).ShouldBe(["risk", "risk-control", "risk-action-control", "risk-kri"], ignoreOrder: true);
        objects.Select(o => o.LoadOrder).ShouldAllBe(order => order >= 70 && order <= 72);
        objects.Single(o => o.Key == "risk-action-control").Where.ShouldBe("Became_Control__c = true");
        objects.Single(o => o.Key == "risk-kri").Fields.Single(f => f.Target == "kpiCardId").ReferenceType.ShouldBe("kpi.card");
        SyncPackChecks.Check(objects, targets, RiskAccessCatalogue.Lookups);
    }

    [Fact]
    public async Task The_identification_date_falls_back_to_the_created_date()
    {
        var targets = SyncPackChecks.Targets(s => s.AddRiskSyncTargets());
        var mapping = SyncPackChecks.Objects("risk.").Single(o => o.Key == "risk");
        var record = new SourceRecord(new Dictionary<string, string?>
        {
            ["Id"] = "a0R000000000001",
            ["Name"] = "R-0001",
            ["Department__c"] = "a01000000000001",
            ["Probability_Level__c"] = "4",
            ["Impact_Level__c"] = "5",
            ["Inherent_Risk_Score__c"] = "9",
            ["Status__c"] = "Active",
            ["CreatedDate"] = "2025-03-04T10:00:00.000+0000",
            ["Identification_Date__c"] = null,
        });
        var mapped = await FieldMapper.MapAsync(record, mapping.Fields, mapping.Defaults, targets[RiskRecord.EntityType], new SyncPackChecks.Resolver(Guid.CreateVersion7()),
            CancellationToken.None);
        mapped.Ok.ShouldBeTrue();
        mapped.Values["identifiedOn"].ShouldBe(new DateOnly(2025, 3, 4));
        mapped.Values["inherentLikelihood"].ShouldBe(4);
        mapped.Values["status"].ShouldBe(RiskStatuses.Active);
        mapped.Values["riskType"].ShouldBe(RiskTypes.Internal);
    }
}
