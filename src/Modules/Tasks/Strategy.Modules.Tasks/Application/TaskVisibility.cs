using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Tasks.Domain;
using Strategy.Modules.Tasks.Infrastructure;
using Strategy.Platform.Access;
using Strategy.Platform.Links;

namespace Strategy.Modules.Tasks.Application;

/// <summary>
/// The one rule for reading tasks (CLAUDE.md §7, ADR 0039): data scope / record roles of <c>tasks.task.read</c> (or the linked record's
/// follow-linked permission) <b>and</b> "not confidential, or the user is a participant", always in the database query.
/// </summary>
internal sealed class TaskVisibility(TasksDbContext db, IAccessService access, ILinkRegistry links)
{
    /// <summary>Every task the user may read.</summary>
    public async Task<IQueryable<TaskItem>> QueryAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var scoped = await access.ApplyScopeAsync(db.Tasks.AsNoTracking(), user, TaskPermissions.Read, TaskScope.Map, ct).ConfigureAwait(false);
        return Confidentiality(scoped, user);
    }

    /// <summary>
    /// Tasks linked to one record: everything the user may read anyway, or — when the user may follow the record's linked items (e.g. the
    /// PM of the project) — every linked task. Confidential tasks stay limited to their participants either way.
    /// </summary>
    public async Task<IQueryable<TaskItem>> LinkedQueryAsync(ClaimsPrincipal user, string type, Guid id, CancellationToken ct)
    {
        IQueryable<TaskItem> query = await links.CanFollowLinkedAsync(user, type, id, ct).ConfigureAwait(false)
            ? db.Tasks.AsNoTracking()
            : await access.ApplyScopeAsync(db.Tasks.AsNoTracking(), user, TaskPermissions.Read, TaskScope.Map, ct).ConfigureAwait(false);
        return Confidentiality(query, user).Where(t => t.LinkedType == type && t.LinkedId == id);
    }

    /// <summary>Can the user read this task? (404 otherwise.)</summary>
    public async Task<bool> CanReadAsync(TaskItem task, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.IsSystem())
        {
            return true;
        }

        if (!user.IsInternal())
        {
            return false;
        }

        if (task.Confidential && !await IsParticipantAsync(task, user, ct).ConfigureAwait(false))
        {
            return false;
        }

        if (await access.CanAsync(user, TaskPermissions.Read, TaskScope.Map.RefOf(task), ct).ConfigureAwait(false))
        {
            return true;
        }

        return task.LinkedType is { } type && task.LinkedId is { } id && await links.CanFollowLinkedAsync(user, type, id, ct).ConfigureAwait(false);
    }

    /// <summary>The task when the user may read it, else null. <paramref name="tracked"/> loads it with its timesheet for changes.</summary>
    public async Task<TaskItem?> FindAsync(Guid id, ClaimsPrincipal user, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.Tasks.Include(t => t.Periods) : db.Tasks.AsNoTracking().Include(t => t.Periods);
        var task = await query.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        return task is not null && await CanReadAsync(task, user, ct).ConfigureAwait(false) ? task : null;
    }

    /// <summary>Holds <paramref name="permission"/> on the task (system role in scope or record role).</summary>
    public Task<bool> HasAsync(TaskItem task, ClaimsPrincipal user, string permission, CancellationToken ct) =>
        access.CanAsync(user, permission, TaskScope.Map.RefOf(task), ct);

    public async Task<bool> IsParticipantAsync(TaskItem task, ClaimsPrincipal user, CancellationToken ct)
    {
        var employeeId = user.EmployeeIdOf();
        var userId = user.UserId();
        if (task.IsCreator(employeeId, userId) || task.IsAssignee(employeeId) || (employeeId is { } e && task.EnteredByEmployeeId == e))
        {
            return true;
        }

        return employeeId is { } employee
            && await db.FollowUps.AnyAsync(f => f.TaskId == task.Id && f.Active && f.EmployeeId == employee, ct).ConfigureAwait(false);
    }

    private IQueryable<TaskItem> Confidentiality(IQueryable<TaskItem> query, ClaimsPrincipal user)
    {
        if (user.IsSystem())
        {
            return query;
        }

        var employee = user.EmployeeIdOf() ?? Guid.Empty;
        var userId = user.UserId() ?? Guid.Empty;
        var followUps = db.FollowUps.AsNoTracking();
        return query.Where(t => !t.Confidential
            || t.AssigneeEmployeeId == employee
            || t.CreatorEmployeeId == employee
            || t.CreatorUserId == userId
            || t.EnteredByEmployeeId == employee
            || followUps.Any(f => f.TaskId == t.Id && f.Active && f.EmployeeId == employee));
    }
}
