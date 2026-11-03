using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Teams.Domain;
using Strategy.Modules.Teams.Infrastructure;
using Strategy.Platform.Files;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Reporting.Pdf;
using Strategy.Platform.Scheduling;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Teams.Application;

/// <summary>Files of a team (Files service): documents uploaded by people, and the memos / certificates the system generates (ADR 0044).</summary>
public static class TeamFilePurposes
{
    public static readonly FilePurpose Documents = new(
        "teams.document", TeamPermissions.ResourceType, TeamPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    public static readonly FilePurpose Generated = new(
        "teams.generated", TeamPermissions.ResourceType, TeamPermissions.SystemCode, [FileKinds.Pdf], 20 * 1024 * 1024);

    /// <summary>Evaluation evidence: owned by the evaluation, readable only with the results permission (ADR 0042).</summary>
    public static readonly FilePurpose Evidence = new(
        "teams.evidence", TeamEvaluation.EntityType, TeamPermissions.SystemCode, FileKinds.Documents, 20 * 1024 * 1024);

    /// <summary>Certificates: owned by the evaluation, readable only with the results permission.</summary>
    public static readonly FilePurpose Certificate = new(
        "teams.certificate", TeamEvaluation.EntityType, TeamPermissions.SystemCode, [FileKinds.Pdf], 20 * 1024 * 1024);
}

/// <summary>The bilingual HTML of the formation memo and certificates: Arabic right, English left, organisation names and colour from branding.</summary>
public static class TeamDocuments
{
    public sealed record MemoMember(string? NameAr, string? NameEn, string? RoleAr, string? RoleEn, string? Responsibilities);

    public sealed record MemoData(
        string? OrganisationAr, string? OrganisationEn, string Color, string? MemoNumber, DateOnly? MemoDate, string? TeamNumber, string? NameAr, string? NameEn,
        string? TypeAr, string? TypeEn, string? Subject, string? Objectives, string? Mandate, DateOnly? StartDate, DateOnly? EndDate, IReadOnlyList<MemoMember> Members,
        IReadOnlyList<(string? StepAr, string? StepEn, DateTimeOffset At)> Approvals);

