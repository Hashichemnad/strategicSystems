using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Strategy.Platform.Kernel.Net;

namespace Strategy.Platform.Integration.Sync.Salesforce;

/// <summary>HTTP transport of the Salesforce connector; tests replace it with a fake Salesforce (no live org in CI).</summary>
public interface ISalesforceTransport
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);

    /// <summary>Waits before a retry (tests: no wait).</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Production transport: the outbound URL policy's handler (no redirects, forbidden addresses refused at connect time, ADR 0155).</summary>
internal sealed class PolicySalesforceTransport(OutboundUrlPolicy policy) : ISalesforceTransport, IDisposable
{
    private readonly HttpClient _client = new(policy.CreateHandler()) { Timeout = TimeSpan.FromMinutes(5) };

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

    public void Dispose() => _client.Dispose();
}

/// <summary>A source row: field → value as text (invariant), relationship fields flattened (<c>RecordType.DeveloperName</c>).</summary>
public sealed record SourceRecord(IReadOnlyDictionary<string, string?> Values)
{
    public string? this[string field] => Values.TryGetValue(field, out var value) ? value : null;

    public string Id => this["Id"] ?? string.Empty;

    public bool IsDeleted => string.Equals(this["IsDeleted"], "true", StringComparison.OrdinalIgnoreCase);

    public string ToJson() => JsonSerializer.Serialize(Values, SyncJson.Options);

    public static SourceRecord FromJson(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string?>>(json, SyncJson.Options) ?? []);
}

public sealed record SourceObjectInfo(string Name, string Label, bool Queryable, string? KeyPrefix, bool Custom);

public sealed record SourcePicklistValue(string Value, string? Label, bool Active);

public sealed record SourceFieldInfo(
    string Name, string Label, string Type, int Length, bool Nillable, IReadOnlyList<string> ReferenceTo, string? RelationshipName,
    IReadOnlyList<SourcePicklistValue> PicklistValues);

public sealed record SourceObjectDescribe(string Name, string Label, string? KeyPrefix, IReadOnlyList<SourceFieldInfo> Fields)
{
    public SourceFieldInfo? Field(string name) => Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A connector failure that stops the run (auth, limits, HTTP after retries) with a message key for the run log.</summary>
public sealed class SourceConnectorException(string messageKey, string? detail = null, Exception? inner = null, SalesforceError? error = null)
    : Exception(detail ?? messageKey, inner)
{
    public SourceConnectorException(string messageKey, SalesforceError error)
        : this(messageKey, error?.Summary, null, error)
    {
    }

    public string MessageKey { get; } = messageKey;

    public string? Detail { get; } = detail;

    /// <summary>What Salesforce answered (HTTP status, call, errorCode, message); null for failures before any answer (e.g. an invalid key).</summary>
    public SalesforceError? Error { get; } = error;

    /// <summary>Run-log / run parameters: the structured error fields, plus <c>detail</c> for failures without a Salesforce answer.</summary>
    public Dictionary<string, string?>? Params()
    {
        if (Error is not null)
        {
            return Error.ToParams();
        }

        return Detail is null ? null : new Dictionary<string, string?> { ["detail"] = Detail };
    }
}

/// <summary>
/// What Salesforce answered to a failed call (ADR 0211): HTTP status, a sanitised description of the call (never the URL with its query
/// string, never headers or tokens), Salesforce's <c>errorCode</c> and its message (truncated). Shown on the Connection tab, in the run log
/// and written to the server log.
/// </summary>
public sealed record SalesforceError(int? Status, string Call, string? ErrorCode, string? Message)
{
    public const int MaxMessage = 500;

    public string Summary => string.Join(' ', new[] { Status?.ToString(CultureInfo.InvariantCulture), ErrorCode, Message }.Where(p => !string.IsNullOrEmpty(p)));

    public Dictionary<string, string?> ToParams() => new(StringComparer.Ordinal)
    {
        ["status"] = Status?.ToString(CultureInfo.InvariantCulture),
        ["call"] = Call,
        ["errorCode"] = ErrorCode,
        ["message"] = Message,
    };

