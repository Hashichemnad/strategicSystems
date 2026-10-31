using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Access.Api;

internal static class AccessEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/me", MeAsync).WithName("GetMyAccess").RequireSignedIn();

        var systems = group.MapGroup("/systems");
        systems.MapGet("/", (AccessAdministration a, CancellationToken ct) => a.ListSystemsAsync(ct)).WithName("ListSystems")
            .RequirePermission(AccessPermissions.SystemRead);
        systems.MapPut("/{code}", async (string code, SystemInput body, AccessAdministration a, CancellationToken ct) =>
                (await a.UpdateSystemAsync(code, body.Enabled, body.OwningUnitId, body.Version, ct)).ToOk())
            .WithName("UpdateSystem").RequirePermission(AccessPermissions.SystemManage);
        systems.MapGet("/{code}/matrix", async (string code, AccessAdministration a, CancellationToken ct) => (await a.MatrixAsync(code, ct)).ToOk())
            .WithName("GetPermissionMatrix").RequirePermission(AccessPermissions.RoleRead);

        var roles = group.MapGroup("/roles");
        roles.MapGet("/", (string? system, AccessAdministration a, CancellationToken ct) => a.ListRolesAsync(system, ct)).WithName("ListRoles")
            .RequirePermission(AccessPermissions.RoleRead);
        roles.MapPost("/", async (RoleInput body, AccessAdministration a, CancellationToken ct) => (await a.CreateRoleAsync(body, ct)).ToOk())
            .WithName("CreateRole").RequirePermission(AccessPermissions.RoleManage);
        roles.MapPut("/{id:guid}", async (Guid id, UpdateRoleInput body, AccessAdministration a, CancellationToken ct) =>
                (await a.UpdateRoleAsync(id, new RoleInput(null, null, body.NameAr, body.NameEn, body.DescriptionAr, body.DescriptionEn, body.RequiresMfa, body.Active), body.Version, ct)).ToOk())
            .WithName("UpdateRole").RequirePermission(AccessPermissions.RoleManage);
        roles.MapPut("/{id:guid}/permissions", async (Guid id, RolePermissionsInput body, AccessAdministration a, CancellationToken ct) =>
                (await a.SetRolePermissionsAsync(id, body.Permissions ?? [], body.Version, ct)).ToOk())
            .WithName("SetRolePermissions").RequirePermission(AccessPermissions.RoleManage);

        group.MapPut("/record-roles/{id:guid}/permissions", async (Guid id, RecordRolePermissionsInput body, AccessAdministration a, CancellationToken ct) =>
                (await a.SetRecordRolePermissionsAsync(id, body.Permissions ?? [], body.Version, ct)).ToOk())
            .WithName("SetRecordRolePermissions").RequirePermission(AccessPermissions.RoleManage);

        var assignments = group.MapGroup("/assignments");
        assignments.MapGet("/", (Guid? roleId, Guid? userId, string? system, bool? includeEnded, AccessAdministration a, CancellationToken ct) =>
                a.ListAssignmentsAsync(roleId, userId, system, includeEnded ?? false, ct))
            .WithName("ListRoleAssignments").RequirePermission(AccessPermissions.AssignmentRead);
        assignments.MapPost("/", async (AssignmentInput body, AccessAdministration a, CancellationToken ct) => (await a.CreateAssignmentAsync(body, ct)).ToOk())
            .WithName("CreateRoleAssignment").RequirePermission(AccessPermissions.AssignmentManage);
        assignments.MapPost("/{id:guid}/end", async (Guid id, EndAssignmentInput body, AccessAdministration a, CancellationToken ct) =>
                (await a.EndAssignmentAsync(id, body.LastDay, body.Version, ct)).ToOk())
            .WithName("EndRoleAssignment").RequirePermission(AccessPermissions.AssignmentManage);

        var systemAccess = group.MapGroup("/system-access");
        systemAccess.MapGet("/", (string? system, AccessAdministration a, CancellationToken ct) => a.ListSystemAccessAsync(system, ct))
            .WithName("ListSystemAccess").RequirePermission(AccessPermissions.AssignmentRead);
        systemAccess.MapPost("/", async (SystemAccessInput body, AccessAdministration a, CancellationToken ct) =>
                (await a.GrantSystemAccessAsync(body.SystemCode, body.PrincipalKind, body.PrincipalId, ct)).ToOk())
            .WithName("GrantSystemAccess").RequirePermission(AccessPermissions.AssignmentManage);
        systemAccess.MapPost("/{id:guid}/revoke", async Task<Results<NoContent, ProblemHttpResult>> (Guid id, AccessAdministration a, CancellationToken ct) =>
            {
                var result = await a.RevokeSystemAccessAsync(id, ct);
                return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();
            })
            .WithName("RevokeSystemAccess").RequirePermission(AccessPermissions.AssignmentManage);

        var transfers = group.MapGroup("/transfers");
        transfers.MapGet("/", (AccessReporting r, CancellationToken ct) => r.ListTransfersAsync(ct)).WithName("ListRoleTransfers")
            .RequirePermission(AccessPermissions.AssignmentRead);
        transfers.MapPost("/", async (TransferInput body, AccessReporting r, CancellationToken ct) => (await r.TransferAsync(body, ct)).ToOk())
            .WithName("TransferRoles").RequirePermission(AccessPermissions.AssignmentManage);

        group.MapGet("/users/{id:guid}/effective", async (Guid id, AccessReporting r, CancellationToken ct) => (await r.EffectiveAsync(id, ct)).ToOk())
            .WithName("GetEffectiveAccess").RequirePermission(AccessPermissions.AssignmentRead);

        group.MapGet("/review", (string? system, Guid? unitId, Guid? userId, AccessReporting r, CancellationToken ct) => r.ReviewAsync(system, unitId, userId, ct))
            .WithName("GetAccessReview").RequirePermission(AccessPermissions.ReviewExport);
        group.MapGet("/review/export", ExportAsync).WithName("ExportAccessReview").RequirePermission(AccessPermissions.ReviewExport)
            .Produces(StatusCodes.Status200OK, contentType: XlsxContentType);
    }

    private static async Task<Results<Ok<AccessMeDto>, ProblemHttpResult>> MeAsync(IRequestContext context, AccessService access, CancellationToken ct)
    {
        if (!context.User.IsInternal() || context.User.UserId() is not { } userId)
        {
            return Error.Forbidden().ToProblem();
        }

        var (model, snapshot) = await access.SnapshotAsync(userId, context.User.EmployeeIdOf(), ct);
        var permissions = snapshot.Permissions.Keys.Where(p => AccessService.SystemOpen(model, snapshot, p, out _)).ToHashSet(StringComparer.Ordinal);
        foreach (var permission in model.RecordRoleCodes.Keys.Where(p => !permissions.Contains(p) && AccessService.SystemOpen(model, snapshot, p, out _)))
        {
            if (await access.CanAsync(context.User, permission, cancellationToken: ct))
            {
                permissions.Add(permission);
            }
        }

        return TypedResults.Ok(new AccessMeDto(
            snapshot.Systems.Order(StringComparer.Ordinal).ToList(),
            permissions.Order(StringComparer.Ordinal).ToList(),
            snapshot.Roles.Select(r => r.Key).Order(StringComparer.Ordinal).ToList()));
    }

    private static async Task<FileContentHttpResult> ExportAsync(string? system, Guid? unitId, string? lang, AccessReporting reporting, CancellationToken ct)
    {
        var language = lang == "en" ? "en" : "ar";
        var bytes = await reporting.ExportReviewAsync(system, unitId, language, ct);
        return TypedResults.File(bytes, XlsxContentType, $"access-review-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }
}

internal sealed record SystemInput(bool Enabled, Guid? OwningUnitId, uint Version);

internal sealed record UpdateRoleInput(string? NameAr, string? NameEn, string? DescriptionAr, string? DescriptionEn, bool RequiresMfa, bool? Active, uint Version);

internal sealed record RolePermissionsInput(IReadOnlyList<RolePermissionDto>? Permissions, uint Version);

internal sealed record RecordRolePermissionsInput(IReadOnlyList<string>? Permissions, uint Version);

internal sealed record EndAssignmentInput(DateOnly? LastDay, uint Version);

internal sealed record SystemAccessInput(string? SystemCode, string? PrincipalKind, Guid? PrincipalId);
