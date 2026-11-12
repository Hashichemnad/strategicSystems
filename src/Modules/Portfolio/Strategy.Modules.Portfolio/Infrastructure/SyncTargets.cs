using Microsoft.Extensions.DependencyInjection;
using Strategy.Platform.Access;
using Strategy.Platform.Integration;
using Strategy.Modules.Portfolio.Application;
using Strategy.Modules.Portfolio.Domain;

namespace Strategy.Modules.Portfolio.Infrastructure;

/// <summary>
/// Portfolio record types the standby sync may write (SPEC-ADDENDUM §G3, ADR 0202): Program Request, Project / Initiative and its work plan and
/// registers. Only the listed properties are written; synced records arrive in their Salesforce state (stage, sub-state, statuses), without
/// starting approvals or raising events.
/// </summary>
internal static class PortfolioSyncTargets
{
    private static readonly IReadOnlyList<string> Stages = [.. ProjectStages.ProjectPath.Union(ProjectStages.InitiativePath)];
    private static readonly IReadOnlyList<string> ProjectStatusCodes =
        [ProjectStatuses.Draft, ProjectStatuses.Active, ProjectStatuses.OnHold, ProjectStatuses.Cancelled, ProjectStatuses.Completed];
    private static readonly IReadOnlyList<string> SubStateCodes =
    [
        SubStates.Ownership, SubStates.CharterDraft, SubStates.CharterApproval, SubStates.Lpo, SubStates.TeamFormation, SubStates.PlanningDraft,
        SubStates.PlanningApproval, SubStates.Execution, SubStates.Completion, SubStates.ClosureDraft, SubStates.ClosureApproval, SubStates.ImpactDraft,
        SubStates.ImpactApproval, SubStates.Done,
    ];
    private static readonly IReadOnlyList<string> ClassificationCodes = [Classifications.Project, Classifications.Initiative];
    private static readonly IReadOnlyList<string> ItemStatusCodes = ["draft", "active", "inactive", "closed"];

