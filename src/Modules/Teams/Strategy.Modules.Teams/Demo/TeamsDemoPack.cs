using Strategy.Modules.Teams.Application;
using Strategy.Modules.Teams.Domain;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.Teams.Demo;

/// <summary>
/// Demo pack (ADR 0153): a standing committee and a temporary work team, both submitted for formation approval (the approvers are the demo
/// department director and Director General, so they show in My Work), and a draft work team. Removal: drafts and pending teams are
/// cancelled / withdrawn, operating teams closed — always with a reason.
/// </summary>
internal sealed class TeamsDemoPack : IDemoPackContributor
{
    public const string TeamType = "teams.team";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    public string Code => "teams";

    public int Order => 20;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        // Requested by staff, so the department director and the Director General approve (nobody approves their own request).
        await context.ActAsAsync("analyst").ConfigureAwait(false);
        var committee = await CreateAsync(context, "standing-committee", "لجنة الحوكمة والأداء (عرض)", "Governance and Performance Committee (demo)", true, null,
            [("director", "president"), ("strategyHead", "deputy"), ("financeHead", "member"), ("analyst", "secretary")], "demo.team.committee", cancellationToken)
            .ConfigureAwait(false);
        (await context.SendAsync(new SubmitTeam(committee.Id, committee.Version, null), cancellationToken).ConfigureAwait(false)).Require("submit committee");

        await context.ActAsAsync("pm").ConfigureAwait(false);
        var team = await CreateAsync(context, "work-team", "فريق التحول الرقمي (عرض)", "Digital Transformation Work Team (demo)", false, context.Today.AddMonths(6),
            [("pm", "president"), ("analyst", "member"), ("accountant", "member")], "demo.team.work", cancellationToken).ConfigureAwait(false);
        (await context.SendAsync(new SubmitTeam(team.Id, team.Version, null), cancellationToken).ConfigureAwait(false)).Require("submit work team");

        await CreateAsync(context, "work-team", "فريق تحسين الإجراءات (مسودة عرض)", "Process Improvement Team (demo draft)", false, context.Today.AddMonths(3),
            [("analyst", "president"), ("auditor", "member")], null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TeamDto> CreateAsync(DemoPackContext context, string type, string nameAr, string nameEn, bool permanent, DateOnly? end,
        (string Person, string Role)[] members, string? key, CancellationToken cancellationToken)
    {
        var input = new TeamInput(type, nameAr, nameEn, permanent, "Demo: " + nameEn, "تحسين الأداء المؤسسي / Improve institutional performance",
            "مراجعة ورفع التوصيات / Review and recommend", context.Units["strategy"], context.Person("director").EmployeeId, null, "generate", null,
            context.Today, end, null, null, "medium", null, null, null,
            members.Select(m => new MemberInput(null, context.Person(m.Person).EmployeeId, null, null, null, null, m.Role, null, null, null, null)).ToList());
        var created = (await context.SendAsync(new CreateTeam(input), cancellationToken).ConfigureAwait(false)).Require("team " + nameEn);
        await context.TrackAsync(TeamType, created.Id, key, cancellationToken: cancellationToken).ConfigureAwait(false);
        return created;
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var record in await context.TrackedAsync(TeamType, cancellationToken).ConfigureAwait(false))
        {
            await context.ActAsAsync(record).ConfigureAwait(false);
            var team = await context.SendAsync(new GetTeam(record.ResourceId), cancellationToken).ConfigureAwait(false);
            if (team.IsFailure || TeamStatuses.IsFinished(team.Value.Status))
            {
                continue;
            }

            var action = team.Value.Status is TeamStatuses.Draft or TeamStatuses.PendingApproval ? TeamActions.Cancel : TeamActions.Close;
            if (action == TeamActions.Close)
            {
                await context.ActAsAsync("director").ConfigureAwait(false);
            }

            (await context.SendAsync(new ChangeTeamStatus(record.ResourceId, action, team.Value.Version, Reason), cancellationToken).ConfigureAwait(false))
                .Require(action + " team");
        }
    }
}
