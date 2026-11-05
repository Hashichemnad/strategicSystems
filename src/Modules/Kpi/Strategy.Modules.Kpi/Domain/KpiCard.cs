using System.Text.Json;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Kpi.Domain;

/// <summary>Everything an author sets on a card (form / API / AI builder).</summary>
public sealed record CardValues(
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? MeasuringEquation,
    string? ProcessName,
    DateOnly? InitiatedOn,
    string? TypeCode,
    string? IndexClassificationCode,
    string? EffectivenessCode,
    string Nature,
    IReadOnlyList<string>? SourceCodes,
    string? MeasuringMethodCode,
    bool CompetitivenessIndicator,
    bool IsGlobalStar,
    string UnitCode,
    string MeasuringPeriod,
    string Direction,
    string TargetType,
    string? TargetSettingMechanism,
    bool AllowsNegative,
    Guid? ObjectiveId,
    string? SectorGoal,
    string? NationalGoalCode,
    string? VisionLinkCode,
    IReadOnlyList<string>? GemCriteriaCodes,
    string? CriteriaText,
    string? ExcellenceLink,
    Guid? DepartmentUnitId,
    Guid? ResponsibleUnitId,
    Guid? ResponsibleEmployeeId,
    Guid? ResponsibleTeamId,
    string? BenchmarkDescription,
    decimal? BenchmarkResult,
    string Level,
    Guid? ParentCardId,
    decimal? Weight,
    decimal? BaselineValue,
    int? BaselineYear,
    decimal? GreenFrom,
    decimal? AmberFrom,
    string? Formula,
    IReadOnlyList<DataElement>? DataElements,
    IReadOnlyList<ScaleLevel>? Scale);

/// <summary>The KPI definition (SPEC §2.3 A).</summary>
[Audited(EntityType)]
public sealed class KpiCard : AuditableEntity
{
    public const string EntityType = "kpi.card";
    public const int NameMaxLength = 400;
    public const int TextMaxLength = 8000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private KpiCard()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public string? MeasuringEquation { get; private set; }

    public string? ProcessName { get; private set; }

    public string Status { get; private set; } = CardStatuses.Active;

