using System.Text.Json;
using System.Text.Json.Nodes;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Domain;

namespace Strategy.Modules.Requests.Domain;

internal static class RequestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string? json, T fallback) =>
        string.IsNullOrWhiteSpace(json) ? fallback : JsonSerializer.Deserialize<T>(json, Options) ?? fallback;

    public static JsonObject Object(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonNode.Parse(json) as JsonObject ?? [];
}

public static class TypeKinds
{
    public const string BuiltIn = "builtin";
    public const string Custom = "custom";
}

/// <summary>A request type (ADR 0080): built-in (seeded from a module's registration) or custom (form builder).</summary>
[Audited(EntityType)]
public sealed class RequestType : AuditableEntity
{
    public const string EntityType = "requests.type";
    public const int NameMaxLength = 300;
    public const int TextMaxLength = 4000;

    private RequestType()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public string Kind { get; private set; } = TypeKinds.Custom;

    public string? NameAr { get; private set; }

    public string? NameEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public string? DescriptionEn { get; private set; }

    public string? Category { get; private set; }

    public string Icon { get; private set; } = "file";

    public string NumberPrefix { get; private set; } = "REQ";

    public Guid? OwningUnitId { get; private set; }

    public string SubmittersJson { get; private set; } = "{}";

    public string FormJson { get; private set; } = "{}";

    public string StagesJson { get; private set; } = "[]";

    public string TemplatesJson { get; private set; } = "[]";

    public bool Active { get; private set; } = true;

    public bool ShowInCatalogue { get; private set; } = true;

    public bool Embeddable { get; private set; }

    public int SchemaVersion { get; private set; } = 1;

    public int Sort { get; private set; } = 100;

    public SubmitterRule Submitters => RequestJson.Read(SubmittersJson, new SubmitterRule());

    public FormSchema Form => RequestJson.Read(FormJson, new FormSchema());

    public IReadOnlyList<StageDefinition> Stages => RequestJson.Read<List<StageDefinition>>(StagesJson, []);

    public IReadOnlyList<PdfTemplate> Templates => RequestJson.Read<List<PdfTemplate>>(TemplatesJson, []);

    public bool IsBuiltIn => Kind == TypeKinds.BuiltIn;

    public static RequestType FromRegistration(RequestTypeRegistration registration) => new()
    {
        Code = registration.Code,
        SystemCode = registration.SystemCode,
        Kind = TypeKinds.BuiltIn,
        NameAr = registration.NameAr,
        NameEn = registration.NameEn,
        DescriptionAr = registration.DescriptionAr,
        DescriptionEn = registration.DescriptionEn,
        Category = registration.Category,
        Icon = registration.Icon,
        NumberPrefix = registration.NumberPrefix,
        FormJson = RequestJson.Write(registration.Form),
        StagesJson = RequestJson.Write(registration.Stages),
        TemplatesJson = RequestJson.Write(registration.Templates ?? []),
        ShowInCatalogue = registration.ShowInCatalogue,
        Sort = registration.Sort,
    };

    public static RequestType CreateCustom(string code, string systemCode) => new() { Code = code, SystemCode = systemCode, Kind = TypeKinds.Custom };

    /// <summary>Code-defined parts of a built-in type follow the module's registration on start-up (fields marked system, stage definitions).</summary>
    public bool SyncRegistration(RequestTypeRegistration registration, FormSchema merged)
    {
        var stages = RequestJson.Write(MergeStages(registration.Stages, Stages));
        var form = RequestJson.Write(merged);
        if (stages == StagesJson && form == FormJson)
        {
            return false;
        }

        if (form != FormJson)
        {
            SchemaVersion++;
        }

        StagesJson = stages;
        FormJson = form;
        return true;
    }

    /// <summary>Keeps the admin's SLA and processor roles on code-defined stages.</summary>
    private static List<StageDefinition> MergeStages(IReadOnlyList<StageDefinition> code, IReadOnlyList<StageDefinition> saved) =>
        code.Select(c => saved.FirstOrDefault(s => s.Code == c.Code) is { } s
            ? c with
            {
                NameAr = s.NameAr ?? c.NameAr,
                NameEn = s.NameEn ?? c.NameEn,
                SlaHours = s.SlaHours,
                ProcessorRoleKeys = s.ProcessorRoleKeys.Count > 0 ? s.ProcessorRoleKeys : c.ProcessorRoleKeys,
            }
            : c).ToList();

    public void Edit(string? nameAr, string? nameEn, string? descriptionAr, string? descriptionEn, string? category, string? icon, string? numberPrefix,
        Guid? owningUnitId, SubmitterRule submitters, bool active, bool showInCatalogue, bool embeddable, int sort)
    {
        NameAr = Clean(nameAr);
        NameEn = Clean(nameEn);
        DescriptionAr = Clean(descriptionAr);
        DescriptionEn = Clean(descriptionEn);
        Category = Clean(category);
        Icon = Clean(icon) ?? "file";
        NumberPrefix = (Clean(numberPrefix) ?? NumberPrefix).ToUpperInvariant();
        OwningUnitId = owningUnitId;
        SubmittersJson = RequestJson.Write(submitters);
        Active = active;
        ShowInCatalogue = showInCatalogue;
        Embeddable = embeddable;
        Sort = sort;
    }

    public void SetForm(FormSchema form)
    {
        var json = RequestJson.Write(form);
        if (json != FormJson)
        {
            FormJson = json;
            SchemaVersion++;
        }
    }

    public void SetStages(IReadOnlyList<StageDefinition> stages) => StagesJson = RequestJson.Write(stages);

