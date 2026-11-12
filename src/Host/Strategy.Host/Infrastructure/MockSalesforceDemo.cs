using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;

namespace Strategy.Host.Infrastructure;

/// <summary>Body of <c>POST /dev/mock-salesforce/change</c>: sets one field of one record in the mock org (a "change in Salesforce").</summary>
public sealed record MockSalesforceChange(string Object, string? Id, string Field, string? Value);

/// <summary>
/// Demonstrations and E2E only (Phase 23 screenshots): a mock Salesforce org with ADoF-like sample data, replacing the Salesforce transport.
/// Registered and mapped only in the Development environment with <c>Development:MockSalesforce=true</c> — never in Production.
/// <c>GET /dev/mock-salesforce</c> gives the login URL, consumer key, username and the demo private key to configure the connection.
/// </summary>
public static class MockSalesforceDemo
{
    public static IServiceCollection AddMockSalesforce(this IServiceCollection services)
    {
        var org = new MockSalesforceOrg { PageSize = 200 };
        Seed(org, ShippedMappingPacks.AdofSalesforceAsync().GetAwaiter().GetResult());
        services.AddSingleton(org);
        services.RemoveAll<ISalesforceTransport>();
        services.AddSingleton<ISalesforceTransport>(org);
        return services;
    }

    public static void MapMockSalesforce(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/dev/mock-salesforce").AllowAnonymous().ExcludeFromDescription();
        group.MapGet("/", (MockSalesforceOrg org) => TypedResults.Ok(new
        {
            loginUrl = MockSalesforceOrg.LoginUrl, clientId = org.ClientId, username = org.Username, apiVersion = MockSalesforceOrg.ApiVersion,
            privateKeyPem = org.PrivateKeyPem,
        }));
        group.MapPost("/change", Results<Ok<string>, NotFound> (MockSalesforceChange body, MockSalesforceOrg org) =>
        {
            if (!org.Objects.TryGetValue(body.Object, out var o))
            {
                return TypedResults.NotFound();
            }

            var id = body.Id ?? org.NewId("a0Z");
            if (body.Field == "IsDeleted")
            {
                o.Delete(id);
            }
            else
            {
                o.Upsert(id, new Dictionary<string, object?> { [body.Field] = body.Value });
            }

            return TypedResults.Ok(id);
        });
    }

    /// <summary>Objects described from the shipped pack (every mapped field exists, picklists list the mapped values) and a small data set.</summary>
    private static void Seed(MockSalesforceOrg org, MappingPackDocument pack)
    {
        string[] standard = ["Id", "IsDeleted", "SystemModstamp", "CreatedDate", "Name", "CreatedById"];
        foreach (var mapping in pack.Objects.Where(o => o.Kind is ObjectMappingKinds.Record or ObjectMappingKinds.People).DistinctBy(o => o.SourceObject))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in standard)
            {
                fields[f] = f is "SystemModstamp" or "CreatedDate" ? "datetime" : f == "IsDeleted" ? "boolean" : f.EndsWith("Id", StringComparison.Ordinal) ? "reference" : "string";
            }

            foreach (var m in pack.Objects.Where(o => o.SourceObject == mapping.SourceObject).SelectMany(o => o.Fields)
                .Where(f => f.Kind != FieldMappingKinds.Constant && f.Source is not null && !f.Source.Contains('.', StringComparison.Ordinal)))
            {
                fields[m.Source!] = m.Kind is FieldMappingKinds.Reference or FieldMappingKinds.Person ? "reference" : m.Kind == FieldMappingKinds.Picklist ? "picklist" : "string";
            }

            if (mapping.Kind == ObjectMappingKinds.People)
            {
                fields["Email"] = "email";
                fields["EmployeeNumber"] = "string";
                fields["FederationIdentifier"] = "string";
            }