    public DateOnly? InitiatedOn { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    public string? TypeCode { get; private set; }

    public string? IndexClassificationCode { get; private set; }

    public string? EffectivenessCode { get; private set; }

    public string Nature { get; private set; } = Natures.Quantitative;

    public string SourceCodes { get; private set; } = "[]";

    public string? MeasuringMethodCode { get; private set; }

    public bool CompetitivenessIndicator { get; private set; }

    public bool IsGlobalStar { get; private set; }

    public string UnitCode { get; private set; } = "number";

    public string MeasuringPeriod { get; private set; } = MeasuringPeriods.Quarterly;

    public string Direction { get; private set; } = Contracts.Directions.Increase;

    public string TargetType { get; private set; } = Contracts.TargetTypes.SingleValue;

    public string? TargetSettingMechanism { get; private set; }

    public bool AllowsNegative { get; private set; }

    public Guid? ObjectiveId { get; private set; }

    public string? SectorGoal { get; private set; }

    public string? NationalGoalCode { get; private set; }

    public string? VisionLinkCode { get; private set; }

    public string GemCriteriaCodes { get; private set; } = "[]";

    public string? CriteriaText { get; private set; }

    public string? ExcellenceLink { get; private set; }

    public Guid? DepartmentUnitId { get; private set; }

    public Guid? ResponsibleUnitId { get; private set; }

    public Guid? ResponsibleEmployeeId { get; private set; }

    public Guid? ResponsibleTeamId { get; private set; }

    public string? BenchmarkDescription { get; private set; }

    public decimal? BenchmarkResult { get; private set; }

    public string Level { get; private set; } = CardLevels.Organisation;

    public Guid? ParentCardId { get; private set; }

    public decimal? Weight { get; private set; }

    public decimal? BaselineValue { get; private set; }

    public int? BaselineYear { get; private set; }

    public decimal? GreenFrom { get; private set; }

    public decimal? AmberFrom { get; private set; }

    public string? Formula { get; private set; }

    public string DataElements { get; private set; } = "[]";

    public string Scale { get; private set; } = "[]";

    [NotAudited]
    public decimal? MaxAchieved { get; private set; }

    public int DefinitionVersion { get; private set; } = 1;

    public static KpiCard Create(string code) => new() { Code = code.Trim() };

    public IReadOnlyList<string> Sources => Read<List<string>>(SourceCodes) ?? [];

    public IReadOnlyList<string> GemCriteria => Read<List<string>>(GemCriteriaCodes) ?? [];

    public IReadOnlyList<DataElement> Elements => Read<List<DataElement>>(DataElements) ?? [];

    public IReadOnlyList<ScaleLevel> ScaleLevels => Read<List<ScaleLevel>>(Scale) ?? [];

    public bool IsComposite => !string.IsNullOrWhiteSpace(Formula) && Elements.Count > 0;

    public bool IsQualitative => Nature == Natures.Qualitative;

    public void SetCode(string code) => Code = code.Trim();

    public void Edit(CardValues v)
    {
        ArgumentNullException.ThrowIfNull(v);
        NameAr = StrategyElement.Clean(v.NameAr);
        NameEn = StrategyElement.Clean(v.NameEn);
        DescriptionAr = StrategyElement.Clean(v.DescriptionAr);
        DescriptionEn = StrategyElement.Clean(v.DescriptionEn);
        MeasuringEquation = StrategyElement.Clean(v.MeasuringEquation);
        ProcessName = StrategyElement.Clean(v.ProcessName);
        InitiatedOn = v.InitiatedOn;
        TypeCode = StrategyElement.Clean(v.TypeCode);
        IndexClassificationCode = StrategyElement.Clean(v.IndexClassificationCode);
        EffectivenessCode = StrategyElement.Clean(v.EffectivenessCode);
        Nature = v.Nature;
        SourceCodes = Write(v.SourceCodes ?? []);
        MeasuringMethodCode = StrategyElement.Clean(v.MeasuringMethodCode);
        CompetitivenessIndicator = v.CompetitivenessIndicator;
        IsGlobalStar = v.IsGlobalStar;
        UnitCode = v.UnitCode;
        MeasuringPeriod = v.MeasuringPeriod;
        Direction = v.Direction;
        TargetType = v.TargetType;
        TargetSettingMechanism = StrategyElement.Clean(v.TargetSettingMechanism);
        AllowsNegative = v.AllowsNegative;
        ObjectiveId = v.ObjectiveId;
        SectorGoal = StrategyElement.Clean(v.SectorGoal);
        NationalGoalCode = StrategyElement.Clean(v.NationalGoalCode);
        VisionLinkCode = StrategyElement.Clean(v.VisionLinkCode);
        GemCriteriaCodes = Write(v.GemCriteriaCodes ?? []);
        CriteriaText = StrategyElement.Clean(v.CriteriaText);
        ExcellenceLink = StrategyElement.Clean(v.ExcellenceLink);
        DepartmentUnitId = v.DepartmentUnitId;
        ResponsibleUnitId = v.ResponsibleUnitId;
        ResponsibleEmployeeId = v.ResponsibleEmployeeId;
        ResponsibleTeamId = v.ResponsibleTeamId;
        BenchmarkDescription = StrategyElement.Clean(v.BenchmarkDescription);
        BenchmarkResult = v.BenchmarkResult;
        Level = v.Level;
        ParentCardId = v.ParentCardId;
        Weight = v.Weight;
        BaselineValue = v.BaselineValue;
        BaselineYear = v.BaselineYear;
        GreenFrom = v.GreenFrom;
        AmberFrom = v.AmberFrom;
        Formula = StrategyElement.Clean(v.Formula);
        DataElements = Write(v.DataElements ?? []);
        Scale = Write((v.Scale ?? []).OrderBy(s => s.Value).ToList());
    }

    /// <summary>The controlled part of the definition (ADR 0063): target type, equation / formula, unit and direction.</summary>
    public ControlledDefinition Controlled => new(MeasuringEquation, Formula, UnitCode, Direction, TargetType);

    public void ApplyControlled(ControlledDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        MeasuringEquation = d.MeasuringEquation;
        Formula = d.Formula;
        UnitCode = d.UnitCode;
        Direction = d.Direction;
        TargetType = d.TargetType;
    }

    public void NextVersion() => DefinitionVersion++;

    public void Activate()
    {
        Status = CardStatuses.Active;
        ClosedOn = null;
    }

    public void Deactivate(DateOnly on)
    {
        Status = CardStatuses.Inactive;
        ClosedOn = on;
    }

    public void SetMaxAchieved(decimal? value) => MaxAchieved = value;

    /// <summary>Benchmark fields from an approved benchmarking analysis (ADR 0128): not a definition change, audited as any card change.</summary>
    public void SetBenchmark(string? description, decimal? result)
    {
        BenchmarkDescription = StrategyElement.Clean(description) ?? BenchmarkDescription;
        BenchmarkResult = result ?? BenchmarkResult;
    }

    public string Snapshot() => JsonSerializer.Serialize(new
    {
        Code,
        NameAr,
        NameEn,
        DescriptionAr,
        DescriptionEn,
        MeasuringEquation,
        ProcessName,
        Status,
        TypeCode,
        Nature,
        UnitCode,
        MeasuringPeriod,
        Direction,
        TargetType,
        MeasuringMethodCode,
        ObjectiveId,
        ResponsibleUnitId,
        ResponsibleEmployeeId,
        Level,
        ParentCardId,
        Weight,
        BaselineValue,
        BaselineYear,
        Formula,
        DataElements = Elements,
        Scale = ScaleLevels,
        GemCriteria,
        Sources,
    }, Json);

    internal static T? Read<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);
}

