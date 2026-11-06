using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Reporting.Excel;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Settings;

namespace Strategy.Modules.Teams.Application;

public sealed record CountDto(string Code, int Count);

public sealed record AmountDto(string Code, decimal Amount);

public sealed record TeamWorkloadDto(TeamPersonDto Employee, int ActiveMemberships, bool OverAssigned);

public sealed record TeamDashboardDto(
    int Teams,
    int ActiveCommittees,
    IReadOnlyList<CountDto> ByType,
    IReadOnlyList<CountDto> ByStatus,
    IReadOnlyList<TeamWorkloadDto> Workload,
    IReadOnlyList<CountDto> EvaluationProgress,
    IReadOnlyList<CountDto>? ScoreDistribution,
    IReadOnlyList<CountDto>? GradeDistribution,
    IReadOnlyList<AmountDto>? RewardsByYear,
    IReadOnlyList<AmountDto>? RewardsByUnit);

/// <remarks>
/// <c>From</c> / <c>To</c> (inclusive dates, normally the first day of the first month and the last day of the last month of the period): only teams
/// whose formed-on (start) to end window overlaps the period (no end = still running), and only evaluation cycles whose period ends in the
/// range. Docs/design/reporting.md §2.3.
/// </remarks>
public sealed record GetTeamDashboard(DateOnly? From = null, DateOnly? To = null) : IQuery<TeamDashboardDto>;

public sealed record ExportTeams(string Kind, string Format, string? Language) : IQuery<ExportedFile>;

