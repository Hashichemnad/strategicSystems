using Strategy.Platform.Reporting.ReportTypes;

namespace Strategy.Platform.Reporting.Dossiers;

public sealed record DossierSeed(string Code, string Dataset, string LabelAr, string LabelEn, DossierDefinition Definition);

/// <summary>Seeded dossier templates (SPEC-ADDENDUM E3). Admins edit copies in Settings → Reporting → Dossier templates.</summary>
public static class DossierSeeds
{
    private static DossierSection Fields(string key, string ar, string en, params string[] fields) =>
        new() { Key = key, TitleAr = ar, TitleEn = en, Kind = DossierSectionKinds.Fields, Fields = [.. fields] };

    private static DossierSection Related(string key, string ar, string en, string[] path, string[] fields, List<ReportFilter>? filters = null, List<ReportSort>? sort = null) =>
        new() { Key = key, TitleAr = ar, TitleEn = en, Kind = DossierSectionKinds.Related, Path = [.. path], Fields = [.. fields], Filters = filters, Sort = sort };

    private static DossierSection Chart(string key, string ar, string en, string[] path, string type, string category, string? measure = null, string aggregate = SummaryFunctions.Sum) =>
        new()
        {
            Key = key,
            TitleAr = ar,
            TitleEn = en,
            Kind = DossierSectionKinds.Chart,
            Path = [.. path],
            Chart = new ReportChart { Type = type, Category = category, Measure = measure, Aggregate = aggregate },
        };

    private static DossierSection Gantt(string key, string ar, string en, string[] path, string labelAr, string labelEn, string start, string end, string? actualStart = null, string? actualEnd = null) =>
        new()
        {
            Key = key,
            TitleAr = ar,
            TitleEn = en,
            Kind = DossierSectionKinds.Gantt,
            Path = [.. path],
            LabelAr = labelAr,
            LabelEn = labelEn,
            Start = start,
            End = end,
            ActualStart = actualStart,
            ActualEnd = actualEnd,
        };

    private static DossierSection Approvals(string key, string resourceType) =>
        new() { Key = key, TitleAr = "سجل الاعتماد", TitleEn = "Approval history", Kind = DossierSectionKinds.Approvals, ResourceType = resourceType };

    private static DossierSection Attachments(string key, string ownerType) =>
        new() { Key = key, TitleAr = "المرفقات", TitleEn = "Attachments", Kind = DossierSectionKinds.Attachments, OwnerType = ownerType };

    private static ReportFilter Eq(string field, string value) => new(field, ReportOperators.Equal, [value]);

