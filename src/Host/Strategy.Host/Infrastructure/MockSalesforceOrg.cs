using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Strategy.Platform.Integration.Sync;
using Strategy.Platform.Integration.Sync.Salesforce;

namespace Strategy.Host.Infrastructure;

/// <summary>
/// An in-process stand-in Salesforce org for the integration tests (no live org calls in CI) and, with <c>Development:MockSalesforce=true</c>
/// in the Development environment only, for demonstrations and E2E screenshots (docs/design/salesforce-sync.md §14): recorded-style describe responses,
/// in-memory records per object, the OAuth JWT bearer token endpoint (the assertion's RS256 signature is verified with the test key), REST
/// <c>queryAll</c> with <c>nextRecordsUrl</c> paging, Bulk API 2.0 query jobs with CSV pages and <c>Sforce-Locator</c>, <c>/limits</c> and file
/// downloads. It understands the SOQL the connector sends (field list, <c>FROM</c>, <c>AND</c>-joined conditions: <c>&gt;=</c> on timestamps,
/// <c>= true/false/'x'</c>, <c>IN (…)</c>, <c>COUNT()</c>). Every request is logged; any other verb or path is recorded in <see cref="Violations"/>.
/// </summary>
public sealed partial class MockSalesforceOrg : ISalesforceTransport
{
    public const string LoginUrl = "https://login.salesforce.test";
    public const string InstanceUrl = "https://adof.my.salesforce.test";
    public const string ApiVersion = "v62.0";
    private const string Data = "/services/data/" + ApiVersion;

    private readonly ConcurrentDictionary<string, string> _bulkJobs = new();
    private readonly ConcurrentDictionary<string, (List<Dictionary<string, object?>> Rows, string[] Fields)> _cursors = new();
    private int _ids;

    public MockSalesforceOrg()
    {
        Key = RSA.Create(2048);
        PrivateKeyPem = Key.ExportPkcs8PrivateKeyPem();
    }

    public RSA Key { get; }

    public string PrivateKeyPem { get; }

    public string ClientId { get; } = "3MVG9fake.consumer.key";

    public string Username { get; } = "integration@adof.example";

    /// <summary>Rows per REST page / Bulk results page (small, so paging is exercised).</summary>
    public int PageSize { get; set; } = 3;

    public Dictionary<string, MockObject> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> Violations { get; } = new();

    private int _tokenRequests;

    public int TokenRequests => _tokenRequests;

    /// <summary>Fail the next N data requests with 503 (retry test).</summary>
    public int FailNext { get; set; }

    public bool RejectToken { get; set; }

    /// <summary>
    /// Refuse <c>/limits</c> like an org whose integration user lacks <i>View Setup and Configuration</i> (the <i>Salesforce Integration</i>
    /// licence; ADR 0210): e.g. <c>(Forbidden, "INSUFFICIENT_ACCESS")</c> or <c>(BadRequest, "FUNCTIONALITY_NOT_ENABLED")</c>.
    /// </summary>
    public (HttpStatusCode Status, string ErrorCode)? RefuseLimits { get; set; }

    /// <summary>Daily API requests allowed (reported by <c>/limits</c> and the <c>Sforce-Limit-Info</c> header of every answer).</summary>
    public int ApiMax { get; set; } = 100000;

    /// <summary>Daily API requests used; null = 1 000 plus the requests seen so far.</summary>
    public int? ApiUsed { get; set; }

    private int Used => ApiUsed ?? 1000 + Requests.Count;

    public MockObject AddObject(string name, params (string Name, string Type)[] fields)
    {
        var o = new MockObject(name, fields.Select(f => new MockField(f.Name, f.Type)).ToList());
        Objects[name] = o;
        return o;
    }

