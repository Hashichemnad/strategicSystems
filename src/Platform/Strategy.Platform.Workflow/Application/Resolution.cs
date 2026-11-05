using Strategy.Platform.Access;
using Strategy.Platform.Kernel.Expressions;
using Strategy.Platform.Organisation.Contracts;

namespace Strategy.Platform.Workflow.Application;

/// <summary>
/// The registered processes: code definitions plus the dynamic ones of <see cref="IApprovalProcessSource"/>s (ADR 0081), with the variables
/// their conditions may use (built-in + the module's). A code definition wins over a dynamic one with the same key.
/// </summary>
internal sealed class ProcessRegistry
{
    private readonly Dictionary<string, ApprovalProcessDefinition> _byKey;
    private readonly List<IApprovalProcessSource> _sources;

    public ProcessRegistry(IEnumerable<ApprovalProcessDefinition> definitions, IEnumerable<IApprovalProcessSource> sources)
    {
        _byKey = definitions.ToDictionary(d => d.Key, StringComparer.Ordinal);
        _sources = sources.ToList();
    }

    public IReadOnlyCollection<ApprovalProcessDefinition> All
    {
        get
        {
            if (_sources.Count == 0)
            {
                return _byKey.Values;
            }

            var all = new Dictionary<string, ApprovalProcessDefinition>(_byKey, StringComparer.Ordinal);
            foreach (var definition in _sources.SelectMany(s => s.GetProcesses()))
            {
                all.TryAdd(definition.Key, definition);
            }

            return all.Values;
        }
    }

    public ApprovalProcessDefinition? Find(string key) =>
        _byKey.GetValueOrDefault(key) ?? _sources.SelectMany(s => s.GetProcesses()).FirstOrDefault(d => d.Key == key);

    /// <summary>True when the process comes from a dynamic source (its version 1 is seeded on first use).</summary>
    public bool IsDynamic(string key) => !_byKey.ContainsKey(key) && Find(key) is not null;

    public static IReadOnlyList<ExpressionVariable> VariablesOf(ApprovalProcessDefinition process) =>
        [.. BuiltInVariables.All, .. process.Variables.Where(v => BuiltInVariables.All.All(b => !string.Equals(b.Name, v.Name, StringComparison.OrdinalIgnoreCase)))];
}

/// <summary>An approver after resolution (snapshotted on the task).</summary>
internal sealed record ResolvedApprover(Guid UserId, Guid? EmployeeId, string? NameAr, string? NameEn)
{
    public string DisplayName => NameEn ?? NameAr ?? UserId.ToString("D");

    public PersonName ToPerson() => new(UserId, NameAr ?? NameEn, NameEn ?? NameAr);
}

/// <summary>Result of one rule: the approvers and notes for the preview (message keys).</summary>
internal sealed record Resolution(IReadOnlyList<ResolvedApprover> Approvers, IReadOnlyList<string> Notes);

/// <summary>What a rule is resolved against: the process, the record, the submitter and the level settings.</summary>
internal sealed record ResolutionContext(
    ApprovalProcessDefinition Process,
    ApprovalSubject Subject,
    Guid? SubmitterEmployeeId,
    ApprovalSettings Settings);

/// <summary>Organisation facts used by rules and by the built-in condition variables (ADR 0025).</summary>
internal sealed class OrgRelations(IOrgDirectory org, IEmployeeDirectory employees)
{
    /// <summary>The unit an org-relation rule starts from: the record's unit, or the submitter's.</summary>
    public async Task<Guid?> UnitOfAsync(string? of, ResolutionContext context, CancellationToken ct)
    {
        if (of == ApproverRuleSubjects.Submitter)
        {
            return context.SubmitterEmployeeId is { } submitter ? (await employees.GetAsync(submitter, ct).ConfigureAwait(false))?.UnitId : null;
        }

        var start = context.Subject.UnitId
            ?? (context.SubmitterEmployeeId is { } fallback ? (await employees.GetAsync(fallback, ct).ConfigureAwait(false))?.UnitId : null);
        if (of == ApproverRuleSubjects.Organisation && start is { } leaf)
        {
            var path = await org.GetPathAsync(leaf, cancellationToken: ct).ConfigureAwait(false);
            return path.Count > 0 ? path[0].Id : leaf;
        }

        return start;
    }

    /// <summary>The nearest unit at <paramref name="levelCode"/> on the unit's path (the unit itself first), or null.</summary>
    public async Task<UnitReference?> NearestAtLevelAsync(Guid unitId, string levelCode, CancellationToken ct)
    {
        var path = await org.GetPathAsync(unitId, cancellationToken: ct).ConfigureAwait(false);
        return path.LastOrDefault(u => string.Equals(u.LevelCode, levelCode, StringComparison.Ordinal));
    }