    /// <summary>Reads Salesforce's error body: REST <c>[{"errorCode","message"}]</c>, OAuth <c>{"error","error_description"}</c>, Bulk
    /// <c>{"errorMessage"}</c>; anything else is kept as plain text (truncated).</summary>
    public static SalesforceError FromBody(int? status, string call, string? body)
    {
        string? code = null;
        string? message = null;
        var text = body?.Trim() ?? string.Empty;
        if (text.Length > 0 && text[0] is '[' or '{')
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                var root = json.RootElement;
                var first = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().FirstOrDefault() : root;
                if (first.ValueKind == JsonValueKind.Object)
                {
                    code = Text(first, "errorCode") ?? Text(first, "error");
                    message = Text(first, "message") ?? Text(first, "error_description") ?? Text(first, "errorMessage");
                }
            }
            catch (JsonException)
            {
                message = text;
            }
        }
        else if (text.Length > 0)
        {
            message = text;
        }

        return new SalesforceError(status, call, Clean(code, 100), Clean(message, MaxMessage));
    }

    /// <summary>True when Salesforce refused the call for lack of permission or a feature (e.g. <c>/limits</c> without View Setup, ADR 0210).</summary>
    public bool IsPermissionRefusal =>
        Status is 401 or 403 || ErrorCode is "INSUFFICIENT_ACCESS" or "FUNCTIONALITY_NOT_ENABLED" or "API_DISABLED_FOR_ORG";

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var single = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return single.Length > max ? single[..max] + "…" : single;
    }
}

/// <summary>Daily API usage as Salesforce reports it on every REST response (<c>Sforce-Limit-Info: api-usage=used/max</c>, ADR 0210).</summary>
public sealed record ApiUsage(int Used, int Max, DateTimeOffset At)
{
    public ApiLimits ToLimits() => new(Max, Math.Max(0, Max - Used), null, null, At, ApiLimitSources.Header);

    /// <summary>Parses the header value; null when it carries no <c>api-usage</c> part.</summary>
    public static ApiUsage? Parse(string? header, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var match = SalesforceSession.ApiUsageHeader().Match(header);
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var used)
            && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var max) && max > 0
            ? new ApiUsage(used, max, at)
            : null;
    }
}

/// <summary>Settings of one session (from the connection; the private key is decrypted only in memory).</summary>
public sealed record SalesforceSessionOptions(string LoginUrl, string ClientId, string Username, string PrivateKeyPem, string ApiVersion, int PageSize);

/// <summary>
/// Read-only Salesforce REST / Bulk API 2.0 client (ADR 0201). The only non-GET requests are the OAuth token request and Bulk API 2.0 query
/// job creation (<c>POST …/jobs/query</c>); <see cref="IsReadOnlyRequest"/> refuses anything else, so no code path can write to Salesforce.
/// </summary>
public sealed partial class SalesforceSession(ISalesforceTransport transport, SalesforceSessionOptions options, TimeProvider? time = null)
{
    private const int MaxAttempts = 3;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _token;

    public string? InstanceUrl { get; private set; }

    public int ApiCalls { get; private set; }

    /// <summary>The latest <c>Sforce-Limit-Info</c> api-usage Salesforce sent (null before the first REST answer).</summary>
    public ApiUsage? ApiUsage { get; private set; }

    private string DataPath => $"/services/data/{options.ApiVersion}";

    // ---- Authentication: OAuth 2.0 JWT bearer ----