    /// <summary>
    /// An object with a describe recorded from a real org (<c>Infrastructure/SalesforceDescribes/{name}.json</c>: User without IsDeleted, Contact
    /// without EmployeeNumber / FederationIdentifier), so CI catches queries of fields Salesforce does not have (ADR 0212).
    /// </summary>
    public MockObject AddRecordedObject(string name)
    {
        using var stream = typeof(MockSalesforceOrg).Assembly.GetManifestResourceStream($"Strategy.Host.SalesforceDescribes.{name}.json")
            ?? throw new InvalidOperationException($"No recorded describe for {name}.");
        using var json = JsonDocument.Parse(stream);
        var fields = json.RootElement.GetProperty("fields").EnumerateArray().Select(f => new MockField(f.GetProperty("name").GetString()!, f.GetProperty("type").GetString()!)
        {
            ReferenceTo = f.TryGetProperty("referenceTo", out var r) && r.GetArrayLength() > 0 ? r[0].GetString() : null,
            RelationshipName = f.TryGetProperty("relationshipName", out var rel) ? rel.GetString() : null,
        }).ToList();
        var o = new MockObject(json.RootElement.GetProperty("name").GetString()!, fields);
        Objects[o.Name] = o;
        return o;
    }

    /// <summary>The standard objects of files, attachments and approvals, with their real relationship fields (for Validate's full field check).</summary>
    public void AddStandardObjects()
    {
        AddObject("ContentVersion", ("Id", "id"), ("ContentDocumentId", "reference"), ("Title", "string"), ("PathOnClient", "string"), ("FileExtension", "string"),
            ("IsLatest", "boolean"), ("SystemModstamp", "datetime"), ("IsDeleted", "boolean"));
        AddObject("ContentDocumentLink", ("Id", "id"), ("ContentDocumentId", "reference"), ("LinkedEntityId", "reference"), ("SystemModstamp", "datetime"), ("IsDeleted", "boolean"));
        AddObject("Attachment", ("Id", "id"), ("ParentId", "reference"), ("Name", "string"), ("ContentType", "string"), ("IsDeleted", "boolean"), ("SystemModstamp", "datetime"));
        AddObject("ProcessDefinition", ("Id", "id"), ("Name", "string"), ("DeveloperName", "string"));
        AddObject("ProcessInstance", ("Id", "id"), ("TargetObjectId", "reference"), ("Status", "picklist"), ("ProcessDefinitionId", "reference"), ("CreatedDate", "datetime"),
            ("SystemModstamp", "datetime"), ("IsDeleted", "boolean"))
            .Reference("ProcessDefinitionId", "ProcessDefinition", "ProcessDefinition");
        AddObject("ProcessInstanceStep", ("Id", "id"), ("ProcessInstanceId", "reference"), ("StepStatus", "picklist"), ("ActorId", "reference"), ("Comments", "textarea"),
            ("CreatedDate", "datetime"), ("SystemModstamp", "datetime"))
            .Reference("ProcessInstanceId", "ProcessInstance", "ProcessInstance")
            .Reference("ActorId", "User", "Actor");
    }

    /// <summary>A <c>…__History</c> object (field history) with its real fields.</summary>
    public MockObject AddHistoryObject(string name) =>
        AddObject(name, ("Id", "id"), ("ParentId", "reference"), ("Field", "picklist"), ("OldValue", "anyType"), ("NewValue", "anyType"),
            ("CreatedDate", "datetime"), ("CreatedById", "reference"), ("IsDeleted", "boolean"))
            .Reference("CreatedById", "User", "CreatedBy");