    public Task<Guid?> HeadOfAsync(Guid unitId, CancellationToken ct) => org.GetHeadOfUnitAsync(unitId, cancellationToken: ct);

    public async Task<Guid?> HeadAtLevelAsync(Guid? unitId, string levelCode, CancellationToken ct) =>
        unitId is { } unit && await NearestAtLevelAsync(unit, levelCode, ct).ConfigureAwait(false) is { } found
            ? await HeadOfAsync(found.Id, ct).ConfigureAwait(false)
            : null;

    /// <summary>The built-in condition variables of a submission (design note §4), employee ids as text.</summary>
    public async Task<Dictionary<string, object?>> BuiltInsAsync(ResolutionContext context, CancellationToken ct)
    {
        var unit = await UnitOfAsync(ApproverRuleSubjects.Record, context, ct).ConfigureAwait(false);
        var submitter = context.SubmitterEmployeeId;
        Guid? manager = submitter is { } s ? await employees.GetManagerAsync(s, cancellationToken: ct).ConfigureAwait(false) : null;
        var head = unit is { } u ? await HeadOfAsync(u, ct).ConfigureAwait(false) : null;
        var level = unit is { } lu ? (await org.GetUnitAsync(lu, ct).ConfigureAwait(false))?.LevelCode : null;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [BuiltInVariables.Submitter] = submitter?.ToString("D"),
            [BuiltInVariables.SubmitterManager] = manager?.ToString("D"),
            [BuiltInVariables.SectionManager] = (await HeadAtLevelAsync(unit, context.Settings.SectionLevelCode, ct).ConfigureAwait(false))?.ToString("D"),
            [BuiltInVariables.DepartmentDirector] = (await HeadAtLevelAsync(unit, context.Settings.DepartmentLevelCode, ct).ConfigureAwait(false))?.ToString("D"),
            [BuiltInVariables.UnitHead] = head?.ToString("D"),
            [BuiltInVariables.UnitLevel] = level,
            [BuiltInVariables.SubmitterIsUnitHead] = submitter is not null && head == submitter,
        };
    }
}