    public void SetTemplates(IReadOnlyList<PdfTemplate> templates) => TemplatesJson = RequestJson.Write(templates);

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A request of any type (docs/design/requests.md §2.3).</summary>
[Audited(EntityType)]
public sealed class Request : AuditableEntity
{
    public const string EntityType = "requests.request";
    public const int TitleMaxLength = 400;

    private Request()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public Guid TypeId { get; private set; }

    public string TypeCode { get; private set; } = string.Empty;

    public string SystemCode { get; private set; } = string.Empty;

    public int SchemaVersion { get; private set; }

    public string? TitleAr { get; private set; }

    public string? TitleEn { get; private set; }

    public Guid? RequesterUserId { get; private set; }

    public Guid? RequesterEmployeeId { get; private set; }

    public Guid? UnitId { get; private set; }

    public string Status { get; private set; } = RequestStatuses.Draft;

    public string? Stage { get; private set; }

    /// <summary>Form values; <c>secret</c> values are stored encrypted (ADR 0086).</summary>
    public string DataJson { get; private set; } = "{}";

    /// <summary>Mini-form values per stage (secrets encrypted).</summary>
    public string ProcessDataJson { get; private set; } = "{}";

    public string? SubjectType { get; private set; }

    public Guid? SubjectId { get; private set; }

    public string? Outcome { get; private set; }

    public int Attempt { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset? StageEnteredAt { get; private set; }

    public DateTimeOffset? StageDueAt { get; private set; }

    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CancelReason { get; private set; }

    public string Source { get; private set; } = "ui";

    public string? TitleSearch { get; private set; }

    public static Request Create(Guid typeId, string typeCode, string systemCode, int schemaVersion, string number, Guid? userId, Guid? employeeId, Guid? unitId,
        string source) => new()
        {
            Number = number,
            TypeId = typeId,
            TypeCode = typeCode,
            SystemCode = systemCode,
            SchemaVersion = schemaVersion,
            RequesterUserId = userId,
            RequesterEmployeeId = employeeId,
            UnitId = unitId,
            Source = source,
        };

    public JsonObject Data => RequestJson.Object(DataJson);

    public JsonObject ProcessData => RequestJson.Object(ProcessDataJson);

    public void SetData(JsonObject data, int schemaVersion, string? titleAr, string? titleEn, Guid? unitId)
    {
        DataJson = data.ToJsonString(RequestJson.Options);
        SchemaVersion = schemaVersion;
        SetTitle(titleAr, titleEn);
        if (unitId is not null)
        {
            UnitId = unitId;
        }
    }

    public void SetTitle(string? titleAr, string? titleEn)
    {
        TitleAr = Trim(titleAr);
        TitleEn = Trim(titleEn);
        TitleSearch = Platform.Kernel.Text.SearchText.Normalise(Number, TitleAr, TitleEn);
    }

    public void SetProcessData(string stage, JsonObject values)
    {
        var all = ProcessData;
        all[stage] = values;
        ProcessDataJson = all.ToJsonString(RequestJson.Options);
    }

    public void SetSubject(string? type, Guid? id)
    {
        SubjectType = type ?? SubjectType;
        SubjectId = id ?? SubjectId;
    }

    public void SetOutcome(string? outcome) => Outcome = outcome ?? Outcome;

    /// <summary>Applies a lifecycle transition (already checked by <see cref="RequestLifecycle"/>).</summary>
    public void MoveTo(string status, string? stage, DateTimeOffset now, DateTimeOffset? dueAt = null)
    {
        if (Status is RequestStatuses.Draft or RequestStatuses.Returned && status is RequestStatuses.InReview or RequestStatuses.InProgress)
        {
            SubmittedAt = now;
            Attempt++;
        }

        if (stage != Stage || status != Status)
        {
            StageEnteredAt = now;
            OverdueNotifiedAt = null;
        }

        Status = status;
        Stage = stage;
        StageDueAt = dueAt;
        if (RequestStatuses.IsFinal(status))
        {
            CompletedAt = now;
            StageDueAt = null;
        }
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        CancelReason = reason.Trim();
        MoveTo(RequestStatuses.Cancelled, Stage, now);
    }

    public void MarkOverdueNotified(DateTimeOffset now) => OverdueNotifiedAt = now;

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > TitleMaxLength ? value.Trim()[..TitleMaxLength] : value.Trim();
}

/// <summary>Append-only timeline of a request (actions, stage moves, mini-form values with secrets masked).</summary>
public sealed class RequestEvent : Entity
{
    private RequestEvent()
    {
    }

    public Guid RequestId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string? FromStatus { get; private set; }

    public string? ToStatus { get; private set; }

    public string? FromStage { get; private set; }

    public string? ToStage { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? ActorName { get; private set; }

    public Guid? OnBehalfOfId { get; private set; }

    public string? Comment { get; private set; }

    public string? DataJson { get; private set; }

    public DateTimeOffset At { get; private set; }

    public static RequestEvent Create(Guid requestId, string action, string? fromStatus, string? toStatus, string? fromStage, string? toStage, Guid? actor,
        string? actorName, Guid? onBehalfOf, string? comment, JsonObject? data, DateTimeOffset at) => new()
        {
            RequestId = requestId,
            Action = action,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            FromStage = fromStage,
            ToStage = toStage,
            ActorUserId = actor,
            ActorName = actorName,
            OnBehalfOfId = onBehalfOf,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            DataJson = data is null || data.Count == 0 ? null : data.ToJsonString(RequestJson.Options),
            At = at,
        };
}