    public static IServiceCollection AddPortfolioSyncTargets(this IServiceCollection services)
    {
        const string System = PortfolioPermissions.SystemCode;
        services.AddSyncTarget(SyncTarget.Entity<PortfolioDbContext, ProgramRequest>(ProgramRequest.EntityType, System, "طلب برنامج", "Program request")
            .Text("number", nameof(ProgramRequest.Number), required: true)
            .Choice("status", nameof(ProgramRequest.Status), RequestStatuses.All, required: true)
            .Text("approvalStatus", nameof(ProgramRequest.ApprovalStatus))
            .Text("nameAr", nameof(ProgramRequest.NameAr)).Text("nameEn", nameof(ProgramRequest.NameEn))
            .Text("description", nameof(ProgramRequest.Description)).Text("justifications", nameof(ProgramRequest.Justifications))
            .Text("scopeOfWork", nameof(ProgramRequest.ScopeOfWork)).Text("strategicObjectiveCode", nameof(ProgramRequest.StrategicObjectiveCode))
            .Lookup("programType", nameof(ProgramRequest.ProgramTypeCode), PortfolioPermissions.ProgramTypeLookup)
            .Text("programNature", nameof(ProgramRequest.ProgramNature))
            .Choice("classification", nameof(ProgramRequest.Classification), ClassificationCodes)
            .Bool("requiresBudget", nameof(ProgramRequest.RequiresBudget))
            .Number("estimatedBudget", nameof(ProgramRequest.EstimatedBudget)).Number("approvedBudget", nameof(ProgramRequest.ApprovedBudget))
            .Number("year1Cost", nameof(ProgramRequest.Year1Cost)).Number("year2Cost", nameof(ProgramRequest.Year2Cost))
            .Number("year3Cost", nameof(ProgramRequest.Year3Cost)).Number("year4Cost", nameof(ProgramRequest.Year4Cost))
            .Number("annualLicenceCost", nameof(ProgramRequest.AnnualLicenceCost)).Number("annualMaintenanceCost", nameof(ProgramRequest.AnnualMaintenanceCost))
            .Number("annualOtherCost", nameof(ProgramRequest.AnnualOtherCost))
            .Date("expectedStartDate", nameof(ProgramRequest.ExpectedStartDate)).Date("expectedEndDate", nameof(ProgramRequest.ExpectedEndDate))
            .Unit("unitId", nameof(ProgramRequest.UnitId)).Unit("sectionUnitId", nameof(ProgramRequest.SectionUnitId))
            .Person("requesterEmployeeId", nameof(ProgramRequest.RequesterEmployeeId))
            .Bool("screenCoreWork", nameof(ProgramRequest.ScreenCoreWork)).Bool("screenOneTime", nameof(ProgramRequest.ScreenOneTime))
            .Bool("screenTimeBound", nameof(ProgramRequest.ScreenTimeBound)).Bool("screenUniqueOutput", nameof(ProgramRequest.ScreenUniqueOutput))
            .Bool("screenMinCost", nameof(ProgramRequest.ScreenMinCost)).Bool("screenOperationalCosts", nameof(ProgramRequest.ScreenOperationalCosts))
            .Bool("othersBearCosts", nameof(ProgramRequest.OthersBearCosts)).Bool("othersCoverImplementation", nameof(ProgramRequest.OthersCoverImplementation))
            .Bool("returnExpected", nameof(ProgramRequest.ReturnExpected)).Bool("reductionExpected", nameof(ProgramRequest.ReductionExpected))
            .Lookup("mainOption", nameof(ProgramRequest.MainOptionCode), PortfolioPermissions.DeliveryOptionLookup)
            .Text("mainAdvantages", nameof(ProgramRequest.MainAdvantages)).Text("mainDisadvantages", nameof(ProgramRequest.MainDisadvantages))
            .Lookup("altOption", nameof(ProgramRequest.AltOptionCode), PortfolioPermissions.DeliveryOptionLookup)
            .Text("altAdvantages", nameof(ProgramRequest.AltAdvantages)).Text("altDisadvantages", nameof(ProgramRequest.AltDisadvantages))
            .Number("evaluationScore", nameof(ProgramRequest.EvaluationScore))
            .Text("procurementStatus", nameof(ProgramRequest.ProcurementStatus))
            .Default("status", RequestStatuses.Draft));

        services.AddSyncTarget(SyncTarget.Entity<PortfolioDbContext, Project>(Project.EntityType, System, "مشروع / مبادرة", "Project / initiative")
            .Text("number", nameof(Project.Number), required: true)
            .Choice("classification", nameof(Project.Classification), ClassificationCodes)
            .Reference("requestId", nameof(Project.RequestId), ProgramRequest.EntityType, required: true)
            .Text("nameAr", nameof(Project.NameAr)).Text("nameEn", nameof(Project.NameEn))
            .Text("description", nameof(Project.Description)).Text("scopeOfWork", nameof(Project.ScopeOfWork))
            .Choice("stage", nameof(Project.Stage), Stages, required: true)
            .Choice("status", nameof(Project.Status), ProjectStatusCodes, required: true)
            .Choice("subState", nameof(Project.SubState), SubStateCodes)
            .Text("approvalResult", nameof(Project.ApprovalResult))
            .Unit("unitId", nameof(Project.UnitId)).Unit("sectionUnitId", nameof(Project.SectionUnitId))
            .Text("strategicObjectiveCode", nameof(Project.StrategicObjectiveCode))
            .Lookup("programType", nameof(Project.ProgramTypeCode), PortfolioPermissions.ProgramTypeLookup)
            .Codes("operationalThemes", nameof(Project.OperationalThemes), PortfolioPermissions.ThemeLookup)
            .Number("budget", nameof(Project.Budget))
            .Number("year1ContractedCost", nameof(Project.Year1ContractedCost)).Number("year2ContractedCost", nameof(Project.Year2ContractedCost))
            .Number("year3ContractedCost", nameof(Project.Year3ContractedCost)).Number("year4ContractedCost", nameof(Project.Year4ContractedCost))
            .Number("year1ActualCost", nameof(Project.Year1ActualCost)).Number("year2ActualCost", nameof(Project.Year2ActualCost))
            .Number("year3ActualCost", nameof(Project.Year3ActualCost)).Number("year4ActualCost", nameof(Project.Year4ActualCost))
            .Number("annualOperationCost", nameof(Project.AnnualOperationCost)).Number("annualMaintenanceCost", nameof(Project.AnnualMaintenanceCost))
            .Number("annualLicenceCost", nameof(Project.AnnualLicenceCost)).Number("annualOtherCost", nameof(Project.AnnualOtherCost))
            .Date("plannedStart", nameof(Project.PlannedStart)).Date("plannedEnd", nameof(Project.PlannedEnd))
            .Person("ownerEmployeeId", nameof(Project.OwnerEmployeeId)).Person("pmEmployeeId", nameof(Project.PmEmployeeId))
            .Text("resourcesHuman", nameof(Project.ResourcesHuman)).Text("resourcesFinancial", nameof(Project.ResourcesFinancial))
            .Text("resourcesTechnical", nameof(Project.ResourcesTechnical)).Text("resourcesContract", nameof(Project.ResourcesContract))
            .Text("resourcesOther", nameof(Project.ResourcesOther))
            .Bool("lpoUploaded", nameof(Project.LpoUploaded))
            .Ratio("performance", nameof(Project.Performance)).Ratio("plannedProgress", nameof(Project.PlannedProgress))
            .Text("summaryOfAchievement", nameof(Project.SummaryOfAchievement))
            .Default("status", ProjectStatuses.Active)
            .After<ProjectSyncHook>());

        services.AddSyncTarget(SyncTarget.Entity<PortfolioDbContext, Milestone>(Milestone.EntityType, System, "مرحلة / بند خطة العمل", "Work plan item")
            .Reference("projectId", nameof(Milestone.ProjectId), Project.EntityType, required: true)
            .Text("code", nameof(Milestone.Code), required: true)
            .Reference("parentId", nameof(Milestone.ParentId), Milestone.EntityType)
            .Bool("isHeader", nameof(Milestone.IsHeader)).Bool("highLevel", nameof(Milestone.HighLevel))
            .Text("titleAr", nameof(Milestone.TitleAr)).Text("titleEn", nameof(Milestone.TitleEn))
            .Date("plannedStart", nameof(Milestone.PlannedStart)).Date("plannedEnd", nameof(Milestone.PlannedEnd))
            .Date("actualStart", nameof(Milestone.ActualStart)).Date("actualEnd", nameof(Milestone.ActualEnd))
            .Ratio("weight", nameof(Milestone.Weight)).Ratio("achieved", nameof(Milestone.MaxAchieved))
            .Number("cost", nameof(Milestone.Cost))
            .Choice("status", nameof(Milestone.Status), [MilestoneStatuses.NotStarted, MilestoneStatuses.Active, MilestoneStatuses.Completed, MilestoneStatuses.Cancelled])
            .Bool("isDeliverable", nameof(Milestone.IsDeliverable)).Text("mainDeliverable", nameof(Milestone.MainDeliverable))
            .Text("acceptanceCriteria", nameof(Milestone.AcceptanceCriteria)).Text("evidenceNote", nameof(Milestone.EvidenceNote))
            .WholeNumber("sort", nameof(Milestone.Sort)));

        services.AddSyncTarget(Child<ProjectKpi>(ProjectKpi.EntityType, "مؤشر أداء المشروع", "Project KPI")
            .Choice("nature", nameof(ProjectKpi.Nature), KpiNatures.All)
            .Text("nameAr", nameof(ProjectKpi.NameAr)).Text("nameEn", nameof(ProjectKpi.NameEn))
            .Text("equation", nameof(ProjectKpi.Equation)).Text("direction", nameof(ProjectKpi.Direction))
            .Number("target", nameof(ProjectKpi.Target)).WholeNumber("targetYear", nameof(ProjectKpi.TargetYear))
            .Number("currentResult", nameof(ProjectKpi.CurrentResult)).Text("projectGoal", nameof(ProjectKpi.ProjectGoal)));

        services.AddSyncTarget(Child<ProjectRisk>(ProjectRisk.EntityType, "خطر المشروع", "Project risk")
            .Text("title", nameof(ProjectRisk.Title)).Text("riskFactor", nameof(ProjectRisk.RiskFactor))
            .WholeNumber("probability", nameof(ProjectRisk.Probability), required: true).WholeNumber("impact", nameof(ProjectRisk.Impact), required: true)
            .WholeNumber("sourceScore", nameof(ProjectRisk.InherentScore)) // Salesforce additive score kept; the band is re-rated by the framework
            .Text("mitigationActions", nameof(ProjectRisk.MitigationActions))
            .Ratio("implementationPct", nameof(ProjectRisk.ImplementationPct)).Date("implementationEnd", nameof(ProjectRisk.ImplementationEnd))
            .Person("ownerEmployeeId", nameof(ProjectRisk.OwnerEmployeeId)).Text("rmsStatus", nameof(ProjectRisk.RmsStatus)));

        services.AddSyncTarget(Child<Stakeholder>(Stakeholder.EntityType, "صاحب مصلحة", "Stakeholder")
            .Text("name", nameof(Stakeholder.Name))
            .Lookup("classification", nameof(Stakeholder.ClassificationCode), PortfolioPermissions.StakeholderClassLookup)
            .Text("interestLevel", nameof(Stakeholder.InterestLevel)).Text("impactLevel", nameof(Stakeholder.ImpactLevel))
            .Text("communicationMethod", nameof(Stakeholder.CommunicationMethod)).Text("communicationFrequency", nameof(Stakeholder.CommunicationFrequency))
            .Text("email", nameof(Stakeholder.Email)).Text("phone", nameof(Stakeholder.Phone)));

        services.AddSyncTarget(Child<PlanningItem>(PlanningItem.EntityType, "بند تخطيط", "Planning item")
            .Choice("category", nameof(PlanningItem.Category), PlanningCategories.All, required: true)
            .Text("description", nameof(PlanningItem.Description)));

        services.AddSyncTarget(Child<Challenge>(Challenge.EntityType, "تحدٍّ / قيد", "Challenge / constraint")
            .Text("text", nameof(Challenge.Text)).Text("impact", nameof(Challenge.Impact))
            .Ratio("achievedPct", nameof(Challenge.AchievedPct)).Text("recommendation", nameof(Challenge.Recommendation))
            .Date("reportDate", nameof(Challenge.ReportDate)).Reference("milestoneId", nameof(Challenge.MilestoneId), Milestone.EntityType));

        services.AddSyncTarget(Child<Lesson>(Lesson.EntityType, "درس مستفاد", "Lesson learned")
            .Text("stage", nameof(Lesson.Stage)).Text("text", nameof(Lesson.Text))
            .Lookup("type", nameof(Lesson.TypeCode), PortfolioPermissions.LessonTypeLookup).Text("recommendation", nameof(Lesson.Recommendation)));

        services.AddSyncTarget(Child<Meeting>(Meeting.EntityType, "اجتماع فريق المشروع", "Project team meeting")
            .Timestamp("meetingAt", nameof(Meeting.MeetingAt), required: true).Text("title", nameof(Meeting.Title)).Text("notes", nameof(Meeting.Notes)));

        services.AddSyncTarget(Child<Contract>(Contract.EntityType, "عقد / أمر شراء", "Contract / purchase order")
            .Text("name", nameof(Contract.Name)).Text("number", nameof(Contract.Number)).Text("supplier", nameof(Contract.Supplier))
            .Date("issueDate", nameof(Contract.IssueDate)).Number("amount", nameof(Contract.Amount))
            .Text("contractStatus", nameof(Contract.ContractStatus)));

        services.AddSyncTarget(SyncTarget.Entity<PortfolioDbContext, ChangeRequest>(ChangeRequest.EntityType, System, "طلب تغيير", "Change request")
            .Reference("projectId", nameof(ChangeRequest.ProjectId), Project.EntityType, required: true)
            .Text("number", nameof(ChangeRequest.Number), required: true)
            .Choice("type", nameof(ChangeRequest.Type), ChangeTypes.All, required: true)
            .Text("reason", nameof(ChangeRequest.Reason)).Text("proposedDescription", nameof(ChangeRequest.ProposedDescription))
            .Date("requestedDate", nameof(ChangeRequest.RequestedDate))
            .Choice("status", nameof(ChangeRequest.Status), ChangeStatuses.All, required: true)
            .Person("newPmEmployeeId", nameof(ChangeRequest.NewPmEmployeeId)).Person("newOwnerEmployeeId", nameof(ChangeRequest.NewOwnerEmployeeId)));

        return services;
    }