public sealed record ExportedFile(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Teams dashboard (SPEC §4.6) over the teams the caller may read; score, grade and reward figures only over teams where the caller holds
/// the results permission (ADR 0042).
/// </summary>
internal sealed class DashboardHandlers(
    TeamsDbContext db, TeamReader reader, IRequestContext context, ISettingsStore settings, IPdfRenderer pdf, IAuditLog audit)
    : IQueryHandler<GetTeamDashboard, TeamDashboardDto>, IQueryHandler<ExportTeams, ExportedFile>
{
    public static readonly IReadOnlyList<string> Kinds = ["register", "membership", "evaluations", "rewards"];

    public async Task<Result<TeamDashboardDto>> HandleAsync(GetTeamDashboard query, CancellationToken cancellationToken)
    {
        if (query.From is { } rangeFrom && query.To is { } rangeTo && rangeFrom > rangeTo)
        {
            return Error.Validation([new FieldError("to", "validation.invalid", "validation.invalid")]);
        }

        var user = context.User;
        var visible = (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => t.Status != TeamStatuses.Cancelled);
        if (query.From is { } from)
        {
            visible = visible.Where(t => t.EndDate == null || t.EndDate >= from);
        }

        if (query.To is { } to)
        {
            visible = visible.Where(t => t.StartDate == null || t.StartDate <= to);
        }

        var teams = await visible.ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = teams.Select(t => t.Id).ToList();
        var configuration = await settings.GetAsync<TeamSettings>(cancellationToken).ConfigureAwait(false);
        var operating = teams.Where(t => TeamStatuses.IsOperating(t.Status)).Select(t => t.Id).ToList();
        var memberships = await db.Members.AsNoTracking().Where(m => operating.Contains(m.TeamId) && m.Status == MemberStatuses.Active && m.EmployeeId != null)
            .GroupBy(m => m.EmployeeId).Select(g => new { Employee = g.Key!.Value, Count = g.Count() }).OrderByDescending(x => x.Count).Take(15)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await reader.PeopleAsync(memberships.Select(m => (Guid?)m.Employee), cancellationToken).ConfigureAwait(false);
        var evaluationQuery = db.Evaluations.AsNoTracking().Where(e => ids.Contains(e.TeamId) && e.Status != EvaluationStatuses.Cancelled);
        if (query.From is { } cycleFrom)
        {
            evaluationQuery = evaluationQuery.Where(e => e.PeriodEnd >= cycleFrom);
        }

        if (query.To is { } cycleTo)
        {
            evaluationQuery = evaluationQuery.Where(e => e.PeriodEnd <= cycleTo);
        }

        var evaluations = await evaluationQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var withResults = await reader.WithResultsAsync(user, teams, cancellationToken).ConfigureAwait(false);
        var resultEvaluations = evaluations.Where(e => withResults.Contains(e.TeamId) && e.Status == EvaluationStatuses.Approved).ToList();
        var resultIds = resultEvaluations.Select(e => e.Id).ToList();
        var grades = withResults.Count == 0
            ? null
            : await db.MemberEvaluations.AsNoTracking().Where(m => resultIds.Contains(m.EvaluationId) && m.Grade != null).GroupBy(m => m.Grade!)
                .Select(g => new CountDto(g.Key, g.Count())).ToListAsync(cancellationToken).ConfigureAwait(false);
        var units = teams.ToDictionary(t => t.Id, t => t.OwningUnitId);
        return new TeamDashboardDto(
            teams.Count,
            teams.Count(t => TeamStatuses.IsOperating(t.Status) && t.TypeCode.Contains("committee", StringComparison.Ordinal)),
            teams.GroupBy(t => t.TypeCode).Select(g => new CountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ToList(),
            teams.GroupBy(t => t.Status).Select(g => new CountDto(g.Key, g.Count())).OrderByDescending(c => c.Count).ToList(),
            memberships.Select(m => new TeamWorkloadDto(TeamReader.Person(m.Employee, people)!, m.Count, m.Count >= configuration.OverAssignedThreshold)).ToList(),
            evaluations.GroupBy(e => e.Status).Select(g => new CountDto(g.Key, g.Count())).ToList(),
            withResults.Count == 0 ? null : resultEvaluations.GroupBy(e => Band(e.Score)).Select(g => new CountDto(g.Key, g.Count())).OrderBy(c => c.Code).ToList(),
            grades,
            withResults.Count == 0
                ? null
                : resultEvaluations.Where(e => e.RewardStatus == RewardStatuses.Approved).GroupBy(e => EvaluationReader.RewardYear(e, DateOnly.FromDateTime(DateTime.UtcNow)))
                    .Select(g => new AmountDto(g.Key.ToString(CultureInfo.InvariantCulture), g.Sum(e => e.RewardTotal ?? 0))).OrderBy(a => a.Code).ToList(),
            withResults.Count == 0
                ? null
                : resultEvaluations.Where(e => e.RewardStatus == RewardStatuses.Approved).GroupBy(e => units.GetValueOrDefault(e.TeamId)?.ToString("D") ?? "-")
                    .Select(g => new AmountDto(g.Key, g.Sum(e => e.RewardTotal ?? 0))).ToList());
    }

    public async Task<Result<ExportedFile>> HandleAsync(ExportTeams query, CancellationToken cancellationToken)
    {
        if (!Kinds.Contains(query.Kind) || query.Format is not ("xlsx" or "pdf"))
        {
            return Error.Validation([new FieldError("kind", "validation.unknownCode", "validation.unknownCode")]);
        }

        var user = context.User;
        var arabic = query.Language != "en";
        var teams = await (await reader.VisibleAsync(user, cancellationToken).ConfigureAwait(false)).Where(t => t.Status != TeamStatuses.Cancelled).OrderBy(t => t.Number)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var types = await db.Types.AsNoTracking().ToDictionaryAsync(t => t.Code, cancellationToken).ConfigureAwait(false);
        var roles = await reader.RolesAsync(cancellationToken).ConfigureAwait(false);
        var withResults = query.Kind is "evaluations" or "rewards" ? await reader.WithResultsAsync(user, teams, cancellationToken).ConfigureAwait(false) : [];
        string N(string? ar, string? en) => (arabic ? ar ?? en : en ?? ar) ?? string.Empty;
        (string Ar, string En)[] headers;
        var rows = new List<string?[]>();
        switch (query.Kind)
        {
            case "register":
                headers = [("الرمز", "Code"), ("الاسم", "Name"), ("النوع", "Type"), ("الحالة", "Status"), ("البدء", "Start"), ("الانتهاء", "End")];
                rows.AddRange(teams.Select(t => new[]
                {
                    t.Number, N(t.NameAr, t.NameEn), N(types.GetValueOrDefault(t.TypeCode)?.NameAr, types.GetValueOrDefault(t.TypeCode)?.NameEn), t.Status, D(t.StartDate), D(t.EndDate),
                }));
                break;
            case "membership":
                {
                    headers = [("الفريق", "Team"), ("العضو", "Member"), ("الدور", "Role"), ("من", "From"), ("إلى", "To"), ("الحالة", "Status")];
                    var ids = teams.Select(t => t.Id).ToList();
                    var members = await db.Members.AsNoTracking().Where(m => ids.Contains(m.TeamId)).ToListAsync(cancellationToken).ConfigureAwait(false);
                    var people = await reader.PeopleAsync(members.Select(m => m.EmployeeId), cancellationToken).ConfigureAwait(false);
                    foreach (var m in members.OrderBy(m => m.TeamId).ThenBy(m => m.Position))
                    {
                        var t = teams.First(x => x.Id == m.TeamId);
                        var role = roles.FirstOrDefault(r => r.Code == m.RoleCode);
                        var person = m.EmployeeId is { } e ? people.GetValueOrDefault(e) : null;
                        rows.Add([t.Number, N(person?.NameAr ?? m.ExternalName, person?.NameEn ?? m.ExternalName), N(role?.NameAr, role?.NameEn), D(m.FromDate), D(m.ToDate), m.Status]);
                    }

                    break;
                }

            case "evaluations":
                {
                    headers = [("الفريق", "Team"), ("الدورة", "Cycle"), ("الحالة", "Status"), ("الدرجة", "Score"), ("عبء العمل", "Workload")];
                    var list = await db.Evaluations.AsNoTracking().Where(e => withResults.Contains(e.TeamId)).OrderBy(e => e.OpenedAt).ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    rows.AddRange(list.Select(e => new[]
                    {
                        teams.First(t => t.Id == e.TeamId).Number, e.Cycle, e.Status, e.Score?.ToString(CultureInfo.InvariantCulture), e.Workload,
                    }));
                    break;
                }

            default:
                {
                    headers = [("الفريق", "Team"), ("الدورة", "Cycle"), ("العضو", "Member"), ("التقدير", "Grade"), ("المكافأة", "Reward"), ("شهادة", "Certificate")];
                    var list = await (from m in db.MemberEvaluations.AsNoTracking()
                                      join e in db.Evaluations.AsNoTracking() on m.EvaluationId equals e.Id
                                      where withResults.Contains(e.TeamId) && e.RewardStatus == RewardStatuses.Approved
                                      select new { e.TeamId, e.Cycle, m.EmployeeId, m.ExternalName, m.Grade, m.ApprovedReward, m.CertificateNumber })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    var people = await reader.PeopleAsync(list.Select(l => l.EmployeeId), cancellationToken).ConfigureAwait(false);
                    rows.AddRange(list.Select(l =>
                    {
                        var person = l.EmployeeId is { } e ? people.GetValueOrDefault(e) : null;
                        return new[]
                        {
                            teams.First(t => t.Id == l.TeamId).Number, l.Cycle, N(person?.NameAr ?? l.ExternalName, person?.NameEn ?? l.ExternalName), l.Grade,
                            l.ApprovedReward?.ToString("0.00", CultureInfo.InvariantCulture), l.CertificateNumber,
                        };
                    }));
                    break;
                }
        }

        var (titleAr, titleEn) = query.Kind switch
        {
            "register" => ("سجل الفرق واللجان", "Team register"),
            "membership" => ("تقرير العضويات", "Membership report"),
            "evaluations" => ("نتائج التقييم", "Evaluation results"),
            _ => ("المكافآت", "Rewards"),
        };
        ExportedFile file;
        if (query.Format == "pdf")
        {
            var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
            var document = new ReportDocument
            {
                TitleAr = titleAr,
                TitleEn = titleEn,
                OrganisationAr = branding.OrganisationNameAr,
                OrganisationEn = branding.OrganisationNameEn,
                PrimaryColor = branding.PrimaryColor,
                Language = arabic ? "ar" : "en",
            }.Table(headers.Select(h => new ReportColumn(h.Ar, h.En)).ToList(), rows);
            file = new ExportedFile(await pdf.RenderAsync(document.ToHtml(), cancellationToken).ConfigureAwait(false), "application/pdf", $"teams-{query.Kind}.pdf");
        }
        else
        {
            file = new ExportedFile(ExcelExport.Build(arabic ? titleAr : titleEn, headers.Select(h => arabic ? h.Ar : h.En).ToList(),
                rows.Select(r => (IReadOnlyList<object?>)r.Cast<object?>().ToList()), arabic), ExcelExport.ContentType, $"teams-{query.Kind}.xlsx");
        }

        await audit.WriteAsync(new AuditEvent(AuditActions.Export, true, TeamPermissions.SystemCode, Team.EntityType, null,
            new { kind = query.Kind, format = query.Format, rows = rows.Count }), cancellationToken).ConfigureAwait(false);
        return file;
    }

    private static string Band(decimal? score) => score switch
    {
        >= 90 => "90-100",
        >= 70 => "70-89",
        >= 50 => "50-69",
        _ => "0-49",
    };

    private static string? D(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