    public static IReadOnlyList<DossierSeed> All { get; } =
    [
        new("portfolio.project-dossier", "portfolio.projects", "ملف المشروع", "Project dossier", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "ملف المشروع", TitleEn = "Project dossier", SubtitleField = "a.number" },
            NumberField = "a.number", NameFieldAr = "a.nameAr", NameFieldEn = "a.nameEn",
            HeaderAr = "ملف المشروع", HeaderEn = "Project dossier", FooterAr = "وثيقة داخلية", FooterEn = "Internal document",
            Sections =
            [
                Fields("overview", "نظرة عامة", "Overview", "a.number", "a.nameAr", "a.nameEn", "a.classification", "a.methodology", "a.unit", "a.owner", "a.pm",
                    "a.strategicObjective", "a.plannedStart", "a.plannedEnd"),
                Fields("charter", "ميثاق المشروع", "Charter", "a.description", "a.scopeOfWork", "a.resourcesHuman", "a.resourcesTechnical"),
                Fields("health", "المرحلة وحالة الأداء", "Stage and RAG health", "a.stage", "a.status", "a.progressStatus", "a.performance", "a.plannedProgress"),
                Fields("budget", "الميزانية مقابل الفعلي", "Budget vs actual", "a.budget", "a.actualCost", "a.costPerformance"),
                Related("milestones", "المعالم", "Milestones", ["portfolio.project-milestones"],
                    ["b.code", "b.titleAr", "b.titleEn", "b.status", "b.plannedStart", "b.plannedEnd", "b.actualEnd", "b.completion"], sort: [new("b.plannedStart", false)]),
                Gantt("gantt", "المخطط الزمني للمعالم", "Milestones Gantt", ["portfolio.project-milestones"], "b.titleAr", "b.titleEn", "b.plannedStart", "b.plannedEnd",
                    "b.actualStart", "b.actualEnd"),
                Related("kpis", "المؤشرات ونتائجها", "KPIs with results", ["portfolio.project-kpis", "portfolio.project-kpi-results"],
                    ["b.nameAr", "b.nameEn", "b.target", "b.currentResult", "c.period", "c.achieve", "c.actualPerformance"]),
                Related("risks", "المخاطر", "Risks", ["portfolio.project-project-risks"],
                    ["b.title", "b.probability", "b.impact", "b.inherentBand", "b.residualBand", "b.response", "b.owner"]),
                new()
                {
                    Key = "risk-matrix", TitleAr = "مصفوفة المخاطر", TitleEn = "Risk matrix", Kind = DossierSectionKinds.Matrix, Path = ["portfolio.project-project-risks"],
                    Likelihood = "b.probability", Impact = "b.impact", Size = 5,
                },
                Related("issues", "القضايا", "Issues", ["portfolio.project-raid"], ["b.code", "b.title", "b.priority", "b.status", "b.owner", "b.dueDate"],
                    [Eq("b.type", "issue")]),
                Related("change-requests", "طلبات التغيير", "Change requests", ["portfolio.project-change-requests"],
                    ["b.number", "b.type", "b.status", "b.submittedAt", "b.decidedAt"]),
                Related("team", "فريق المشروع", "Project team", ["portfolio.project-team-members"], ["b.employee", "b.externalName", "b.role", "b.fromDate", "b.toDate"]),
                Related("stakeholders", "أصحاب المصلحة", "Stakeholders", ["portfolio.project-stakeholders"],
                    ["b.name", "b.classification", "b.interest", "b.influence", "b.communication"]),
                Related("decisions", "قرارات الاجتماعات", "Meeting decisions", ["portfolio.project-raid"], ["b.code", "b.title", "b.decidedOn", "b.decidedBy", "b.resolution"],
                    [Eq("b.type", "decision")]),
                Related("lessons", "الدروس المستفادة", "Lessons learned", ["portfolio.project-lessons"], ["b.stage", "b.type", "b.text", "b.recommendation"]),
                Fields("closure", "الإغلاق والأثر", "Closure and impact", "a.completedAt", "a.summaryOfAchievement", "a.mainDeliverables", "a.communitySatisfaction",
                    "a.employeeSatisfaction", "a.impactNotes"),
                Approvals("approvals", "portfolio.project"),
                Attachments("attachments", "portfolio.project"),
            ],
        }),
        new("kpi.card-dossier", "kpi.cards", "ملف بطاقة المؤشر", "KPI card dossier", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "ملف بطاقة المؤشر", TitleEn = "KPI card dossier", SubtitleField = "a.code" },
            NumberField = "a.code", NameFieldAr = "a.nameAr", NameFieldEn = "a.nameEn",
            Sections =
            [
                Fields("overview", "نظرة عامة", "Overview", "a.code", "a.nameAr", "a.nameEn", "a.type", "a.level", "a.status", "a.department", "a.unit", "a.owner",
                    "a.measuringPeriod", "a.measuringMethod", "a.direction", "a.weight", "a.baselineValue"),
                new()
                {
                    Key = "trend", TitleAr = "اتجاه الأداء", TitleEn = "KPI trend", Kind = DossierSectionKinds.Trend, Path = ["kpi.card-results"],
                    Category = "b.period", Measures = ["b.target", "b.achieve"],
                },
                Related("results", "النتائج حسب الفترة", "Results by period", ["kpi.card-results"],
                    ["b.year", "b.period", "b.status", "b.target", "b.achieve", "b.actualPerformance", "b.cappedPerformance", "b.late"], sort: [new("b.periodStart", false)]),
                Approvals("approvals", "kpi.card"),
                Attachments("attachments", "kpi.card"),
            ],
        }),
        new("risk.risk-profile", "risk.risks", "ملف الخطر", "Risk profile", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "ملف الخطر", TitleEn = "Risk profile", SubtitleField = "a.number" },
            NumberField = "a.number", NameFieldAr = "a.titleAr", NameFieldEn = "a.titleEn",
            Sections =
            [
                Fields("overview", "نظرة عامة", "Overview", "a.number", "a.titleAr", "a.titleEn", "a.type", "a.status", "a.unit", "a.owner", "a.coordinator", "a.identifiedOn"),
                Fields("assessment", "التقييم", "Assessment", "a.inherentLikelihood", "a.inherentImpact", "a.inherentScore", "a.inherentBand", "a.residualLikelihood",
                    "a.residualImpact", "a.residualScore", "a.residualBand", "a.targetScore", "a.withinAppetite", "a.response"),
                new()
                {
                    Key = "matrix", TitleAr = "موقع الخطر في المصفوفة", TitleEn = "Risk matrix", Kind = DossierSectionKinds.Matrix, Path = [],
                    Likelihood = "a.residualLikelihood", Impact = "a.residualImpact", Size = 5,
                },
                Related("controls", "الضوابط", "Controls", ["risk.risk-controls"], ["b.nameAr", "b.nameEn", "b.type", "b.owner", "b.effectiveness", "b.lastAssessedOn"]),
                Related("actions", "إجراءات المعالجة", "Treatment actions", ["risk.risk-actions"],
                    ["b.number", "b.titleAr", "b.titleEn", "b.status", "b.owner", "b.plannedEnd", "b.completion"]),
                Gantt("actions-gantt", "المخطط الزمني للإجراءات", "Actions timeline", ["risk.risk-actions"], "b.titleAr", "b.titleEn", "b.plannedStart", "b.plannedEnd",
                    null, "b.actualEnd"),
                Related("kris", "مؤشرات المخاطر ونتائجها", "Key risk indicators and results", ["risk.risk-kris", "risk.kri-results"],
                    ["b.cardCode", "b.breachThreshold", "c.period", "c.achieve"]),
                Approvals("approvals", "risk.risk"),
            ],
        }),
        new("auditmgmt.report-pack", "auditmgmt.reports", "حزمة تقرير التدقيق", "Audit report pack", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "حزمة تقرير التدقيق", TitleEn = "Audit report pack", SubtitleField = "a.number" },
            NumberField = "a.number", NameFieldAr = "a.titleAr", NameFieldEn = "a.titleEn",
            Sections =
            [
                Fields("overview", "بيانات التقرير", "Report", "a.number", "a.titleAr", "a.titleEn", "a.type", "a.auditorBody", "a.leadAuditor", "a.periodFrom", "a.periodTo",
                    "a.reportDate", "a.status"),
                Fields("summary", "الملخص", "Summary", "a.summary"),
                Chart("by-priority", "الملاحظات حسب الأولوية", "Findings by priority", ["auditmgmt.report-findings"], ChartTypes.Bar, "b.priority"),
                Related("findings", "الملاحظات", "Findings", ["auditmgmt.report-findings"],
                    ["b.number", "b.titleAr", "b.titleEn", "b.priority", "b.status", "b.responsibleUnit", "b.expectedClosingOn"]),
                Related("recommendations", "التوصيات", "Recommendations", ["auditmgmt.report-findings", "auditmgmt.finding-recommendations"],
                    ["b.number", "c.number", "c.actionAr", "c.actionEn", "c.status", "c.dueDate", "c.latestPct"]),
                Related("follow-ups", "المتابعات", "Follow-ups",
                    ["auditmgmt.report-findings", "auditmgmt.finding-recommendations", "auditmgmt.recommendation-follow-ups"],
                    ["c.number", "d.number", "d.followUpOn", "d.pct", "d.reviewStatus"]),
            ],
        }),
        new("teams.evaluation-report", "teams.teams", "تقرير تقييم الفريق", "Team evaluation report", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "تقرير تقييم الفريق", TitleEn = "Team evaluation report", SubtitleField = "a.number" },
            NumberField = "a.number", NameFieldAr = "a.nameAr", NameFieldEn = "a.nameEn",
            Sections =
            [
                Fields("overview", "بيانات الفريق", "Team", "a.number", "a.nameAr", "a.nameEn", "a.type", "a.status", "a.unit", "a.leader", "a.sponsor", "a.startDate", "a.endDate"),
                Related("members", "الأعضاء", "Members", ["teams.team-members"], ["b.employee", "b.externalName", "b.role", "b.allocation", "b.status"]),
                Chart("scores", "درجات التقييم", "Evaluation scores", ["teams.team-evaluations"], ChartTypes.Line, "b.periodEnd", "b.score", SummaryFunctions.Average),
                Related("evaluations", "التقييمات", "Evaluations", ["teams.team-evaluations"],
                    ["b.number", "b.cycle", "b.periodStart", "b.periodEnd", "b.evaluator", "b.score", "b.status"], sort: [new("b.periodStart", false)]),
            ],
        }),
        new("bench.study-report", "bench.studies", "تقرير دراسة المقارنة المعيارية", "Benchmark study report", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "تقرير دراسة المقارنة المعيارية", TitleEn = "Benchmark study report", SubtitleField = "a.number" },
            NumberField = "a.number", NameFieldAr = "a.titleAr", NameFieldEn = "a.titleEn",
            Sections =
            [
                Fields("overview", "بيانات الدراسة", "Study", "a.number", "a.titleAr", "a.titleEn", "a.stage", "a.status", "a.targetYear", "a.targetQuarter", "a.unit",
                    "a.coordinator", "a.comparisonType", "a.purpose"),
                Related("topics", "المحاور", "Topics", ["bench.study-topics"], ["b.title", "b.owner"], sort: [new("b.sort", false)]),
                Related("plan", "خطة التحسين", "Improvement plan", ["bench.study-plan-items"], ["b.titleAr", "b.titleEn", "b.owner", "b.unit", "b.dueOn", "b.priority"]),
                Related("cap", "ملاحظات وإجراءات خطة العمل التصحيحية", "CAP remarks and actions", ["bench.study-cap-remarks", "cap.remark-actions"],
                    ["b.number", "b.textAr", "b.textEn", "b.status", "c.number", "c.status", "c.achieved"]),
            ],
        }),
        new("cap.source-report", "cap.sources", "تقرير مصدر خطة العمل التصحيحية", "CAP source report", new DossierDefinition
        {
            Cover = new DossierCover { TitleAr = "تقرير مصدر خطة العمل التصحيحية", TitleEn = "CAP source report", SubtitleField = "a.number" },
            NumberField = "a.number", NameFieldAr = "a.titleAr", NameFieldEn = "a.titleEn",
            Sections =
            [
                Fields("overview", "بيانات المصدر", "Source", "a.number", "a.titleAr", "a.titleEn", "a.sourceType", "a.receivedOn", "a.owner", "a.unit", "a.status"),
                Chart("by-status", "الملاحظات حسب الحالة", "Remarks by status", ["cap.source-remarks"], ChartTypes.Pie, "b.status"),
                Related("remarks", "الملاحظات", "Remarks", ["cap.source-remarks"], ["b.number", "b.textAr", "b.textEn", "b.priority", "b.status", "b.owner", "b.dueOn"]),
                Related("actions", "الإجراءات التصحيحية", "Corrective actions", ["cap.source-remarks", "cap.remark-actions"],
                    ["b.number", "c.number", "c.detailsAr", "c.detailsEn", "c.status", "c.assignee", "c.endDate", "c.achieved"]),
                Gantt("actions-gantt", "المخطط الزمني للإجراءات", "Actions timeline", ["cap.source-remarks", "cap.remark-actions"], "c.detailsAr", "c.detailsEn",
                    "c.plannedStart", "c.plannedEnd", null, "c.actualEnd"),
            ],
        }),
    ];
}