            var o = org.AddObject(mapping.SourceObject, [.. fields.Select(f => (f.Key, f.Value))]);
            foreach (var f in pack.Objects.Where(x => x.SourceObject == mapping.SourceObject).SelectMany(x => x.Fields)
                .Where(f => f.Kind == FieldMappingKinds.Picklist && f.Values is { Count: > 0 }).DistinctBy(f => f.Source))
            {
                o.Picklist(f.Source!, [.. f.Values!.Keys.Where(k => k != "*")]);
            }
        }

        org.AddObject("ContentVersion", ("Id", "id"), ("ContentDocumentId", "reference"), ("Title", "string"), ("PathOnClient", "string"), ("FileExtension", "string"),
            ("IsLatest", "boolean"), ("SystemModstamp", "datetime"), ("IsDeleted", "boolean"));
        org.AddObject("ContentDocumentLink", ("Id", "id"), ("ContentDocumentId", "reference"), ("LinkedEntityId", "reference"), ("SystemModstamp", "datetime"));
        org.AddObject("Attachment", ("Id", "id"), ("ParentId", "reference"), ("Name", "string"), ("ContentType", "string"), ("IsDeleted", "boolean"), ("SystemModstamp", "datetime"));
        org.AddObject("ProcessInstance", ("Id", "id"), ("TargetObjectId", "reference"), ("Status", "picklist"), ("CreatedDate", "datetime"), ("SystemModstamp", "datetime"));
        org.AddObject("ProcessInstanceStep", ("Id", "id"), ("ProcessInstanceId", "reference"), ("StepStatus", "picklist"), ("Comments", "textarea"), ("CreatedDate", "datetime"));
        foreach (var history in pack.Objects.Where(o => o.Kind == ObjectMappingKinds.FieldHistory))
        {
            org.AddObject(history.SourceObject, ("Id", "id"), ("ParentId", "reference"), ("Field", "string"), ("OldValue", "string"), ("NewValue", "string"),
                ("CreatedDate", "datetime"), ("IsDeleted", "boolean"));
        }

        org.Objects["Department__c"].Reference("Parent_Department__c", "Department__c", "Parent_Department__r");

        // Data: two departments, the development users, a request, a project with a work plan, a pending approval and a file.
        var it = org.NewId("a01");
        var pmo = org.NewId("a01");
        org.Objects["Department__c"].Upsert(it, new() { ["Name"] = "إدارة التقنية", ["Department_Name_English__c"] = "Technology Department", ["Short_Code__c"] = "SF-IT",
            ["Record_Type__c"] = "Department" });
        org.Objects["Department__c"].Upsert(pmo, new() { ["Name"] = "قسم إدارة المشاريع", ["Department_Name_English__c"] = "Project Management Section", ["Short_Code__c"] = "SF-PMO",
            ["Record_Type__c"] = "Section", ["Parent_Department__c"] = it });
        var mariam = org.NewId("005");
        var ahmed = org.NewId("005");
        org.Objects["User"].Upsert(mariam, new() { ["Name"] = "Mariam Al Suwaidi", ["Email"] = "mariam@strategy.local" });
        org.Objects["User"].Upsert(ahmed, new() { ["Name"] = "Ahmed Al Ali", ["Email"] = "ahmed@strategy.local" });
        org.Objects["User"].Upsert(org.NewId("005"), new() { ["Name"] = "Salesforce Admin", ["Email"] = "sf.admin@adof.example" });
        var request = org.NewId("a0R");
        org.Objects["PMS_Project_Request__c"].Upsert(request, new() { ["Name"] = "PR-0107", ["Status__c"] = "Approved", ["Program_Name__c"] = "منصة البيانات الحكومية",
            ["Program_Description__c"] = "منصة موحدة لبيانات الدائرة", ["Program_Type__c"] = "Digital", ["Department__c"] = it, ["Section_to_Implement__c"] = pmo,
            ["CreatedById"] = ahmed, ["Does_it_require_Budget__c"] = "Yes", ["Program_Estimated_Budget__c"] = 850000m, ["Main_Option__c"] = "Contracting with Company" });
        var project = org.NewId("a0P");
        org.Objects["PMS_Project__c"].Upsert(project, new() { ["Name"] = "PROJ-SPMO-2026-014", ["PMS_Project_Request__c"] = request, ["Project_Name__c"] = "منصة البيانات الحكومية",
            ["Program_Classification__c"] = "Project", ["Project_Stage__c"] = "Planning", ["Project_Status__c"] = "Active", ["Approval_Status__c"] = "PM Office - Planning",
            ["Project_Manager__c"] = ahmed, ["Project_Owner__c"] = mariam, ["Department_Concerned__c"] = it, ["Project_Budget__c"] = 850000m,
            ["Planned_Project_Start_Date__c"] = "2026-03-01", ["Planned_Project_End_Date__c"] = "2027-02-28", ["Total_Achieve__c"] = 18.5m, ["Till_Weight__c"] = 25m,
            ["Operational_Themes__c"] = "Future Readiness" });
        var phase = org.NewId("a0W");
        org.Objects["PMS_Work_Plan__c"].Upsert(phase, new() { ["Name"] = "1", ["PMS_Project__c"] = project, ["Is_this_header_task__c"] = true, ["Status__c"] = "Active",
            ["Weight__c"] = 40m, ["Planned_Start_Date__c"] = "2026-03-01", ["Planned_End_Date__c"] = "2026-06-30" });
        org.Objects["PMS_Work_Plan__c"].Upsert(org.NewId("a0W"), new() { ["Name"] = "1.1", ["PMS_Project__c"] = project, ["Header_Phase__c"] = phase, ["Status__c"] = "Completed",
            ["Weight__c"] = 15m, ["Achieve__c"] = 15m, ["Planned_Start_Date__c"] = "2026-03-01", ["Planned_End_Date__c"] = "2026-04-15" });
        var instance = org.NewId("04g");
        org.Objects["ProcessInstance"].Upsert(instance, new() { ["TargetObjectId"] = project, ["Status"] = "Pending", ["ProcessDefinition.Name"] = "PMS Planning Approval" });
        org.Objects["ProcessInstanceStep"].Upsert(org.NewId("04h"), new() { ["ProcessInstanceId"] = instance, ["ProcessInstance.TargetObjectId"] = project,
            ["ProcessInstance.Status"] = "Pending", ["ProcessInstance.ProcessDefinition.Name"] = "PMS Planning Approval", ["StepStatus"] = "Started",
            ["Actor.Name"] = "Ahmed Al Ali", ["Comments"] = "خطة المشروع جاهزة للاعتماد" });
        var document = org.NewId("069");
        org.Objects["ContentVersion"].Upsert(org.NewId("068"), new() { ["ContentDocumentId"] = document, ["Title"] = "Project charter", ["PathOnClient"] = "project-charter.pdf",
            ["FileExtension"] = "pdf", ["IsLatest"] = true, ["__content"] = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n") });
        org.Objects["ContentDocumentLink"].Upsert(org.NewId("06A"), new() { ["ContentDocumentId"] = document, ["LinkedEntityId"] = project });
    }
}