    public static string MemoHtml(MemoData d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var html = Start(d.Color, d.NameAr ?? d.NameEn);
        Band(html, d.OrganisationAr, d.OrganisationEn, "قرار تشكيل", "Formation decision");
        html.Append("<table class=\"meta\" dir=\"ltr\"><tr><td class=\"en\">Decision no. <b>").Append(E(d.MemoNumber)).Append("</b> · Date <b>").Append(E(D(d.MemoDate)))
            .Append("</b></td><td class=\"ar\">رقم القرار <b>").Append(E(d.MemoNumber)).Append("</b> · التاريخ <b>").Append(E(D(d.MemoDate))).Append("</b></td></tr></table>");
        Pair(html, $"تشكيل {d.TypeAr ?? d.TypeEn}: {d.NameAr ?? d.NameEn} ({d.TeamNumber})", $"Formation of the {d.TypeEn ?? d.TypeAr}: {d.NameEn ?? d.NameAr} ({d.TeamNumber})", true);
        if (!string.IsNullOrWhiteSpace(d.Subject))
        {
            Pair(html, "الموضوع: " + d.Subject, "Subject: " + d.Subject);
        }

        if (!string.IsNullOrWhiteSpace(d.Objectives))
        {
            Pair(html, "الأهداف: " + d.Objectives, "Objectives: " + d.Objectives);
        }

        if (!string.IsNullOrWhiteSpace(d.Mandate))
        {
            Pair(html, "المهام والصلاحيات: " + d.Mandate, "Mandate: " + d.Mandate);
        }

        Pair(html, $"المدة: من {D(d.StartDate)} {(d.EndDate is null ? "(دائم)" : "إلى " + D(d.EndDate))}",
            $"Term: from {D(d.StartDate)} {(d.EndDate is null ? "(permanent)" : "to " + D(d.EndDate))}");
        html.Append("<table class=\"grid\"><thead><tr><th>#</th><th>الاسم / Name</th><th>الدور / Role</th><th>المسؤوليات / Responsibilities</th></tr></thead><tbody>");
        for (var i = 0; i < d.Members.Count; i++)
        {
            var m = d.Members[i];
            html.Append("<tr><td>").Append(i + 1).Append("</td><td><div class=\"ar\">").Append(E(m.NameAr ?? m.NameEn)).Append("</div><div class=\"en\">").Append(E(m.NameEn ?? m.NameAr))
                .Append("</div></td><td><div class=\"ar\">").Append(E(m.RoleAr)).Append("</div><div class=\"en\">").Append(E(m.RoleEn)).Append("</div></td><td dir=\"auto\">")
                .Append(E(m.Responsibilities)).Append("</td></tr>");
        }

        html.Append("</tbody></table>");
        if (d.Approvals.Count > 0)
        {
            html.Append("<h3 dir=\"ltr\"><span class=\"en\">Approved by</span> · <span class=\"ar\">اعتمد من</span></h3><ul class=\"approvals\" dir=\"ltr\">");
            foreach (var (stepAr, stepEn, at) in d.Approvals)
            {
                html.Append("<li><span class=\"en\">").Append(E(stepEn ?? stepAr)).Append("</span> · <span class=\"ar\">").Append(E(stepAr ?? stepEn)).Append("</span> — ")
                    .Append(E(at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append("</li>");
            }

            html.Append("</ul>");
        }

        return html.Append("</body></html>").ToString();
    }

    public sealed record CertificateData(
        string? OrganisationAr, string? OrganisationEn, string Color, string? Number, DateOnly Date, string? PersonAr, string? PersonEn, string? TeamAr, string? TeamEn,
        string? RoleAr, string? RoleEn, string? GradeAr, string? GradeEn, string Cycle);

    public static string CertificateHtml(CertificateData d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var html = Start(d.Color, d.PersonAr ?? d.PersonEn, landscape: true);
        Band(html, d.OrganisationAr, d.OrganisationEn, "شهادة شكر وتقدير", "Certificate of appreciation");
        html.Append("<div class=\"cert\" dir=\"ltr\"><div class=\"en\"><p>This certificate is awarded to</p><h2>").Append(E(d.PersonEn ?? d.PersonAr)).Append("</h2><p>")
            .Append(E(d.RoleEn)).Append(" of <b>").Append(E(d.TeamEn ?? d.TeamAr)).Append("</b>, in appreciation of the contribution in the evaluation cycle ").Append(E(d.Cycle))
            .Append(" (").Append(E(d.GradeEn)).Append(").</p></div><div class=\"ar\"><p>تُمنح هذه الشهادة إلى</p><h2>").Append(E(d.PersonAr ?? d.PersonEn)).Append("</h2><p>")
            .Append(E(d.RoleAr)).Append(" في <b>").Append(E(d.TeamAr ?? d.TeamEn)).Append("</b>، تقديراً للمساهمة في دورة التقييم ").Append(E(d.Cycle)).Append(" (")
            .Append(E(d.GradeAr)).Append(").</p></div></div>");
        html.Append("<table class=\"meta\" dir=\"ltr\"><tr><td class=\"en\">No. <b>").Append(E(d.Number)).Append("</b> · ").Append(E(D(d.Date))).Append("</td><td class=\"ar\">رقم <b>")
            .Append(E(d.Number)).Append("</b> · ").Append(E(D(d.Date))).Append("</td></tr></table>");
        return html.Append("</body></html>").ToString();
    }

    private static StringBuilder Start(string color, string? title, bool landscape = false)
    {
        var c = System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") ? color : "#1f3a68";
        return new StringBuilder().Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append("</title><style>")
            .Append(PdfFonts.FontFaceCss()).Append("@page{size:A4").Append(landscape ? " landscape" : string.Empty).Append(";margin:16mm 14mm}")
            .Append("body{font-family:'").Append(PdfFonts.Family).Append("',sans-serif;font-size:10.5pt;color:#1b1f24;margin:0}")
            .Append(".band{display:flex;justify-content:space-between;border-bottom:3px solid ").Append(c).Append(";padding-bottom:8px;margin-bottom:12px}")
            .Append(".ar{direction:rtl;text-align:right}.en{direction:ltr;text-align:left}h1{font-size:16pt;margin:0;color:").Append(c).Append("}.org{font-size:9pt;color:#57606a}")
            .Append(".pair{display:flex;gap:18px;margin-bottom:6px}.pair>div{flex:1}p{margin:0;line-height:1.75}.lead{font-weight:700;font-size:12pt}")
            .Append("table{width:100%;border-collapse:collapse}.meta td{width:50%;font-size:9.5pt;padding:4px 0}.grid{font-size:9pt;margin-top:10px}")
            .Append(".grid th{background:").Append(c).Append(";color:#fff}.grid th,.grid td{border:1px solid #d0d7de;padding:4px 6px;vertical-align:top;text-align:start}")
            .Append("h3{font-size:11pt;margin:14px 0 4px}.approvals{font-size:9pt;margin:0;padding-inline-start:18px}")
            .Append(".cert{display:flex;gap:28px;margin:40px 0}.cert>div{flex:1}.cert h2{font-size:22pt;color:").Append(c).Append(";margin:10px 0}")
            .Append("</style></head><body>");
    }

    private static void Band(StringBuilder html, string? orgAr, string? orgEn, string titleAr, string titleEn) =>
        html.Append("<div class=\"band\" dir=\"ltr\"><div class=\"en\"><h1>").Append(E(titleEn)).Append("</h1><div class=\"org\">").Append(E(orgEn)).Append("</div></div><div class=\"ar\"><h1>")
            .Append(E(titleAr)).Append("</h1><div class=\"org\">").Append(E(orgAr)).Append("</div></div></div>");

    private static void Pair(StringBuilder html, string? ar, string? en, bool lead = false) =>
        html.Append("<div class=\"pair\" dir=\"ltr\"><div class=\"en\"><p").Append(lead ? " class=\"lead\"" : string.Empty).Append('>').Append(E(en)).Append("</p></div><div class=\"ar\"><p")
            .Append(lead ? " class=\"lead\"" : string.Empty).Append('>').Append(E(ar)).Append("</p></div></div>");

    private static string D(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

/// <summary>Renders the formation memo after approval (outbox, retried on failure — ADR 0044).</summary>
internal sealed class RenderMemoHandler(
    TeamsDbContext db, IPdfRenderer pdf, IFileService files, ISettingsStore settings, IEmployeeDirectory employees, IApprovalEngine engine) : IOutboxHandler
{
    public string Type => TeamActivation.RenderMemo;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<RenderMemoMessage>(payload, OutboxJson.Options)!;
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == message.TeamId, cancellationToken).ConfigureAwait(false);
        if (team is null || team.MemoMode != MemoModes.Generate)
        {
            return;
        }

        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var type = await db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.Code == team.TypeCode, cancellationToken).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var members = await db.Members.AsNoTracking().Where(m => m.TeamId == team.Id && m.Status == MemberStatuses.Active).OrderBy(m => m.Position)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var people = await employees.GetManyAsync(members.Where(m => m.EmployeeId is not null).Select(m => m.EmployeeId!.Value).ToList(), cancellationToken).ConfigureAwait(false);
        var attempts = await engine.ListAttemptsAsync(TeamPermissions.ResourceType, team.Id, cancellationToken).ConfigureAwait(false);
        var approvals = attempts.Where(a => a.Instance.ProcessKey == FormationApprovalProcess.Key && a.Instance.Status == ApprovalInstanceStatuses.Approved)
            .Take(1).SelectMany(a => a.Decisions).Where(d => d.Outcome == ApprovalActions.Approve).Select(d => (d.StepNameAr, d.StepNameEn, d.At)).ToList();
        var html = TeamDocuments.MemoHtml(new TeamDocuments.MemoData(
            branding.OrganisationNameAr, branding.OrganisationNameEn, branding.PrimaryColor, team.MemoNumber, team.MemoDate, team.Number, team.NameAr, team.NameEn,
            type?.NameAr, type?.NameEn, team.Subject, team.Objectives, team.Mandate, team.StartDate, team.EndDate,
            members.Select(m =>
            {
                var person = m.EmployeeId is { } e ? people.GetValueOrDefault(e) : null;
                var role = roles.FirstOrDefault(r => r.Code == m.RoleCode);
                return new TeamDocuments.MemoMember(person?.NameAr ?? m.ExternalName, person?.NameEn ?? m.ExternalName, role?.NameAr, role?.NameEn, m.Responsibilities);
            }).ToList(),
            approvals));
        var bytes = await pdf.RenderAsync(html, cancellationToken).ConfigureAwait(false);
        await using var stream = new MemoryStream(bytes);
        var uploaded = await files.UploadAsync(new FileUpload(TeamFilePurposes.Generated.Key, team.Id.ToString("D"), $"{team.MemoNumber ?? team.Number}.pdf", stream,
            "formation-memo", team.MemoFileId), cancellationToken).ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            throw new InvalidOperationException(uploaded.Error.Code);
        }

        team.SetMemo(null, null, uploaded.Value.Id);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Renders the certificates of an evaluation after reward approval (outbox, idempotent per member).</summary>
internal sealed class RenderCertificatesHandler(
    TeamsDbContext db, IPdfRenderer pdf, IFileService files, ISettingsStore settings, IEmployeeDirectory employees) : IOutboxHandler
{
    public string Type => EvaluationApprovalHook.RenderCertificates;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<RenderCertificatesMessage>(payload, OutboxJson.Options)!;
        var evaluation = await db.Evaluations.AsNoTracking().FirstOrDefaultAsync(e => e.Id == message.EvaluationId, cancellationToken).ConfigureAwait(false);
        if (evaluation is null)
        {
            return;
        }

        var team = await db.Teams.AsNoTracking().FirstAsync(t => t.Id == evaluation.TeamId, cancellationToken).ConfigureAwait(false);
        var template = (await db.Templates.AsNoTracking().FirstAsync(t => t.Id == evaluation.TemplateId, cancellationToken).ConfigureAwait(false)).Read();
        var branding = await settings.GetAsync<BrandingSettings>(cancellationToken).ConfigureAwait(false);
        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.MemberEvaluations.Where(m => m.EvaluationId == evaluation.Id && m.Certificate && m.CertificateNumber != null && m.CertificateFileId == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            var person = row.EmployeeId is { } e ? await employees.GetAsync(e, cancellationToken).ConfigureAwait(false) : null;
            var role = roles.FirstOrDefault(r => r.Code == row.RoleCode);
            var grade = template.GradeBands.FirstOrDefault(b => b.Code == row.Grade);
            var html = TeamDocuments.CertificateHtml(new TeamDocuments.CertificateData(
                branding.OrganisationNameAr, branding.OrganisationNameEn, branding.PrimaryColor, row.CertificateNumber,
                DateOnly.FromDateTime((evaluation.ApprovedAt ?? DateTimeOffset.UtcNow).UtcDateTime), person?.NameAr ?? row.ExternalName, person?.NameEn ?? row.ExternalName,
                team.NameAr, team.NameEn, role?.NameAr, role?.NameEn, grade?.NameAr, grade?.NameEn, evaluation.Cycle));
            var bytes = await pdf.RenderAsync(html, cancellationToken).ConfigureAwait(false);
            await using var stream = new MemoryStream(bytes);
            var uploaded = await files.UploadAsync(new FileUpload(TeamFilePurposes.Certificate.Key, evaluation.Id.ToString("D"), $"{row.CertificateNumber}.pdf", stream, "certificate"),
                cancellationToken).ConfigureAwait(false);
            if (uploaded.IsFailure)
            {
                throw new InvalidOperationException(uploaded.Error.Code);
            }

            row.SetCertificate(row.CertificateNumber!, uploaded.Value.Id);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
