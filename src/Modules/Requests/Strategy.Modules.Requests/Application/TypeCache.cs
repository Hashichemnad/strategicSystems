using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Requests.Contracts;
using Strategy.Modules.Requests.Domain;
using Strategy.Modules.Requests.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Organisation.Contracts;
using Strategy.Platform.Workflow;

namespace Strategy.Modules.Requests.Application;

/// <summary>An immutable copy of a request type (cached).</summary>
public sealed record TypeModel(
    Guid Id,
    string Code,
    string SystemCode,
    string Kind,
    string? NameAr,
    string? NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? Category,
    string Icon,
    string NumberPrefix,
    Guid? OwningUnitId,
    SubmitterRule Submitters,
    FormSchema Form,
    IReadOnlyList<StageDefinition> Stages,
    IReadOnlyList<PdfTemplate> Templates,
    bool Active,
    bool ShowInCatalogue,
    bool Embeddable,
    int SchemaVersion,
    int Sort,
    uint Version)
{
    public bool IsBuiltIn => Kind == TypeKinds.BuiltIn;

    public StageDefinition? Stage(string? code) => code is null ? null : Stages.FirstOrDefault(s => s.Code == code);

    public static TypeModel From(RequestType t) => new(t.Id, t.Code, t.SystemCode, t.Kind, t.NameAr, t.NameEn, t.DescriptionAr, t.DescriptionEn, t.Category, t.Icon,
        t.NumberPrefix, t.OwningUnitId, t.Submitters, t.Form, t.Stages, t.Templates, t.Active, t.ShowInCatalogue, t.Embeddable, t.SchemaVersion, t.Sort, t.Version);
}

/// <summary>
/// Every request type, cached for 30 seconds and refreshed at once when this instance saves a type. Also the dynamic approval processes of
/// custom types (ADR 0081).
/// </summary>
internal sealed class RequestTypeCache(IServiceScopeFactory scopes) : IApprovalProcessSource
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly Lock _lock = new();
    private IReadOnlyList<TypeModel>? _types;
    private IReadOnlyList<ApprovalProcessDefinition> _processes = [];
    private DateTimeOffset _loadedAt;

    public IReadOnlyList<TypeModel> All
    {
        get
        {
            lock (_lock)
            {
                if (_types is null || DateTimeOffset.UtcNow - _loadedAt > Lifetime)
                {
                    Load();
                }

                return _types ?? [];
            }
        }
    }

    public TypeModel? Find(string code) => All.FirstOrDefault(t => t.Code == code);

    public TypeModel? Find(Guid id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>Every approval process key used by an approval stage of a type (the framework hook handles them all).</summary>
    public IReadOnlyCollection<string> ProcessKeys => All.SelectMany(t => t.Stages).Where(s => s.ProcessKey is not null).Select(s => s.ProcessKey!).ToHashSet();

    public void Invalidate()
    {
        lock (_lock)
        {
            _types = null;
        }
    }

    public IReadOnlyCollection<ApprovalProcessDefinition> GetProcesses()
    {
        _ = All;
        return _processes;
    }

    private void Load()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RequestsDbContext>();
            var rows = db.Types.AsNoTracking().OrderBy(t => t.Sort).ThenBy(t => t.Code).ToList();
            _types = rows.Select(TypeModel.From).ToList();
            _processes = _types.Where(t => !t.IsBuiltIn).SelectMany(t => t.Stages.Where(s => s.Kind == StageKinds.Approval && s.ProcessKey is not null)
                .Select(s => ProcessOf(t, s))).ToList();
            _loadedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is Npgsql.PostgresException or InvalidOperationException)
        {
            // Before migrations (first start): no types yet.
            _types = [];
            _processes = [];
            _loadedAt = DateTimeOffset.UtcNow.AddSeconds(-25);
        }
    }

    /// <summary>The process of a custom type's approval stage: one Requests office step by default (ADR 0081).</summary>
    public static ApprovalProcessDefinition ProcessOf(TypeModel type, StageDefinition stage) => new(
        stage.ProcessKey!, RequestPermissions.SystemCode,
        $"اعتماد طلب: {type.NameAr ?? type.NameEn}", $"Request approval: {type.NameEn ?? type.NameAr}",
        Request.EntityType, RequestPermissions.Read, RequestResource.LinkTemplate,
        VariablesOf(type.Form),
        [
            new ApprovalStepDefinition("office", "مكتب الاستراتيجية / مكتب إدارة المشاريع", "Strategy Office / PM Office",
                new ApproverRule(ApproverRuleKinds.Role, RoleKey: RequestPermissions.OfficeRoleKey), SlaHours: 24),
        ],
        RequestPermissions.ApproverRole);

    /// <summary><c>requestType</c> plus <c>f_&lt;code&gt;</c> for number / currency / checkbox / picklist fields.</summary>
    public static IReadOnlyList<ExpressionVariable> VariablesOf(FormSchema form) =>
    [
        new(RequestResource.TypeVariable, ExpressionValueType.Text, "نوع الطلب", "Request type"),
        .. form.AllFields.Where(f => FieldTypes.Variables.Contains(f.Type)).Select(f => new ExpressionVariable(
            $"f_{f.Code}",
            f.Type switch
            {
                FieldTypes.Checkbox => ExpressionValueType.Boolean,
                FieldTypes.Picklist => ExpressionValueType.Text,
                _ => ExpressionValueType.Number,
            },
            f.LabelAr ?? f.LabelEn ?? f.Code, f.LabelEn ?? f.LabelAr ?? f.Code)),
    ];
}

/// <summary>Role holding of the current user (role keys, with unit assignments covering the units below) — cached per scope.</summary>
internal sealed class RoleMembership(IRoleHolderQuery roles, IEmployeeDirectory employees, IOrgDirectory org)
{
    private readonly Dictionary<string, RoleHolders> _holders = new(StringComparer.Ordinal);
    private IReadOnlyList<Guid>? _unitPath;

    public async Task<bool> HoldsAnyAsync(ClaimsPrincipal user, IEnumerable<string> roleKeys, CancellationToken ct)
    {
        foreach (var key in roleKeys)
        {
            if (await HoldsAsync(user, key, ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> HoldsAsync(ClaimsPrincipal user, string roleKey, CancellationToken ct)
    {
        if (!_holders.TryGetValue(roleKey, out var holders))
        {
            holders = await roles.GetHoldersAsync(roleKey, ct).ConfigureAwait(false);
            _holders[roleKey] = holders;
        }

        if (!holders.Exists)
        {
            return false;
        }

        if (holders.Everyone || (user.UserId() is { } id && holders.UserIds.Contains(id)))
        {
            return true;
        }

        return holders.UnitIds.Count > 0 && (await UnitPathAsync(user, ct).ConfigureAwait(false)).Any(holders.UnitIds.Contains);
    }

    public async Task<IReadOnlyList<Guid>> UnitPathAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (_unitPath is not null)
        {
            return _unitPath;
        }

        var unit = user.EmployeeIdOf() is { } employee ? (await employees.GetAsync(employee, ct).ConfigureAwait(false))?.UnitId : null;
        _unitPath = unit is { } u ? (await org.GetPathAsync(u, cancellationToken: ct).ConfigureAwait(false)).Select(p => p.Id).ToList() : [];
        return _unitPath;
    }
}