    public async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        var assertion = SalesforceJwt.CreateAssertion(options.ClientId, options.Username, options.LoginUrl, options.PrivateKeyPem, _time.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, options.LoginUrl.TrimEnd('/') + "/services/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }),
        };
        using var response = await SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SourceConnectorException("integration.sync.errors.authFailed", SalesforceError.FromBody((int)response.StatusCode, Calls.Token, body));
        }

        using var json = JsonDocument.Parse(body);
        _token = json.RootElement.GetProperty("access_token").GetString();
        InstanceUrl = json.RootElement.GetProperty("instance_url").GetString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(_token) || !Uri.TryCreate(InstanceUrl, UriKind.Absolute, out var instance) || instance.Scheme != Uri.UriSchemeHttps)
        {
            throw new SourceConnectorException("integration.sync.errors.authFailed",
                new SalesforceError((int)response.StatusCode, Calls.Token, null, "The token response has no access token or no HTTPS instance URL."));
        }
    }

    // ---- Describe ----

    public async Task<IReadOnlyList<SourceObjectInfo>> ListObjectsAsync(CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"{DataPath}/sobjects", "GET /sobjects", cancellationToken).ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("sobjects").EnumerateArray().Select(o => new SourceObjectInfo(
            o.GetProperty("name").GetString()!, Str(o, "label") ?? string.Empty, Bool(o, "queryable"), Str(o, "keyPrefix"), Bool(o, "custom")))];
    }

    public async Task<SourceObjectDescribe> DescribeAsync(string objectName, CancellationToken cancellationToken)
    {
        if (!ApiName().IsMatch(objectName))
        {
            throw new SourceConnectorException("integration.sync.errors.invalidObject", objectName);
        }

        using var json = await GetJsonAsync($"{DataPath}/sobjects/{objectName}/describe", $"GET /sobjects/{objectName}/describe", cancellationToken).ConfigureAwait(false);
        var root = json.RootElement;
        var fields = root.GetProperty("fields").EnumerateArray().Select(f => new SourceFieldInfo(
            f.GetProperty("name").GetString()!, Str(f, "label") ?? string.Empty, Str(f, "type") ?? "string",
            f.TryGetProperty("length", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
            !f.TryGetProperty("nillable", out var n) || n.ValueKind != JsonValueKind.False,
            f.TryGetProperty("referenceTo", out var r) && r.ValueKind == JsonValueKind.Array ? [.. r.EnumerateArray().Select(x => x.GetString()!)] : [],
            Str(f, "relationshipName"),
            f.TryGetProperty("picklistValues", out var p) && p.ValueKind == JsonValueKind.Array
                ? [.. p.EnumerateArray().Select(v => new SourcePicklistValue(v.GetProperty("value").GetString()!, Str(v, "label"),
                    !v.TryGetProperty("active", out var a) || a.ValueKind != JsonValueKind.False))]
                : [])).ToList();
        return new SourceObjectDescribe(root.GetProperty("name").GetString()!, Str(root, "label") ?? objectName, Str(root, "keyPrefix"), fields);
    }

    /// <summary>
    /// <c>/limits</c>, or — when Salesforce refuses it (it needs <i>View Setup and Configuration</i>, which the <i>Salesforce Integration</i>
    /// licence cannot have; ADR 0210) — null limits and the refusal. Usage then comes from the <c>Sforce-Limit-Info</c> header of normal calls.
    /// Any other failure is thrown.
    /// </summary>
    public async Task<(ApiLimits? Limits, SalesforceError? Refused)> TryLimitsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await LimitsAsync(cancellationToken).ConfigureAwait(false), null);
        }
        catch (SourceConnectorException ex) when (ex.MessageKey == "integration.sync.errors.http" && ex.Error is { IsPermissionRefusal: true } refused)
        {
            return (null, refused);
        }
    }

    /// <summary>The cheapest REST call (the API version's resource list): used to read the usage header when <c>/limits</c> is refused.</summary>
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync(DataPath, $"GET /services/data/{options.ApiVersion}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The best known limits: <c>/limits</c> when it answered and no newer header was seen, else the header usage.</summary>
    public ApiLimits? CurrentLimits(ApiLimits? fromLimits) =>
        ApiUsage is { } usage && (fromLimits is null || usage.At >= fromLimits.At) ? usage.ToLimits() : fromLimits;

    public async Task<ApiLimits> LimitsAsync(CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"{DataPath}/limits", "GET /limits", cancellationToken).ConfigureAwait(false);
        (int?, int?) Of(string name) => json.RootElement.TryGetProperty(name, out var e)
            ? (e.TryGetProperty("Max", out var m) ? m.GetInt32() : null, e.TryGetProperty("Remaining", out var r) ? r.GetInt32() : null)
            : (null, null);
        var (apiMax, apiRemaining) = Of("DailyApiRequests");
        var (bulkMax, bulkRemaining) = Of("DailyBulkV2QueryJobs");
        return new ApiLimits(apiMax, apiRemaining, bulkMax, bulkRemaining, _time.GetUtcNow(), ApiLimitSources.Limits);
    }

    // ---- Queries ----

    public async Task<int> CountAsync(string soql, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"{DataPath}/queryAll?q={Uri.EscapeDataString(soql)}", Calls.Query(soql), cancellationToken).ConfigureAwait(false);
        return json.RootElement.GetProperty("totalSize").GetInt32();
    }

    /// <summary>REST <c>queryAll</c> (deleted rows included) with <c>nextRecordsUrl</c> paging.</summary>
    public async IAsyncEnumerable<SourceRecord> QueryAsync(string soql, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? path = $"{DataPath}/queryAll?q={Uri.EscapeDataString(soql)}";
        var call = Calls.Query(soql);
        while (path is not null)
        {
            using var json = await GetJsonAsync(path, call, cancellationToken, ("Sforce-Query-Options", $"batchSize={options.PageSize}")).ConfigureAwait(false);
            foreach (var row in json.RootElement.GetProperty("records").EnumerateArray())
            {
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                Flatten(row, null, values);
                yield return new SourceRecord(values);
            }

            path = json.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.False ? Str(json.RootElement, "nextRecordsUrl") : null;
            call = Calls.Query(soql) + " (next page)";
        }
    }

    /// <summary>Bulk API 2.0 <c>queryAll</c> job: create, poll until complete, read the CSV results page by page (<c>Sforce-Locator</c>).</summary>
    public async IAsyncEnumerable<SourceRecord> BulkQueryAsync(string soql, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var create = JsonSerializer.Serialize(new { operation = "queryAll", query = soql, contentType = "CSV", columnDelimiter = "COMMA", lineEnding = "LF" });
        string jobId;
        using (var request = new HttpRequestMessage(HttpMethod.Post, InstanceUrl + $"{DataPath}/jobs/query") { Content = new StringContent(create, Encoding.UTF8, "application/json") })
        using (var json = await SendJsonAsync(request, Calls.Bulk(soql), cancellationToken).ConfigureAwait(false))
        {
            jobId = json.RootElement.GetProperty("id").GetString()!;
        }

        for (var poll = 0; ; poll++)
        {
            using var state = await GetJsonAsync($"{DataPath}/jobs/query/{jobId}", $"GET /jobs/query/{jobId}", cancellationToken).ConfigureAwait(false);
            var value = Str(state.RootElement, "state");
            if (value == "JobComplete")
            {
                break;
            }

            if (value is "Failed" or "Aborted" || poll > 720)
            {
                throw new SourceConnectorException("integration.sync.errors.bulkFailed",
                    new SalesforceError(null, Calls.Bulk(soql), value, SalesforceError.Clean(Str(state.RootElement, "errorMessage"), SalesforceError.MaxMessage)));
            }

            await transport.DelayAsync(TimeSpan.FromSeconds(Math.Min(30, 2 + poll)), cancellationToken).ConfigureAwait(false);
        }

        string? locator = null;
        do
        {
            var path = $"{DataPath}/jobs/query/{jobId}/results?maxRecords={options.PageSize}" + (locator is null ? string.Empty : $"&locator={Uri.EscapeDataString(locator)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, InstanceUrl + path);
            using var response = await SendAsync(request, $"GET /jobs/query/{jobId}/results", cancellationToken).ConfigureAwait(false);
            var csv = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            locator = response.Headers.TryGetValues("Sforce-Locator", out var values) ? values.FirstOrDefault() : null;
            if (locator is "null" or "")
            {
                locator = null;
            }

            foreach (var row in Csv.Parse(csv))
            {
                yield return new SourceRecord(row);
            }
        }
        while (locator is not null);
    }

    /// <summary>Binary content of a file (<c>ContentVersion/{id}/VersionData</c>, <c>Attachment/{id}/Body</c>).</summary>
    public async Task<byte[]> DownloadAsync(string objectName, string id, string field, CancellationToken cancellationToken)
    {
        if (!ApiName().IsMatch(objectName) || !ApiName().IsMatch(field) || !SalesforceId().IsMatch(id))
        {
            throw new SourceConnectorException("integration.sync.errors.invalidObject", objectName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, InstanceUrl + $"{DataPath}/sobjects/{objectName}/{id}/{field}");
        using var response = await SendAsync(request, $"GET /sobjects/{objectName}/{id}/{field}", cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- HTTP ----

    private async Task<JsonDocument> GetJsonAsync(string path, string call, CancellationToken cancellationToken, (string Name, string Value)? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path.StartsWith("http", StringComparison.Ordinal) ? path : InstanceUrl + path);
        if (header is { } h)
        {
            request.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }

        return await SendJsonAsync(request, call, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, string call, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, call, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Authorised request with retries (network errors and 5xx), one token renewal on 401 and the read-only guard. <paramref name="call"/> is
    /// the sanitised description reported with a failure (ADR 0211).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string call, CancellationToken cancellationToken)
    {
        if (InstanceUrl is null || _token is null)
        {
            await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
            if (request.RequestUri is { } uri && !uri.IsAbsoluteUri)
            {
                request.RequestUri = new Uri(InstanceUrl + uri.OriginalString);
            }
        }

        var renewed = false;
        for (var attempt = 1; ; attempt++)
        {
            using var copy = await CloneAsync(request).ConfigureAwait(false);
            copy.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            HttpResponseMessage? response = null;
            try
            {
                response = await SendRawAsync(copy, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _ = ex;
                await transport.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new SourceConnectorException("integration.sync.errors.unreachable", ex.Message, ex,
                    new SalesforceError(null, call, ex.HttpRequestError.ToString(), SalesforceError.Clean(ex.Message, SalesforceError.MaxMessage)));
            }

            ReadUsage(response);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = response.StatusCode;
            response.Dispose();
            if (status == HttpStatusCode.Unauthorized && !renewed)
            {
                renewed = true;
                await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var error = SalesforceError.FromBody((int)status, call, body);
            if (error.ErrorCode == "REQUEST_LIMIT_EXCEEDED")
            {
                throw new SourceConnectorException("integration.sync.errors.apiLimit", error);
            }

            if ((int)status >= 500 && attempt < MaxAttempts)
            {
                await transport.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new SourceConnectorException("integration.sync.errors.http", error);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsReadOnlyRequest(request))
        {
            throw new InvalidOperationException("The Salesforce connector only reads (ADR 0201).");
        }

        ApiCalls++;
        return await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>GET, or POST to the token endpoint, or POST to create a Bulk API 2.0 <b>query</b> job. Nothing else ever leaves the connector.</summary>
    public static bool IsReadOnlyRequest(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.RequestUri?.IsAbsoluteUri == true ? request.RequestUri.AbsolutePath : request.RequestUri?.OriginalString ?? string.Empty;
        return request.Method == HttpMethod.Get
            || (request.Method == HttpMethod.Post && (path.EndsWith("/services/oauth2/token", StringComparison.Ordinal) || BulkJobPath().IsMatch(path)));
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            copy.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return copy;
    }

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string?> values)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("attributes"))
            {
                continue;
            }

            var name = prefix is null ? property.Name : prefix + "." + property.Name;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, name, values); // relationship fields: RecordType.DeveloperName, ProcessInstance.ProcessDefinition.Name
                    break;
                case JsonValueKind.Array:
                    break; // sub-queries are not used by the mapping engine
                case JsonValueKind.Null:
                    values[name] = null;
                    break;
                case JsonValueKind.String:
                    values[name] = property.Value.GetString();
                    break;
                default:
                    values[name] = property.Value.GetRawText();
                    break;
            }
        }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private void ReadUsage(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Sforce-Limit-Info", out var values) && ApiUsage.Parse(string.Join(',', values), _time.GetUtcNow()) is { } usage)
        {
            ApiUsage = usage;
        }
    }

    /// <summary>Sanitised call descriptions (no query string, no literals, no tokens).</summary>
    internal static class Calls
    {
        public const string Token = "POST /services/oauth2/token";

        public static string Query(string soql) => "GET /queryAll: " + Shape(soql);

        public static string Bulk(string soql) => "POST /jobs/query: " + Shape(soql);

        /// <summary><c>SELECT &lt;fields&gt; FROM &lt;object&gt;</c> only: the WHERE part (filter literals, Id lists) is dropped.</summary>
        public static string Shape(string soql)
        {
            var where = soql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
            var order = soql.IndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
            var end = where >= 0 ? where : order >= 0 ? order : soql.Length;
            var shape = soql[..end] + (where >= 0 ? " WHERE …" : string.Empty);
            return shape.Length > 1000 ? shape[..1000] + "…" : shape;
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,79}$", RegexOptions.CultureInvariant)]
    public static partial Regex ApiName();

    [GeneratedRegex("^[A-Za-z0-9]{15}([A-Za-z0-9]{3})?$", RegexOptions.CultureInvariant)]
    public static partial Regex SalesforceId();

    [GeneratedRegex("/services/data/v\\d{2,3}\\.0/jobs/query$", RegexOptions.CultureInvariant)]
    private static partial Regex BulkJobPath();

    /// <summary><c>api-usage=used/max</c>, not the <c>per-app-api-usage</c> part.</summary>
    [GeneratedRegex("(?:^|[;,\\s])api-usage=(\\d+)/(\\d+)", RegexOptions.CultureInvariant)]
    internal static partial Regex ApiUsageHeader();
}

/// <summary>OAuth 2.0 JWT bearer assertion (RS256), built with the platform's crypto (ADR 0201).</summary>
public static class SalesforceJwt
{
    public static string CreateAssertion(string clientId, string username, string audience, string privateKeyPem, DateTimeOffset now)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
        }
        catch (ArgumentException ex)
        {
            throw new SourceConnectorException("integration.sync.errors.invalidKey", null, ex);
        }

        var header = Base64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"u8.ToArray());
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = clientId,
            sub = username,
            aud = audience.TrimEnd('/'),
            exp = now.AddMinutes(3).ToUnixTimeSeconds(),
        }));
        var signingInput = header + "." + payload;
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + Base64Url(signature);
    }

    /// <summary>Checks a PEM private key and returns the SHA-256 thumbprint of its public key, or null when it is not a usable RSA key.</summary>
    public static string? Thumbprint(string privateKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            if (rsa.KeySize < 2048)
            {
                return null;
            }

            _ = rsa.ExportRSAPrivateKey(); // a public-only PEM throws here
            return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return null;
        }
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>RFC 4180 CSV as Bulk API 2.0 returns it (header row, quoted fields, LF / CRLF).</summary>
public static class Csv
{
    public static IEnumerable<Dictionary<string, string?>> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = ReadRows(text).ToList();
        if (rows.Count == 0)
        {
            yield break;
        }

        var header = rows[0];
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 1 && row[0].Length == 0)
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
            {
                var value = i < row.Count ? row[i] : null;
                values[header[i]] = string.IsNullOrEmpty(value) ? null : value;
            }

            yield return values;
        }
    }

    private static IEnumerable<List<string>> ReadRows(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
