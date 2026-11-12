using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Integration;
using Strategy.Platform.Integration.Sync;

namespace Strategy.UnitTests.Teams;

/// <summary>Phase 23 (ADR 0202): the Teams / Tasks sync targets and their ADoF pack parts fit together; member evaluations find their year.</summary>
public class TeamsSyncTargetsTests
{
    [Theory]
    [InlineData(new[] { "2024", "2025" }, "2026-01-21", "2025")] // the 2025 cycle is evaluated early in 2026
    [InlineData(new[] { "2024", "2025" }, "2024-03-01", "2024")]
    [InlineData(new[] { "2025" }, "2023-05-01", "2025")] // no cycle up to that year: the latest
    [InlineData(new[] { "2024", "completion", "2025" }, null, "2025")]
    public void Member_evaluation_takes_the_latest_cycle_not_after_its_evaluation_year(string[] cycles, string? evaluatedOn, string expected) =>
        TeamsSyncTargets.PickCycle(cycles, evaluatedOn is null ? null : DateTimeOffset.Parse(evaluatedOn, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);

    [Fact]
    public void No_yearly_cycle_gives_no_evaluation() => TeamsSyncTargets.PickCycle(["completion"], DateTimeOffset.UnixEpoch).ShouldBeNull();

    [Fact]
    public async Task Pack_parts_map_only_declared_fields_and_codes()
    {
        var services = new ServiceCollection().AddTeamsSyncTargets().AddTasksSyncTargets();
        var targets = services.Where(d => d.ServiceType == typeof(SyncTargetDefinition)).Select(d => (SyncTargetDefinition)d.ImplementationInstance!)
            .ToDictionary(t => t.Code, StringComparer.Ordinal);
        targets.Keys.ShouldBe(["teams.team", "teams.team_member", "teams.team_evaluation", "teams.member_evaluation", "tasks.task"], ignoreOrder: true);

        var pack = await SyncSeeder.LoadPackAsync(CancellationToken.None);
        var objects = pack.Objects.Where(o => o.TargetType is { } t && targets.ContainsKey(t)).ToList();
        objects.Select(o => o.Key).ShouldBe(["team", "team-member", "team-evaluation", "member-evaluation", "task"], ignoreOrder: true);
        foreach (var o in objects)
        {
            SyncAdministration.ValidateStructure(o).ShouldBeEmpty(o.Key);
            var target = targets[o.TargetType!];
            foreach (var f in o.Fields)
            {
                var field = target.Field(f.Target).ShouldNotBeNull($"{o.Key}.{f.Target}");
                if (field.Choices is { } choices && f.Values is { } values)
                {
                    values.Values.ShouldAllBe(code => choices.Contains(code), $"{o.Key}.{f.Target}");
                }

                if (f.Kind == FieldMappingKinds.Reference)
                {
                    (f.ReferenceType ?? field.ReferenceType).ShouldNotBeNull();
                }
            }

            foreach (var code in o.Defaults.Keys)
            {
                target.Field(code).ShouldNotBeNull($"{o.Key} default {code}");
            }

            foreach (var required in target.Fields.Where(f => f.Required))
            {
                (o.Fields.Any(f => f.Target == required.Code) || o.Defaults.ContainsKey(required.Code)).ShouldBeTrue($"{o.Key}: {required.Code}");
            }
        }

        objects.Single(o => o.Key == "task").Provisional.ShouldBeTrue();
        objects.Where(o => o.Key != "task").ShouldAllBe(o => !o.Provisional);
    }
}