/// <summary>Resolves an approver rule to users at runtime against Organisation and Access (design note §4, ADR 0025).</summary>
internal sealed class ApproverResolver(
    OrgRelations relations,
    IEmployeeDirectory employees,
    IUserDirectory users,
    IRoleHolderQuery roles,
    IRecordRoleService recordRoles,
    ISystemState systems,
    IOrgDirectory org,
    IEnumerable<ITeamRoleResolver> teamRoles)
{
    public async Task<Resolution> ResolveAsync(ApproverRule rule, ResolutionContext context, CancellationToken ct)
    {
        var notes = new List<string>();
        var employeeIds = new List<Guid>();
        var userIds = new List<Guid>();
        switch (rule.Kind)
        {
            case ApproverRuleKinds.Role:
                {
                    var holders = await roles.GetHoldersAsync(rule.RoleKey ?? string.Empty, ct).ConfigureAwait(false);
                    if (!holders.Exists)
                    {
                        notes.Add("workflow.preview.unknownRole");
                        break;
                    }

                    if (holders.Everyone)
                    {
                        notes.Add("workflow.preview.everyoneIgnored");
                    }

                    userIds.AddRange(holders.UserIds);
                    var units = new HashSet<Guid>();
                    foreach (var unit in holders.UnitIds)
                    {
                        units.UnionWith(await org.GetSelfAndDescendantIdsAsync(unit, ct).ConfigureAwait(false));
                    }

                    employeeIds.AddRange(await employees.GetEmployeeIdsInUnitsAsync(units, ct).ConfigureAwait(false));
                    break;
                }

            case ApproverRuleKinds.SectionManager or ApproverRuleKinds.DepartmentDirector:
                {
                    var level = rule.Kind == ApproverRuleKinds.SectionManager ? context.Settings.SectionLevelCode : context.Settings.DepartmentLevelCode;
                    var unit = await relations.UnitOfAsync(rule.Of, context, ct).ConfigureAwait(false);
                    var found = unit is { } u ? await relations.NearestAtLevelAsync(u, level, ct).ConfigureAwait(false) : null;
                    if (found is null)
                    {
                        notes.Add("workflow.preview.noUnitAtLevel");
                        break;
                    }

                    await AddHeadAsync(found.Id, employeeIds, notes, ct).ConfigureAwait(false);
                    break;
                }

            case ApproverRuleKinds.HeadOfUnit:
                {
                    var unit = rule.UnitId ?? await relations.UnitOfAsync(rule.Of, context, ct).ConfigureAwait(false);
                    if (unit is null)
                    {
                        notes.Add("workflow.preview.noUnit");
                        break;
                    }

                    await AddHeadAsync(unit.Value, employeeIds, notes, ct).ConfigureAwait(false);
                    break;
                }

            case ApproverRuleKinds.OwningUnit:
                {
                    var owning = await systems.GetOwningUnitAsync(rule.SystemCode ?? context.Process.SystemCode, ct).ConfigureAwait(false);
                    if (owning is null)
                    {
                        notes.Add("workflow.preview.noOwningUnit");
                        break;
                    }

                    await AddHeadAsync(owning.Value, employeeIds, notes, ct).ConfigureAwait(false);
                    break;
                }

            case ApproverRuleKinds.Employee:
                if (rule.EmployeeId is { } employee)
                {
                    employeeIds.Add(employee);
                }

                break;
            case ApproverRuleKinds.RecordRole:
                foreach (var holder in await recordRoles.GetHoldersAsync(context.Subject.ResourceType, context.Subject.ResourceId, ct).ConfigureAwait(false))
                {
                    if (holder.RoleCode != rule.RoleCode || holder.PrincipalKind == RecordPrincipalKinds.Unit)
                    {
                        continue;
                    }

                    (holder.PrincipalKind == RecordPrincipalKinds.Employee ? employeeIds : userIds).Add(holder.PrincipalId);
                }

                break;
            case ApproverRuleKinds.TeamRole:
                {
                    var resolver = teamRoles.FirstOrDefault();
                    if (resolver is null)
                    {
                        notes.Add("workflow.preview.teamRoleUnavailable");
                        break;
                    }

                    employeeIds.AddRange(await resolver.ResolveEmployeesAsync(rule.RoleCode ?? string.Empty, context.Subject, ct).ConfigureAwait(false));
                    break;
                }

            case ApproverRuleKinds.SubmitterManager:
                if (context.SubmitterEmployeeId is { } submitter && await employees.GetManagerAsync(submitter, cancellationToken: ct).ConfigureAwait(false) is { } manager)
                {
                    employeeIds.Add(manager);
                }
                else
                {
                    notes.Add("workflow.preview.noManager");
                }

                break;
            default:
                notes.Add("workflow.preview.unknownRule");
                break;
        }

        var approvers = await ToUsersAsync(employeeIds, userIds, notes, ct).ConfigureAwait(false);
        return new Resolution(approvers, notes.Distinct(StringComparer.Ordinal).ToList());
    }

    private async Task AddHeadAsync(Guid unitId, List<Guid> employeeIds, List<string> notes, CancellationToken ct)
    {
        if (await relations.HeadOfAsync(unitId, ct).ConfigureAwait(false) is { } head)
        {
            employeeIds.Add(head);
        }
        else
        {
            notes.Add("workflow.preview.noHead");
        }
    }

    /// <summary>Employees → their active user accounts; users → active only. Employees who left are dropped.</summary>
    public async Task<IReadOnlyList<ResolvedApprover>> ToUsersAsync(
        IReadOnlyCollection<Guid> employeeIds, IReadOnlyCollection<Guid> userIds, List<string> notes, CancellationToken ct)
    {
        var people = await employees.GetManyAsync(employeeIds.Distinct().ToList(), ct).ConfigureAwait(false);
        var activeEmployees = people.Values.Where(e => e.Status != "left").Select(e => e.Id).ToList();
        var byEmployee = activeEmployees.Count == 0 ? [] : await users.GetByEmployeesAsync(activeEmployees, ct).ConfigureAwait(false);
        var direct = userIds.Count == 0 ? new Dictionary<Guid, UserReference>() : await users.GetManyAsync(userIds.Distinct().ToList(), ct).ConfigureAwait(false);
        var all = byEmployee.Concat(direct.Values).Where(u => u.Active).DistinctBy(u => u.Id).ToList();
        if (activeEmployees.Any(e => all.All(u => u.EmployeeId != e)))
        {
            notes.Add("workflow.preview.noAccount");
        }

        var missing = all.Where(u => u.EmployeeId is { } e && !people.ContainsKey(e)).Select(u => u.EmployeeId!.Value).Distinct().ToList();
        var extra = missing.Count == 0 ? new Dictionary<Guid, EmployeeReference>() : await employees.GetManyAsync(missing, ct).ConfigureAwait(false);
        return all.Select(u =>
        {
            var employee = u.EmployeeId is { } id ? people.GetValueOrDefault(id) ?? extra.GetValueOrDefault(id) : null;
            return new ResolvedApprover(u.Id, u.EmployeeId, employee?.NameAr ?? u.UserName, employee?.NameEn ?? u.UserName);
        }).OrderBy(a => a.NameEn, StringComparer.Ordinal).ToList();
    }
}
