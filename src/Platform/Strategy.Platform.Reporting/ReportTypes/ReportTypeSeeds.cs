namespace Strategy.Platform.Reporting.ReportTypes;

/// <summary>A seeded report type (design note reporting-2.md §3); skipped when one of its datasets or relationships is not registered.</summary>
public sealed record ReportTypeSeed(string Code, string SystemCode, string LabelAr, string LabelEn, string DescriptionAr, string DescriptionEn, ReportTypeDefinition Definition);

/// <summary>Seeded report types per system (SPEC-ADDENDUM E1). Codes are stable; admins edit copies in Settings → Reporting → Report types.</summary>
public static class ReportTypeSeeds
{
    private static ReportTypeNode Root(string dataset) => new() { Alias = "a", Dataset = dataset };

    private static ReportTypeNode Rel(string alias, string parent, string dataset, string relationship, string join = JoinKinds.Outer) =>
        new() { Alias = alias, Parent = parent, Dataset = dataset, Relationship = relationship, Join = join };

    private static ReportTypeSeed Seed(string code, string system, string ar, string en, string descAr, string descEn, params ReportTypeNode[] nodes) =>
        new(code, system, ar, en, descAr, descEn, new ReportTypeDefinition { Nodes = [.. nodes] });

    public static IReadOnlyList<ReportTypeSeed> All { get; } =
    [
        Seed("portfolio.projects-milestones", "portfolio", "المشاريع مع المعالم والمهام", "Projects with milestones and tasks",
            "مشروع لكل معلم ولكل مهمة مرتبطة بالمعلم", "One row per milestone and its tasks",
            Root("portfolio.projects"), Rel("b", "a", "portfolio.milestones", "portfolio.project-milestones"),
            Rel("c", "b", "tasks.tasks", "portfolio.milestone-tasks")),
        Seed("portfolio.projects-risks-kpis", "portfolio", "المشاريع مع المخاطر والمؤشرات", "Projects with risks and KPIs",
            "المخاطر والمؤشرات تُدرج متتالية دون تكرار متقاطع", "Risks and KPIs listed one after the other (no cross product)",
            Root("portfolio.projects"), Rel("b", "a", "portfolio.project-risks", "portfolio.project-project-risks"),
            Rel("c", "a", "portfolio.project-kpis", "portfolio.project-kpis"), Rel("d", "c", "kpi.results", "portfolio.project-kpi-results")),
        Seed("portfolio.projects-change-requests", "portfolio", "المشاريع مع طلبات التغيير", "Projects with change requests",
            "المشاريع التي لديها طلبات تغيير", "Projects that have change requests",
            Root("portfolio.projects"), Rel("b", "a", "portfolio.change-requests", "portfolio.project-change-requests", JoinKinds.Inner)),
        Seed("portfolio.projects-team", "portfolio", "المشاريع مع أعضاء الفريق", "Projects with team members",
            "أعضاء فريق كل مشروع من نظام الفرق", "Each project's team members from the Teams system",
            Root("portfolio.projects"), Rel("b", "a", "teams.members", "portfolio.project-team-members")),
        Seed("portfolio.requests-approvals", "portfolio", "طلبات البرامج مع سجل الاعتماد", "Program requests with approvals",
            "قرارات خطوات الاعتماد لكل طلب", "The decided approval steps of each request",
            Root("portfolio.requests"), Rel("b", "a", "workflow.approvals", "portfolio.request-approvals")),
        Seed("requests.requests-approvals", "requests", "الطلبات مع سجل الاعتماد", "Requests with approvals",
            "قرارات خطوات الاعتماد لكل طلب", "The decided approval steps of each request",
            Root("requests.requests"), Rel("b", "a", "workflow.approvals", "requests.request-approvals")),
        Seed("kpi.cards-results", "kpi", "بطاقات المؤشرات مع النتائج حسب الفترة", "KPI cards with results by period",
            "نتيجة لكل فترة قياس", "One row per measuring period",
            Root("kpi.cards"), Rel("b", "a", "kpi.results", "kpi.card-results")),
        Seed("risk.risks-controls-actions", "risk", "المخاطر مع الضوابط والإجراءات", "Risks with controls and actions",
            "الضوابط وإجراءات المعالجة لكل خطر", "Each risk's controls and treatment actions",
            Root("risk.risks"), Rel("b", "a", "risk.controls", "risk.risk-controls"), Rel("c", "a", "risk.actions", "risk.risk-actions")),
        Seed("risk.risks-kri-results", "risk", "المخاطر مع نتائج مؤشرات المخاطر", "Risks with KRI results",
            "نتائج مؤشرات الأداء المرتبطة بالخطر كمؤشرات مخاطر", "Results of the KPIs linked to each risk as KRIs",
            Root("risk.risks"), Rel("b", "a", "risk.kris", "risk.risk-kris", JoinKinds.Inner), Rel("c", "b", "kpi.results", "risk.kri-results")),
        Seed("auditmgmt.findings-recommendations", "auditmgmt", "الملاحظات مع التوصيات والمتابعات", "Findings with recommendations and follow-ups",
            "توصيات كل ملاحظة ومتابعاتها", "Each finding's recommendations and their follow-ups",
            Root("auditmgmt.findings"), Rel("b", "a", "auditmgmt.recommendations", "auditmgmt.finding-recommendations"),
            Rel("c", "b", "auditmgmt.follow-ups", "auditmgmt.recommendation-follow-ups")),
        Seed("auditmgmt.findings-risks", "auditmgmt", "الملاحظات مع المخاطر", "Findings with risks",
            "المخاطر المرتبطة بكل ملاحظة من سجل المخاطر", "Risks linked to each finding, from the risk register",
            Root("auditmgmt.findings"), Rel("b", "a", "auditmgmt.finding-risks", "auditmgmt.finding-risk-links", JoinKinds.Inner),
            Rel("c", "b", "risk.risks", "auditmgmt.finding-risk-risks")),
        Seed("cap.remarks-actions", "cap", "الملاحظات مع الإجراءات التصحيحية", "Remarks with actions",
            "الإجراءات التصحيحية لكل ملاحظة", "Each remark's corrective actions",
            Root("cap.remarks"), Rel("b", "a", "cap.actions", "cap.remark-actions")),
        Seed("bench.studies-topics-actions", "bench", "الدراسات مع المحاور والإجراءات", "Studies with topics and actions",
            "محاور الدراسة وإجراءات خطة التحسين", "Study topics and improvement plan actions",
            Root("bench.studies"), Rel("b", "a", "bench.topics", "bench.study-topics"), Rel("c", "a", "bench.plan-items", "bench.study-plan-items")),
        Seed("bench.studies-cap", "bench", "الدراسات مع ملاحظات وإجراءات خطة العمل", "Studies with CAP remarks and actions",
            "ملاحظات خطة العمل التصحيحية الناتجة عن الدراسة وإجراءاتها", "CAP remarks raised from each study and their actions",
            Root("bench.studies"), Rel("b", "a", "cap.remarks", "bench.study-cap-remarks", JoinKinds.Inner), Rel("c", "b", "cap.actions", "cap.remark-actions")),
        Seed("teams.teams-members-evaluations", "teams", "الفرق مع الأعضاء والتقييمات", "Teams with members and evaluations",
            "أعضاء كل فريق وتقييماته", "Each team's members and evaluations",
            Root("teams.teams"), Rel("b", "a", "teams.members", "teams.team-members"), Rel("c", "a", "teams.evaluations", "teams.team-evaluations")),
        Seed("docreg.documents-versions", "docreg", "الوثائق مع الإصدارات", "Documents with versions",
            "إصدارات كل وثيقة", "Each document's versions",
            Root("docreg.documents"), Rel("b", "a", "docreg.versions", "docreg.document-versions")),
        Seed("docreg.documents-ip", "docreg", "الوثائق مع سجلات الملكية الفكرية", "Documents with IP records",
            "سجلات الملكية الفكرية الناتجة عن الوثيقة", "IP records created from each document",
            Root("docreg.documents"), Rel("b", "a", "ip.records", "ip.document-records", JoinKinds.Inner)),
    ];
}
