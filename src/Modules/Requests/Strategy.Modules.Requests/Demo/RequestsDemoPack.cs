using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Strategy.Modules.Requests.Application;
using Strategy.Modules.Requests.Contracts;
using Strategy.Platform.Files;
using Strategy.Platform.Kernel.Demo;

namespace Strategy.Modules.Requests.Demo;

/// <summary>
/// Demo pack (ADR 0153) for the systems built on the Request Framework: a Document Registry submission (with a draft file), an IP registration
/// request and a benchmarking plan request — each submitted by a member of staff and left in review, so the office queues and My Work show them.
/// A second document request stays a draft. Removal: every open demo request is cancelled by its requester with a reason.
/// </summary>
internal sealed class RequestsDemoPack : IDemoPackContributor
{
    private const string RequestType = "requests.request";
    private const string Reason = "Demo data removed / أزيلت بيانات العرض";

    public string Code => "requests";

    public int Order => 60;

    public async Task LoadAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        // Document Registry: a new policy with its draft text (the office classifies and publishes it).
        await context.ActAsAsync("analyst").ConfigureAwait(false);
        var document = await CreateAsync(context, "docreg.document", new { nameAr = "سياسة العمل عن بعد (عرض)", nameEn = "Remote work policy (demo)" }, cancellationToken)
            .ConfigureAwait(false);
        var fileId = await UploadAsync(context, document.Id, "remote-work-policy-draft.txt", "Demo draft: remote work policy for all departments.", cancellationToken)
            .ConfigureAwait(false);
        document = await SaveAsync(context, document, new
        {
            isNewVersion = false,
            nameAr = "سياسة العمل عن بعد (عرض)",
            nameEn = "Remote work policy (demo)",
            ownerUnit = context.Units["strategy"],
            description = "Demo: rules for remote work",
            draftFile = fileId,
        }, cancellationToken).ConfigureAwait(false);
        await SubmitAsync(context, document, cancellationToken).ConfigureAwait(false);

        await CreateAsync(context, "docreg.document", new { nameAr = "دليل إجراءات المشتريات (مسودة عرض)", nameEn = "Procurement procedure manual (demo draft)" },
            cancellationToken).ConfigureAwait(false);

        // Intellectual Property: a methodology developed by the PMO.
        await context.ActAsAsync("pm").ConfigureAwait(false);
        var ip = await CreateAsync(context, "ip.registration", new
        {
            nameAr = "منهجية تقييم المبادرات الاستراتيجية (عرض)",
            nameEn = "Strategic initiative assessment methodology (demo)",
            description = "Demo: a methodology to score and prioritise strategic initiatives.",
            contributors = new object[] { new { employee = context.Person("pm").EmployeeId, role = "developer", share = 100 } },
            declaration = true,
        }, cancellationToken).ConfigureAwait(false);
        await SubmitAsync(context, ip, cancellationToken).ConfigureAwait(false);

        // Benchmarking: a study request for next year's plan.
        await context.ActAsAsync("analyst").ConfigureAwait(false);
        var bench = await CreateAsync(context, "bench.request", new
        {
            titleAr = "مقارنة معيارية لقنوات الخدمة الرقمية (عرض)",
            titleEn = "Benchmarking of digital service channels (demo)",
            responsibleUnit = context.Units["strategy"],
            responsibleEmployee = context.Person("analyst").EmployeeId,
            targetYear = context.Today.Year + 1,
            targetQuarter = 1,
            comparisonType = "functional",
            purpose = "process-improvement",
            scope = "Demo: customer service channels and turnaround times",
            topics = new object[] { new { topic = "Service channels", owner = context.Person("analyst").EmployeeId, description = "Channels and their uptake" } },
        }, cancellationToken).ConfigureAwait(false);
        await SubmitAsync(context, bench, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(DemoPackContext context, CancellationToken cancellationToken)
    {
        foreach (var record in await context.TrackedAsync(RequestType, cancellationToken).ConfigureAwait(false))
        {
            await context.ActAsAsync(record).ConfigureAwait(false);
            var id = record.ResourceId;
            var current = await context.RunAsync(s => s.GetRequiredService<RequestQueries>().GetAsync(id, cancellationToken)).ConfigureAwait(false);
            if (current.IsFailure || current.Value.Status is RequestStatuses.Completed or RequestStatuses.Rejected or RequestStatuses.Cancelled)
            {
                continue;
            }

            var version = current.Value.Version;
            (await context.RunAsync(s => s.GetRequiredService<RequestCommands>().CancelAsync(id, new RequestCancelInput(Reason, version), cancellationToken))
                .ConfigureAwait(false)).Require("cancel request");
        }
    }

    private static async Task<RequestRecordDto> CreateAsync(DemoPackContext context, string type, object data, CancellationToken ct)
    {
        var json = ToJson(data);
        var created = (await context.RunAsync(s => s.GetRequiredService<RequestCommands>().CreateAsync(new DraftInput(type, json, "demo"), ct)).ConfigureAwait(false))
            .Require("request " + type);
        await context.TrackAsync(RequestType, created.Id, cancellationToken: ct).ConfigureAwait(false);
        return created;
    }

    private static async Task<RequestRecordDto> SaveAsync(DemoPackContext context, RequestRecordDto request, object data, CancellationToken ct)
    {
        var json = ToJson(data);
        return (await context.RunAsync(s => s.GetRequiredService<RequestCommands>().SaveAsync(request.Id, new SaveInput(json, request.Version), ct)).ConfigureAwait(false))
            .Require("save request");
    }

    private static async Task SubmitAsync(DemoPackContext context, RequestRecordDto request, CancellationToken ct) =>
        (await context.RunAsync(s => s.GetRequiredService<RequestCommands>().SubmitAsync(request.Id, new RequestSubmitInput(request.Version, "Demo submission"), ct))
            .ConfigureAwait(false)).Require("submit " + request.TypeCode);

    private static async Task<Guid> UploadAsync(DemoPackContext context, Guid requestId, string name, string text, CancellationToken ct)
    {
        var file = await context.RunAsync(async s =>
        {
            using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return await s.GetRequiredService<IFileService>().UploadAsync(new FileUpload(RequestFilePurposes.Attachment.Key, requestId.ToString("D"), name, content), ct)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
        return file.Require("upload " + name).Id;
    }

    private static JsonObject ToJson(object data) => JsonSerializer.SerializeToNode(data, JsonSerializerOptions.Web)!.AsObject();
}
