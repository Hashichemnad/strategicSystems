using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Strategy.Platform.Access;
using Strategy.Platform.Http.Access;
using Strategy.Platform.Http.Problems;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Organisation.Application;
using Strategy.Platform.Organisation.Domain;
using Strategy.Platform.Organisation.Import;
using Strategy.Platform.Organisation.Infrastructure;

namespace Strategy.Platform.Organisation.Api;

internal static class OrgEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxImportBytes = 10 * 1024 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        var levels = group.MapGroup("/levels");
        levels.MapGet("/", ListLevelsAsync).WithName("ListOrgLevels").RequirePermission(OrgPermissions.UnitRead);
        levels.MapPost("/", CreateLevelAsync).WithName("CreateOrgLevel").RequirePermission(OrgPermissions.UnitManage);
        levels.MapPut("/{id:guid}", UpdateLevelAsync).WithName("UpdateOrgLevel").RequirePermission(OrgPermissions.UnitManage);

        group.MapGet("/settings", GetSettingsAsync).WithName("GetOrgSettings").RequirePermission(OrgPermissions.UnitRead);
        group.MapPut("/settings", UpdateSettingsAsync).WithName("UpdateOrgSettings").RequirePermission(OrgPermissions.UnitManage);

        var units = group.MapGroup("/units");
        units.MapGet("/tree", TreeAsync).WithName("GetOrgUnitTree").RequirePermission(OrgPermissions.UnitRead);
        units.MapGet("/{id:guid}", GetUnitAsync).WithName("GetOrgUnit").RequirePermission(OrgPermissions.UnitRead);
        units.MapGet("/{id:guid}/history", UnitHistoryAsync).WithName("GetOrgUnitHistory").RequirePermission(OrgPermissions.UnitRead);
        units.MapGet("/{id:guid}/path", UnitPathAsync).WithName("GetOrgUnitPath").RequirePermission(OrgPermissions.UnitRead);
        units.MapPost("/", CreateUnitAsync).WithName("CreateOrgUnit").RequirePermission(OrgPermissions.UnitManage);
        units.MapPut("/{id:guid}", UpdateUnitAsync).WithName("UpdateOrgUnit").RequirePermission(OrgPermissions.UnitManage);
        units.MapPost("/{id:guid}/move", MoveUnitAsync).WithName("MoveOrgUnit").RequirePermission(OrgPermissions.UnitManage);
        units.MapPost("/{id:guid}/deactivate", DeactivateUnitAsync).WithName("DeactivateOrgUnit").RequirePermission(OrgPermissions.UnitManage);
        units.MapPost("/{id:guid}/activate", ActivateUnitAsync).WithName("ActivateOrgUnit").RequirePermission(OrgPermissions.UnitManage);

        var employees = group.MapGroup("/employees");
        employees.MapGet("/", ListEmployeesAsync).WithName("ListEmployees").RequirePermission(OrgPermissions.EmployeeRead);
        employees.MapGet("/{id:guid}", GetEmployeeAsync).WithName("GetEmployee").RequirePermission(OrgPermissions.EmployeeRead);
        employees.MapGet("/{id:guid}/history", EmployeeHistoryAsync).WithName("GetEmployeeHistory").RequirePermission(OrgPermissions.EmployeeRead);
        employees.MapPost("/", CreateEmployeeAsync).WithName("CreateEmployee").RequirePermission(OrgPermissions.EmployeeManage);
        employees.MapPut("/{id:guid}", UpdateEmployeeAsync).WithName("UpdateEmployee").RequirePermission(OrgPermissions.EmployeeManage);
        employees.MapPost("/{id:guid}/change-status", ChangeStatusAsync).WithName("ChangeEmployeeStatus").RequirePermission(OrgPermissions.EmployeeManage);
        employees.MapGet("/{id:guid}/photo", GetPhotoAsync).WithName("GetEmployeePhoto").RequirePermission(OrgPermissions.EmployeeRead)
            .Produces(StatusCodes.Status200OK, contentType: "image/png");
        employees.MapPut("/{id:guid}/photo", PutPhotoAsync).WithName("PutEmployeePhoto").RequirePermission(OrgPermissions.EmployeeManage)
            .DisableAntiforgery(); // validated by the platform antiforgery middleware for every /api write

        var delegations = group.MapGroup("/delegations");
        delegations.MapGet("/mine", MyDelegationsAsync).WithName("ListMyDelegations").RequireSignedIn();
        delegations.MapGet("/", ListDelegationsAsync).WithName("ListDelegations").RequirePermission(OrgPermissions.DelegationManage);
        delegations.MapPost("/", CreateDelegationAsync).WithName("CreateDelegation").RequireSignedIn();
        delegations.MapPost("/{id:guid}/cancel", CancelDelegationAsync).WithName("CancelDelegation").RequireSignedIn();

        var import = group.MapGroup("/import");
        import.MapGet("/templates/{kind}", TemplateAsync).WithName("GetOrgImportTemplate").RequirePermission(OrgPermissions.ImportManage)
            .Produces(StatusCodes.Status200OK, contentType: XlsxContentType);
        import.MapPost("/{kind}", ImportAsync).WithName("RunOrgImport").RequirePermission(OrgPermissions.ImportManage).DisableAntiforgery();
        import.MapPost("/{kind}/report", ImportReportAsync).WithName("GetOrgImportReport").RequirePermission(OrgPermissions.ImportManage)
            .DisableAntiforgery().Produces(StatusCodes.Status200OK, contentType: XlsxContentType);
    }

    // ---- Levels and settings ----

    private static async Task<Results<Ok<IReadOnlyList<OrgLevelDto>>, ProblemHttpResult>> ListLevelsAsync(IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ListLevels(), ct)).ToOk();

    private static async Task<Results<Ok<OrgLevelDto>, ProblemHttpResult>> CreateLevelAsync(LevelInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CreateLevel(body.Code, body.NameAr, body.NameEn, body.Sort), ct)).ToOk();

    private static async Task<Results<Ok<OrgLevelDto>, ProblemHttpResult>> UpdateLevelAsync(
        Guid id, UpdateLevelInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new UpdateLevel(id, body.NameAr, body.NameEn, body.Sort, body.Active, body.Version), ct)).ToOk();

    private static async Task<Results<Ok<OrgSettingsDto>, ProblemHttpResult>> GetSettingsAsync(IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetOrgSettings(), ct)).ToOk();

    private static async Task<Results<Ok<OrgSettingsDto>, ProblemHttpResult>> UpdateSettingsAsync(
        OrgSettingsInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new UpdateOrgSettings(body.EnforceLevelOrder, body.Version), ct)).ToOk();

    // ---- Units ----

    private static async Task<Results<Ok<IReadOnlyList<OrgTreeNodeDto>>, ProblemHttpResult>> TreeAsync(
        DateOnly? asOf, bool? includeInactive, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetUnitTree(asOf, includeInactive ?? false), ct)).ToOk();

    private static async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> GetUnitAsync(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetUnit(id), ct)).ToOk();

    private static async Task<Results<Ok<IReadOnlyList<OrgUnitVersionDto>>, ProblemHttpResult>> UnitHistoryAsync(
        Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetUnitHistory(id), ct)).ToOk();

    private static async Task<Results<Ok<IReadOnlyList<OrgUnitPathItemDto>>, ProblemHttpResult>> UnitPathAsync(
        Guid id, DateOnly? asOf, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetUnitPath(id, asOf), ct)).ToOk();

    private static async Task<Results<Created<OrgUnitDto>, ProblemHttpResult>> CreateUnitAsync(
        UnitInput body, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(
            new CreateUnit(body.Code, body.NameAr, body.NameEn, body.LevelCode, body.ParentId, body.HeadEmployeeId, body.DeputyEmployeeId,
                body.CostCentre, body.EffectiveFrom), ct);
        return result.IsSuccess ? TypedResults.Created($"/api/v1/org/units/{result.Value.Id}", result.Value) : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> UpdateUnitAsync(
        Guid id, UpdateUnitInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(
            new UpdateUnit(id, body.Code, body.NameAr, body.NameEn, body.LevelCode, body.HeadEmployeeId, body.DeputyEmployeeId, body.CostCentre,
                body.EffectiveDate, body.Version), ct)).ToOk();

    private static async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> MoveUnitAsync(
        Guid id, MoveUnitInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new MoveUnit(id, body.NewParentId, body.EffectiveDate, body.Version), ct)).ToOk();

    private static async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> DeactivateUnitAsync(
        Guid id, EffectiveChangeInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new DeactivateUnit(id, body.EffectiveDate, body.Version), ct)).ToOk();

    private static async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> ActivateUnitAsync(
        Guid id, EffectiveChangeInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ActivateUnit(id, body.EffectiveDate, body.Version), ct)).ToOk();

    // ---- Employees ----

    private static async Task<Results<Ok<PagedResult<EmployeeListItemDto>>, ProblemHttpResult>> ListEmployeesAsync(
        int? page, int? pageSize, string? sort, string? search, Guid? unitId, bool? includeDescendants, string? status,
        IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(
            new ListEmployees(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort), search, unitId, includeDescendants ?? false, status),
            ct)).ToOk();

    private static async Task<Results<Ok<EmployeeDto>, ProblemHttpResult>> GetEmployeeAsync(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetEmployee(id), ct)).ToOk();

    private static async Task<Results<Ok<IReadOnlyList<EmployeeAssignmentDto>>, ProblemHttpResult>> EmployeeHistoryAsync(
        Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new GetEmployeeHistory(id), ct)).ToOk();

    private static async Task<Results<Created<EmployeeDto>, ProblemHttpResult>> CreateEmployeeAsync(
        EmployeeInput body, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(
            new CreateEmployee(body.EmployeeNo, body.NameAr, body.NameEn, body.Email, body.Mobile, body.JobTitleAr, body.JobTitleEn, body.Grade,
                body.UnitId, body.ManagerEmployeeId, body.JoinDate), ct);
        return result.IsSuccess ? TypedResults.Created($"/api/v1/org/employees/{result.Value.Id}", result.Value) : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<EmployeeDto>, ProblemHttpResult>> UpdateEmployeeAsync(
        Guid id, UpdateEmployeeInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(
            new UpdateEmployee(id, body.EmployeeNo, body.NameAr, body.NameEn, body.Email, body.Mobile, body.JobTitleAr, body.JobTitleEn, body.Grade,
                body.UnitId, body.ManagerEmployeeId, body.JoinDate, body.EffectiveDate, body.Version), ct)).ToOk();

    private static async Task<Results<Ok<EmployeeDto>, ProblemHttpResult>> ChangeStatusAsync(
        Guid id, ChangeStatusInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ChangeEmployeeStatus(id, body.Status, body.Version), ct)).ToOk();

    private static async Task<IResult> GetPhotoAsync(
        Guid id, OrgDbContext db, IAccessService access, IRequestContext context, CancellationToken ct)
    {
        var visible = await (await access.ApplyScopeAsync(db.Employees.AsNoTracking(), context.User, OrgPermissions.EmployeeRead, OrgScopes.Employees, ct))
            .AnyAsync(e => e.Id == id, ct);
        var photo = visible ? await db.Photos.AsNoTracking().FirstOrDefaultAsync(p => p.EmployeeId == id, ct) : null;
        if (photo is null)
        {
            return Error.NotFound().ToProblem();
        }

        return TypedResults.File(photo.Content, photo.ContentType, lastModified: photo.UpdatedAt);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PutPhotoAsync(
        Guid id, IFormFile file, OrgDbContext db, IAccessService access, IRequestContext context, IClock clock, CancellationToken ct)
    {
        var exists = await (await access.ApplyScopeAsync(db.Employees.AsNoTracking(), context.User, OrgPermissions.EmployeeManage, OrgScopes.Employees, ct))
            .AnyAsync(e => e.Id == id, ct);
        if (!exists)
        {
            return Error.NotFound().ToProblem();
        }

        if (file.Length > EmployeePhoto.MaxBytes)
        {
            return OrgErrors.PhotoTooLarge.ToProblem();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var created = EmployeePhoto.Create(id, buffer.ToArray(), clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error.ToProblem();
        }

        var existing = await db.Photos.FirstOrDefaultAsync(p => p.EmployeeId == id, ct);
        if (existing is null)
        {
            db.Photos.Add(created.Value);
        }
        else
        {
            existing.Replace(created.Value);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---- Delegations ----

    private static async Task<Results<Ok<IReadOnlyList<DelegationDto>>, ProblemHttpResult>> MyDelegationsAsync(IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ListMyDelegations(), ct)).ToOk();

    private static async Task<Results<Ok<IReadOnlyList<DelegationDto>>, ProblemHttpResult>> ListDelegationsAsync(
        Guid? employeeId, string? status, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new ListDelegations(employeeId, status), ct)).ToOk();

    private static async Task<Results<Ok<DelegationDto>, ProblemHttpResult>> CreateDelegationAsync(
        DelegationInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(
            new CreateDelegation(body.DelegatorEmployeeId, body.DelegateEmployeeId, body.FromDate, body.ToDate, body.Scope, body.Reason), ct)).ToOk();

    private static async Task<Results<Ok<DelegationDto>, ProblemHttpResult>> CancelDelegationAsync(
        Guid id, CancelInput body, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.SendAsync(new CancelDelegation(id, body.Reason, body.Version), ct)).ToOk();

    // ---- Import ----

    private static IResult TemplateAsync(string kind) =>
        ImportKinds.All.Contains(kind)
            ? TypedResults.File(OrgImporter.Template(kind), XlsxContentType, $"{kind}-template.xlsx")
            : Error.NotFound().ToProblem();

    private static async Task<Results<Ok<ImportReportDto>, ProblemHttpResult>> ImportAsync(
        string kind, string? mode, IFormFile file, OrgImporter importer, IRequestContext context, CancellationToken ct)
    {
        var rows = ReadRows(kind, file);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        if (context is RequestContext mutable)
        {
            mutable.SetChannel(Channels.Import);
        }

        return TypedResults.Ok(await importer.RunAsync(kind, rows.Value, commit: mode == "commit", ct));
    }

    private static async Task<IResult> ImportReportAsync(string kind, IFormFile file, OrgImporter importer, CancellationToken ct)
    {
        var rows = ReadRows(kind, file);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        var report = await importer.RunAsync(kind, rows.Value, commit: false, ct);
        var errors = report.Issues.ToLookup(i => i.Row, i => i.Column is null ? i.Code : $"{i.Column}: {i.Code}");
        return TypedResults.File(
            ImportTable.Report(kind, OrgImporter.ColumnsOf(kind), rows.Value, errors), XlsxContentType, $"{kind}-validation-report.xlsx");
    }

    private static Result<IReadOnlyList<ImportRow>> ReadRows(string kind, IFormFile file)
    {
        if (!ImportKinds.All.Contains(kind))
        {
            return Error.NotFound();
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".csv"))
        {
            return Error.Rule("import.fileType", "import.errors.fileType");
        }

        if (file.Length is 0 or > MaxImportBytes)
        {
            return Error.Rule("import.fileSize", "import.errors.fileSize",
                new Dictionary<string, object?> { ["maxMb"] = MaxImportBytes / 1024 / 1024 });
        }

        try
        {
            using var stream = file.OpenReadStream();
            return Result.Success(ImportTable.Read(stream, file.FileName, OrgImporter.ColumnsOf(kind)));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or FormatException or ArgumentException
            or InvalidOperationException)
        {
            return Error.Rule("import.unreadable", "import.errors.unreadable");
        }
    }
}