/// <summary>Controlled definition items (target type, equation, unit, direction) — design note §2.3.</summary>
public sealed record ControlledDefinition(string? MeasuringEquation, string? Formula, string UnitCode, string Direction, string TargetType);

/// <summary>Multi-year target of a card (strategy cycle, SPEC §2.7).</summary>
[Audited(EntityType)]
[AuditParent(KpiCard.EntityType, nameof(CardId))]
public sealed class CardTarget : AuditableEntity
{
    public const string EntityType = "kpi.card-target";

    private CardTarget()
    {
    }

    public Guid CardId { get; private set; }

    public int Year { get; private set; }

    public string TargetType { get; private set; } = Contracts.TargetTypes.SingleValue;

    public decimal? Target { get; private set; }

    public decimal? RangeMin { get; private set; }

    public decimal? RangeMax { get; private set; }

    public static CardTarget Create(Guid cardId, int year) => new() { CardId = cardId, Year = year };

    public void Set(string targetType, decimal? target, decimal? min, decimal? max)
    {
        TargetType = targetType;
        Target = targetType == Contracts.TargetTypes.SingleValue ? target : null;
        RangeMin = targetType == Contracts.TargetTypes.Range ? min : null;
        RangeMax = targetType == Contracts.TargetTypes.Range ? max : null;
    }
}

/// <summary>A saved definition version (full history, SPEC §2.7).</summary>
[Audited(EntityType)]
[AuditParent(KpiCard.EntityType, nameof(CardId))]
public sealed class CardVersion : AuditableEntity
{
    public const string EntityType = "kpi.card-version";

    private CardVersion()
    {
    }

    public Guid CardId { get; private set; }

    public int VersionNo { get; private set; }

    public string Snapshot { get; private set; } = "{}";

    public string? Reason { get; private set; }

    public Guid? ChangeRequestId { get; private set; }

    public static CardVersion Of(KpiCard card, string? reason, Guid? changeRequestId)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new CardVersion
        {
            CardId = card.Id,
            VersionNo = card.DefinitionVersion,
            Snapshot = card.Snapshot(),
            Reason = StrategyElement.Clean(reason),
            ChangeRequestId = changeRequestId,
        };
    }
}

/// <summary>A controlled definition change waiting for Strategy Office approval (ADR 0063).</summary>
[Audited(EntityType)]
public sealed class DefinitionChange : AuditableEntity
{
    public const string EntityType = "kpi.definition-change";

    private DefinitionChange()
    {
    }

    public Guid CardId { get; private set; }

    public int Year { get; private set; }

    /// <summary>JSON of <see cref="ProposedChange"/>.</summary>
    public string Proposed { get; private set; } = "{}";

    /// <summary>JSON of the definition before the change (shown to the approver).</summary>
    public string Current { get; private set; } = "{}";

    public string Reason { get; private set; } = string.Empty;

    public string Status { get; private set; } = ChangeStatuses.Draft;

    public Guid? RequestedByUserId { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionComment { get; private set; }

    public ProposedChange Change => KpiCard.Read<ProposedChange>(Proposed) ?? new ProposedChange(null, null, null, null, null, null, null, null);

    public static DefinitionChange Create(KpiCard card, int year, ProposedChange proposed, ProposedChange current, string reason, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new DefinitionChange
        {
            CardId = card.Id,
            Year = year,
            Proposed = KpiCard.Write(proposed),
            Current = KpiCard.Write(current),
            Reason = reason.Trim(),
            Status = ChangeStatuses.Pending,
            RequestedByUserId = userId,
        };
    }

    public void Decide(string status, DateTimeOffset at, string? comment)
    {
        Status = status;
        DecidedAt = at;
        DecisionComment = StrategyElement.Clean(comment);
    }

    public void Resubmit()
    {
        Status = ChangeStatuses.Pending;
        DecidedAt = null;
    }
}

/// <summary>What a definition change proposes (null = unchanged).</summary>
public sealed record ProposedChange(
    string? MeasuringEquation, string? Formula, string? UnitCode, string? Direction, string? TargetType, decimal? Target, decimal? RangeMin, decimal? RangeMax);