    public string NewId(string prefix) => prefix + Interlocked.Increment(ref _ids).ToString("D12", CultureInfo.InvariantCulture) + "AAA";

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await AnswerAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.RequestUri!.AbsoluteUri.StartsWith(InstanceUrl, StringComparison.Ordinal))
        {
            // Like Salesforce: the daily API usage on every REST answer (ADR 0210).
            response.Headers.TryAddWithoutValidation("Sforce-Limit-Info", string.Create(CultureInfo.InvariantCulture, $"api-usage={Used}/{ApiMax}"));
        }

        return response;
    }

    private async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await RouteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (MockSoqlException ex)
        {
            return Json(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new[] { new { message = ex.Message, errorCode = ex.ErrorCode } }));
        }
    }

    private async Task<HttpResponseMessage> RouteAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Enqueue($"{request.Method} {uri.AbsolutePath}");
        if (!SalesforceSession.IsReadOnlyRequest(request))
        {
            Violations.Enqueue($"{request.Method} {uri}");
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }

        if (uri.AbsoluteUri.StartsWith(LoginUrl, StringComparison.Ordinal) && uri.AbsolutePath == "/services/oauth2/token")
        {
            return await TokenAsync(request).ConfigureAwait(false);
        }

        if (!uri.AbsoluteUri.StartsWith(InstanceUrl, StringComparison.Ordinal))
        {
            Violations.Enqueue("unknown host " + uri);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Headers.Authorization?.Parameter != "fake-access-token")
        {
            return Json(HttpStatusCode.Unauthorized, """[{"errorCode":"INVALID_SESSION_ID"}]""");
        }

        if (FailNext > 0)
        {
            FailNext--;
            return Json(HttpStatusCode.ServiceUnavailable, """[{"errorCode":"SERVER_UNAVAILABLE"}]""");
        }

        var path = uri.AbsolutePath;
        if (path == Data + "/sobjects")
        {
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                sobjects = Objects.Values.Select(o => new { name = o.Name, label = o.Name, queryable = true, custom = o.Name.EndsWith("__c", StringComparison.Ordinal), keyPrefix = "a0X" }),
            }));
        }

        if (DescribePath().Match(path) is { Success: true } describe)
        {
            return Objects.TryGetValue(describe.Groups[1].Value, out var o)
                ? Json(HttpStatusCode.OK, Describe(o))
                : Json(HttpStatusCode.NotFound, """[{"errorCode":"NOT_FOUND"}]""");
        }

        if (path is Data or Data + "/")
        {
            return Json(HttpStatusCode.OK, $$"""{"sobjects":"{{Data}}/sobjects","query":"{{Data}}/query","limits":"{{Data}}/limits"}""");
        }

        if (path == Data + "/limits")
        {
            if (RefuseLimits is { } refusal)
            {
                return Json(refusal.Status, JsonSerializer.Serialize(new[]
                {
                    new { message = "The REST API resource /limits needs the View Setup and Configuration permission.", errorCode = refusal.ErrorCode },
                }));
            }

            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["DailyApiRequests"] = new { Max = ApiMax, Remaining = ApiMax - Used },
                ["DailyBulkV2QueryJobs"] = new { Max = 10000, Remaining = 9990 },
            }));
        }

        if (path == Data + "/queryAll")
        {
            var soql = Uri.UnescapeDataString(Query(uri, "q")!.Replace('+', ' '));
            return Run(soql);
        }

        if (CursorPath().Match(path) is { Success: true } cursor)
        {
            return Page(cursor.Groups[1].Value, int.Parse(cursor.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        if (path == Data + "/jobs/query" && request.Method == HttpMethod.Post)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (body.RootElement.GetProperty("operation").GetString() != "queryAll")
            {
                Violations.Enqueue("bulk operation " + body.RootElement.GetProperty("operation").GetString());
            }

            var id = "750" + Guid.NewGuid().ToString("N")[..12];
            _bulkJobs[id] = body.RootElement.GetProperty("query").GetString()!;
            _ = Select(_bulkJobs[id]); // Salesforce validates the query when the job is created (INVALID_FIELD)
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, state = "UploadComplete" }));
        }

        if (BulkJobPath().Match(path) is { Success: true } job)
        {
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { id = job.Groups[1].Value, state = "JobComplete" }));
        }

        if (BulkResultsPath().Match(path) is { Success: true } results)
        {
            return BulkPage(results.Groups[1].Value, Query(uri, "locator"));
        }

        if (DownloadPath().Match(path) is { Success: true } download)
        {
            var o = Objects[download.Groups[1].Value];
            var row = o.Rows.First(r => (string?)r["Id"] == download.Groups[2].Value);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent((byte[])row["__content"]!) };
        }

        Violations.Enqueue("unhandled " + request.Method + " " + uri);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request)
    {
        Interlocked.Increment(ref _tokenRequests);
        var form = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
        var values = form.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        if (values["grant_type"] != "urn:ietf:params:oauth:grant-type:jwt-bearer")
        {
            return Json(HttpStatusCode.BadRequest, """{"error":"unsupported_grant_type"}""");
        }

        var parts = values["assertion"].Split('.');
        var signed = Key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
        var ok = signed && !RejectToken && payload.RootElement.GetProperty("iss").GetString() == ClientId
            && payload.RootElement.GetProperty("sub").GetString() == Username && payload.RootElement.GetProperty("aud").GetString() == LoginUrl;
        return ok
            ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { access_token = "fake-access-token", instance_url = InstanceUrl, token_type = "Bearer" }))
            : Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"user hasn't approved this consumer"}""");
    }

    private HttpResponseMessage Run(string soql)
    {
        var (o, fields, rows) = Select(soql);
        if (fields is null)
        {
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { totalSize = rows.Count, done = true, records = Array.Empty<object>() }));
        }

        var cursor = Guid.NewGuid().ToString("N");
        _cursors[cursor] = (rows, fields);
        _ = o;
        return Page(cursor, 0);
    }

    private HttpResponseMessage Page(string cursor, int offset)
    {
        var (rows, fields) = _cursors[cursor];
        var page = rows.Skip(offset).Take(PageSize).Select(r => ToJson(r, fields)).ToList();
        var done = offset + PageSize >= rows.Count;
        var body = new JsonObject
        {
            ["totalSize"] = rows.Count,
            ["done"] = done,
            ["records"] = new JsonArray([.. page]),
        };
        if (!done)
        {
            body["nextRecordsUrl"] = $"{Data}/query/{cursor}-{offset + PageSize}";
        }

        return Json(HttpStatusCode.OK, body.ToJsonString());
    }

    private HttpResponseMessage BulkPage(string jobId, string? locator)
    {
        var (_, fields, rows) = Select(_bulkJobs[jobId]);
        var offset = locator is null ? 0 : int.Parse(locator, CultureInfo.InvariantCulture);
        var csv = new StringBuilder(string.Join(',', fields!)).Append('\n');
        foreach (var row in rows.Skip(offset).Take(PageSize))
        {
            csv.Append(string.Join(',', fields!.Select(f => CsvValue(row.GetValueOrDefault(f))))).Append('\n');
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv.ToString(), Encoding.UTF8, "text/csv") };
        response.Headers.Add("Sforce-Locator", offset + PageSize >= rows.Count ? "null" : (offset + PageSize).ToString(CultureInfo.InvariantCulture));
        return response;
    }

    private (MockObject Object, string[]? Fields, List<Dictionary<string, object?>> Rows) Select(string soql)
    {
        var match = SoqlPattern().Match(soql);
        if (!match.Success)
        {
            throw new InvalidOperationException("unsupported SOQL: " + soql);
        }

        var o = Objects.TryGetValue(match.Groups["from"].Value, out var found) ? found
            : throw new MockSoqlException("INVALID_TYPE", $"sObject type '{match.Groups["from"].Value}' is not supported.");
        var select = match.Groups["select"].Value.Trim();
        string[]? fields = select == "COUNT()" ? null : [.. select.Split(',').Select(f => f.Trim())];
        if (fields is not null)
        {
            foreach (var f in fields.Where(f => !f.Contains('.', StringComparison.Ordinal)))
            {
                if (!o.Fields.Any(x => string.Equals(x.Name, f, StringComparison.OrdinalIgnoreCase)))
                {
                    // Salesforce's own answer (HTTP 400): the connector must report it with the call and the errorCode (ADR 0211).
                    throw new MockSoqlException("INVALID_FIELD",
                        $"\n{select} FROM {o.Name}\n^\nERROR at Row:1:Column:8\nNo such column '{f}' on entity '{o.Name}'. If you are attempting to use a custom field, be sure to append the '__c' after the custom field name. Please reference your WSDL or the describe call for the appropriate names.");
                }
            }
        }

        IEnumerable<Dictionary<string, object?>> rows = o.Rows.Where(r => !r.ContainsKey("__purged"));
        if (match.Groups["where"].Success)
        {
            foreach (var condition in SplitAnd(match.Groups["where"].Value))
            {
                rows = rows.Where(r => Matches(r, condition)).ToList();
            }
        }

        if (match.Groups["order"].Success)
        {
            var field = match.Groups["order"].Value;
            rows = rows.OrderBy(r => r.GetValueOrDefault(field) as string, StringComparer.Ordinal);
        }

        return (o, fields, rows.ToList());
    }

    private static IEnumerable<string> SplitAnd(string where)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < where.Length; i++)
        {
            depth += where[i] == '(' ? 1 : where[i] == ')' ? -1 : 0;
            if (depth == 0 && string.Compare(where, i, " AND ", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                yield return where[start..i].Trim();
                start = i + 5;
            }
        }

        yield return where[start..].Trim();
    }

    private static bool Matches(Dictionary<string, object?> row, string condition)
    {
        var c = condition.Trim();
        while (c.StartsWith('(') && c.EndsWith(')'))
        {
            c = c[1..^1].Trim();
        }

        if (c.Contains(" AND ", StringComparison.OrdinalIgnoreCase))
        {
            return SplitAnd(c).All(part => Matches(row, part));
        }

        if (InPattern().Match(c) is { Success: true } inList)
        {
            var values = inList.Groups[2].Value.Split(',').Select(v => v.Trim().Trim('\'')).ToHashSet(StringComparer.Ordinal);
            return row.GetValueOrDefault(inList.Groups[1].Value) is string s && values.Contains(s);
        }

        var compare = ComparePattern().Match(c);
        if (!compare.Success)
        {
            throw new InvalidOperationException("unsupported condition: " + c);
        }

        var actual = row.GetValueOrDefault(compare.Groups[1].Value);
        var op = compare.Groups[2].Value;
        var literal = compare.Groups[3].Value.Trim();
        if (op == ">=")
        {
            return actual is string text && FieldMapper.TryParseDateTime(text, out var at) && FieldMapper.TryParseDateTime(literal, out var since) && at >= since;
        }

        return literal switch
        {
            "true" => actual is true,
            "false" => actual is false or null,
            _ => actual?.ToString() == literal.Trim('\''),
        };
    }

    private static JsonObject ToJson(Dictionary<string, object?> row, string[] fields)
    {
        var json = new JsonObject { ["attributes"] = new JsonObject { ["type"] = "x" } };
        foreach (var field in fields)
        {
            var value = row.GetValueOrDefault(field);
            var node = value switch
            {
                null => null,
                bool b => JsonValue.Create(b),
                decimal d => JsonValue.Create(d),
                int i => JsonValue.Create(i),
                double d => JsonValue.Create(d),
                _ => (JsonNode?)JsonValue.Create(value.ToString()),
            };
            var parts = field.Split('.');
            var target = json;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (target[parts[i]] is not JsonObject next)
                {
                    next = new JsonObject { ["attributes"] = new JsonObject { ["type"] = parts[i] } };
                    target[parts[i]] = next;
                }

                target = next;
            }

            target[parts[^1]] = node;
        }

        return json;
    }

    private static string CsvValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        return text.Contains(',', StringComparison.Ordinal) || text.Contains('"', StringComparison.Ordinal) || text.Contains('\n', StringComparison.Ordinal)
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    private static string Describe(MockObject o) => JsonSerializer.Serialize(new
    {
        name = o.Name,
        label = o.Name,
        keyPrefix = "a0X",
        fields = o.Fields.Select(f => new
        {
            name = f.Name,
            label = f.Name,
            type = f.Type,
            length = 255,
            nillable = true,
            referenceTo = f.ReferenceTo is null ? Array.Empty<string>() : [f.ReferenceTo],
            relationshipName = f.RelationshipName,
            picklistValues = f.Picklist.Select(p => new { value = p, label = p, active = true }),
        }),
    });

    private static string? Query(Uri uri, string name) =>
        uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p[0] == name).Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }

    [GeneratedRegex(@"^SELECT (?<select>.+?) FROM (?<from>[A-Za-z0-9_]+)(?: WHERE (?<where>.+?))?(?: ORDER BY (?<order>[A-Za-z0-9_.]+) ASC)?$", RegexOptions.Singleline)]
    private static partial Regex SoqlPattern();

    [GeneratedRegex(@"^([A-Za-z0-9_.]+)\s+IN\s+\((.*)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex InPattern();

    [GeneratedRegex(@"^([A-Za-z0-9_.]+)\s*(>=|=)\s*(.+)$")]
    private static partial Regex ComparePattern();

    [GeneratedRegex(@"^/services/data/v62\.0/sobjects/([A-Za-z0-9_]+)/describe$")]
    private static partial Regex DescribePath();

    [GeneratedRegex(@"^/services/data/v62\.0/query/([a-f0-9]+)-(\d+)$")]
    private static partial Regex CursorPath();

    [GeneratedRegex(@"^/services/data/v62\.0/jobs/query/([A-Za-z0-9]+)$")]
    private static partial Regex BulkJobPath();

    [GeneratedRegex(@"^/services/data/v62\.0/jobs/query/([A-Za-z0-9]+)/results$")]
    private static partial Regex BulkResultsPath();

    [GeneratedRegex(@"^/services/data/v62\.0/sobjects/([A-Za-z0-9_]+)/([A-Za-z0-9]+)/(VersionData|Body)$")]
    private static partial Regex DownloadPath();
}

/// <summary>A SOQL error Salesforce answers with HTTP 400 (<c>INVALID_FIELD</c>, <c>INVALID_TYPE</c>).</summary>
public sealed class MockSoqlException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record MockField(string Name, string Type)
{
    public string? ReferenceTo { get; init; }

    public string? RelationshipName { get; init; }

    public List<string> Picklist { get; } = [];
}

/// <summary>One fake object: its describe fields and its rows (values keyed by API name; relationship values as <c>Rel.Field</c>).</summary>
public sealed class MockObject(string name, List<MockField> fields)
{
    private static int _tick;

    public string Name { get; } = name;

    public List<MockField> Fields { get; } = fields;

    public List<Dictionary<string, object?>> Rows { get; } = [];

    /// <summary>A monotonically increasing SystemModstamp for this object (one second apart, from a fixed base).</summary>
    public static DateTimeOffset Base { get; set; } = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Salesforce's own timestamp format (<c>+0000</c> offset), one tick (10 minutes) apart across all objects.</summary>
    public static string NextStamp() => Base.AddMinutes(10 * Interlocked.Increment(ref _tick))
        .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+0000'", CultureInfo.InvariantCulture);

    public MockObject Picklist(string field, params string[] values)
    {
        Fields.First(f => f.Name == field).Picklist.AddRange(values);
        return this;
    }

    public MockObject Reference(string field, string to, string relationship)
    {
        var index = Fields.FindIndex(f => f.Name == field);
        Fields[index] = Fields[index] with { ReferenceTo = to, RelationshipName = relationship };
        return this;
    }

    /// <summary>Adds or replaces a row (by Id) with a new SystemModstamp.</summary>
    public Dictionary<string, object?> Upsert(string id, Dictionary<string, object?> values)
    {
        var row = Rows.FirstOrDefault(r => (string?)r["Id"] == id);
        if (row is null)
        {
            row = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Id"] = id, ["CreatedDate"] = NextStamp() };
            if (Has("IsDeleted"))
            {
                row["IsDeleted"] = false; // User has no IsDeleted (users are deactivated, never deleted)
            }

            if (Has("IsActive"))
            {
                row["IsActive"] = true;
            }

            Rows.Add(row);
        }

        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        row["SystemModstamp"] = NextStamp();
        return row;
    }

    public bool Has(string field) => Fields.Any(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));

    public void Delete(string id)
    {
        if (!Has("IsDeleted"))
        {
            throw new InvalidOperationException($"{Name} records cannot be deleted in Salesforce (deactivate them).");
        }

        var row = Rows.First(r => (string?)r["Id"] == id);
        row["IsDeleted"] = true;
        row["SystemModstamp"] = NextStamp();
    }
}