internal sealed record LevelInput(string? Code, string? NameAr, string? NameEn, int Sort);

internal sealed record UpdateLevelInput(string? NameAr, string? NameEn, int Sort, bool Active, uint Version);

internal sealed record OrgSettingsInput(bool EnforceLevelOrder, uint Version);

internal sealed record UnitInput(
    string? Code, string? NameAr, string? NameEn, string? LevelCode, Guid? ParentId, Guid? HeadEmployeeId, Guid? DeputyEmployeeId,
    string? CostCentre, DateOnly? EffectiveFrom);

internal sealed record UpdateUnitInput(
    string? Code, string? NameAr, string? NameEn, string? LevelCode, Guid? HeadEmployeeId, Guid? DeputyEmployeeId, string? CostCentre,
    DateOnly? EffectiveDate, uint Version);

internal sealed record MoveUnitInput(Guid? NewParentId, DateOnly? EffectiveDate, uint Version);

internal sealed record EffectiveChangeInput(DateOnly? EffectiveDate, uint Version);

internal sealed record EmployeeInput(
    string? EmployeeNo, string? NameAr, string? NameEn, string? Email, string? Mobile, string? JobTitleAr, string? JobTitleEn, string? Grade,
    Guid? UnitId, Guid? ManagerEmployeeId, DateOnly? JoinDate);

internal sealed record UpdateEmployeeInput(
    string? EmployeeNo, string? NameAr, string? NameEn, string? Email, string? Mobile, string? JobTitleAr, string? JobTitleEn, string? Grade,
    Guid? UnitId, Guid? ManagerEmployeeId, DateOnly? JoinDate, DateOnly? EffectiveDate, uint Version);

internal sealed record ChangeStatusInput(string? Status, uint Version);

internal sealed record DelegationInput(
    Guid? DelegatorEmployeeId, Guid? DelegateEmployeeId, DateOnly? FromDate, DateOnly? ToDate, IReadOnlyList<string>? Scope, string? Reason);

internal sealed record CancelInput(string? Reason, uint Version);