    private static SyncTargetDefinition Child<T>(string code, string nameAr, string nameEn)
        where T : ProjectChild =>
        SyncTarget.Entity<PortfolioDbContext, T>(code, PortfolioPermissions.SystemCode, nameAr, nameEn)
            .Reference("projectId", nameof(ProjectChild.ProjectId), Project.EntityType, required: true)
            .Choice("status", nameof(ProjectChild.Status), ItemStatusCodes)
            .Default("status", "active");
}

/// <summary>Grants the Project Manager and Project Owner record roles of a synced project, so scope-based access works as for local projects.</summary>
internal sealed class ProjectSyncHook(IRecordRoleService recordRoles) : ISyncTargetHook
{
    public async Task OnWrittenAsync(Guid recordId, bool created, IReadOnlyDictionary<string, object?> values, object transactionContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        var holders = await recordRoles.GetHoldersAsync(PortfolioPermissions.ProjectResource, recordId, cancellationToken).ConfigureAwait(false);
        foreach (var (key, role) in new[] { ("pmEmployeeId", PortfolioPermissions.PmRole), ("ownerEmployeeId", PortfolioPermissions.OwnerRole) })
        {
            if (values.TryGetValue(key, out var value) && value is Guid employee
                && !holders.Any(h => h.RoleCode == role && h.PrincipalKind == RecordPrincipalKinds.Employee && h.PrincipalId == employee))
            {
                await recordRoles.GrantInTransactionAsync(transactionContext, PortfolioPermissions.ProjectResource, recordId, RecordPrincipalKinds.Employee, employee, role,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
