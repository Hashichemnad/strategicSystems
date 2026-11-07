using System.Net;
using System.Net.Http.Json;
using Strategy.IntegrationTests.Workflow;
using Strategy.Platform.Access.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Paging;
using Strategy.Platform.Notifications.Application;

namespace Strategy.IntegrationTests.Access;

/// <summary>Calls of System → Settings → Responsibilities (ADR 0168) and the checks every module test repeats.</summary>
internal static class Responsibilities
{
    public const string AssignedTemplate = "access.responsibilityAssigned";
    public const string RemovedTemplate = "access.responsibilityRemoved";

    public static string Base(string system) => $"/api/v1/access/systems/{system}/responsibilities";

    public static async Task<ResponsibilityListDto> ListAsync(HttpClient client, string system, string kind, string query = "")
    {
        var response = await client.GetAsync($"{Base(system)}/{kind}?pageSize=200{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ResponsibilityListDto>())!;
    }

    public static object Body(IEnumerable<ResponsibilityRowDto> rows, Guid newId, string? reason = "Reorganisation") =>
        new { items = rows.Select(r => new { id = r.Id, version = r.Version }).ToList(), newId, reason };

    public static async Task<List<ResponsibilityOutcomeDto>> PreviewAsync(HttpClient client, string system, string kind, object body)
    {
        var response = await client.PostAsJsonAsync($"{Base(system)}/{kind}/preview", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<List<ResponsibilityOutcomeDto>>())!;
    }

    public static async Task<ResponsibilityResultDto> ReassignAsync(HttpClient client, string system, string kind, object body)
    {
        var response = await client.PostAsJsonAsync($"{Base(system)}/{kind}/reassign", body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ResponsibilityResultDto>())!;
    }

    /// <summary>Field changes of the record carry the reason (ambient audit reason) and one summary event exists for the system.</summary>
    public static async Task ShouldBeAuditedAsync(HttpClient admin, string system, string entityType, Guid recordId, string reason)
    {
        var changes = (await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/v1/audit/entries?entityType={entityType}&recordId={recordId}&kind=change&pageSize=200"))!;
        changes.Items.ShouldContain(e => e.Reason == reason, $"a change of {entityType} {recordId} should carry the reason");
        var events = (await admin.GetFromJsonAsync<PagedResult<AuditEntryDto>>(
            $"/api/v1/audit/entries?action=access.responsibilities.reassign&system={system}&pageSize=50"))!;
        events.Items.ShouldContain(e => e.Reason == reason);
    }

    /// <summary>Exactly one in-app notice of <paramref name="template"/> reaches the user (bilingual: the user's language picks the title).</summary>
    public static async Task<NotificationDto> ShouldBeNotifiedOnceAsync(HttpClient client, string template)
    {
        var first = await ApprovalFlowTests.WaitForNotificationAsync(client, template);
        await Task.Delay(500);
        var page = (await client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications/mine?pageSize=100"))!;
        page.Items.Count(n => n.TemplateKey == template).ShouldBe(1);
        first.TitleAr.ShouldNotBeNullOrWhiteSpace();
        first.TitleEn.ShouldNotBeNullOrWhiteSpace();
        first.TitleAr.ShouldNotBe(first.TitleEn);
        return first;
    }

    public static async Task ShouldNotBeNotifiedAsync(HttpClient client, string template)
    {
        var page = (await client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications/mine?pageSize=100"))!;
        page.Items.ShouldNotContain(n => n.TemplateKey == template);
    }
}
