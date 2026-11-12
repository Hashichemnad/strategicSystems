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
public sealed class SourceConnectorException(string messageKey, string? detail = null, Exception? inner = null)
    : Exception(detail ?? messageKey, inner)
{
    public string MessageKey { get; } = messageKey;

    public string? Detail { get; } = detail;
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
            throw new SourceConnectorException("integration.sync.errors.authFailed", ErrorOf(body));
        }

        using var json = JsonDocument.Parse(body);
        _token = json.RootElement.GetProperty("access_token").GetString();
        InstanceUrl = json.RootElement.GetProperty("instance_url").GetString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(_token) || !Uri.TryCreate(InstanceUrl, UriKind.Absolute, out var instance) || instance.Scheme != Uri.UriSchemeHttps)
        {
            throw new SourceConnectorException("integration.sync.errors.authFailed", "invalid token response");
        }
    }

    // ---- Describe ----

    public async Task<IReadOnlyList<SourceObjectInfo>> ListObjectsAsync(CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"{DataPath}/sobjects", cancellationToken).ConfigureAwait(false);
        return [.. json.RootElement.GetProperty("sobjects").EnumerateArray().Select(o => new SourceObjectInfo(
            o.GetProperty("name").GetString()!, Str(o, "label") ?? string.Empty, Bool(o, "queryable"), Str(o, "keyPrefix"), Bool(o, "custom")))];
    }

    public async Task<SourceObjectDescribe> DescribeAsync(string objectName, CancellationToken cancellationToken)
    {
        if (!ApiName().IsMatch(objectName))
        {
            throw new SourceConnectorException("integration.sync.errors.invalidObject", objectName);
        }

        using var json = await GetJsonAsync($"{DataPath}/sobjects/{objectName}/describe", cancellationToken).ConfigureAwait(false);
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

    public async Task<ApiLimits> LimitsAsync(CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"{DataPath}/limits", cancellationToken).ConfigureAwait(false);
        (int?, int?) Of(string name) => json.RootElement.TryGetProperty(name, out var e)
            ? (e.TryGetProperty("Max", out var m) ? m.GetInt32() : null, e.TryGetProperty("Remaining", out var r) ? r.GetInt32() : null)
            : (null, null);
        var (apiMax, apiRemaining) = Of("DailyApiRequests");
        var (bulkMax, bulkRemaining) = Of("DailyBulkV2QueryJobs");
        return new ApiLimits(apiMax, apiRemaining, bulkMax, bulkRemaining, _time.GetUtcNow());
    }

    // ---- Queries ----

    public async Task<int> CountAsync(string soql, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"{DataPath}/queryAll?q={Uri.EscapeDataString(soql)}", cancellationToken).ConfigureAwait(false);
        return json.RootElement.GetProperty("totalSize").GetInt32();
    }

    /// <summary>REST <c>queryAll</c> (deleted rows included) with <c>nextRecordsUrl</c> paging.</summary>
    public async IAsyncEnumerable<SourceRecord> QueryAsync(string soql, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? path = $"{DataPath}/queryAll?q={Uri.EscapeDataString(soql)}";
        while (path is not null)
        {
            using var json = await GetJsonAsync(path, cancellationToken, ("Sforce-Query-Options", $"batchSize={options.PageSize}")).ConfigureAwait(false);
            foreach (var row in json.RootElement.GetProperty("records").EnumerateArray())
            {
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                Flatten(row, null, values);
                yield return new SourceRecord(values);
            }

            path = json.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.False ? Str(json.RootElement, "nextRecordsUrl") : null;
        }
    }

    /// <summary>Bulk API 2.0 <c>queryAll</c> job: create, poll until complete, read the CSV results page by page (<c>Sforce-Locator</c>).</summary>
    public async IAsyncEnumerable<SourceRecord> BulkQueryAsync(string soql, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var create = JsonSerializer.Serialize(new { operation = "queryAll", query = soql, contentType = "CSV", columnDelimiter = "COMMA", lineEnding = "LF" });
        string jobId;
        using (var request = new HttpRequestMessage(HttpMethod.Post, InstanceUrl + $"{DataPath}/jobs/query") { Content = new StringContent(create, Encoding.UTF8, "application/json") })
        using (var json = await SendJsonAsync(request, cancellationToken).ConfigureAwait(false))
        {
            jobId = json.RootElement.GetProperty("id").GetString()!;
        }

        for (var poll = 0; ; poll++)
        {
            using var state = await GetJsonAsync($"{DataPath}/jobs/query/{jobId}", cancellationToken).ConfigureAwait(false);
            var value = Str(state.RootElement, "state");
            if (value == "JobComplete")
            {
                break;
            }

            if (value is "Failed" or "Aborted" || poll > 720)
            {
                throw new SourceConnectorException("integration.sync.errors.bulkFailed", Str(state.RootElement, "errorMessage") ?? value);
            }

            await transport.DelayAsync(TimeSpan.FromSeconds(Math.Min(30, 2 + poll)), cancellationToken).ConfigureAwait(false);
        }

        string? locator = null;
        do
        {
            var path = $"{DataPath}/jobs/query/{jobId}/results?maxRecords={options.PageSize}" + (locator is null ? string.Empty : $"&locator={Uri.EscapeDataString(locator)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, InstanceUrl + path);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
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
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- HTTP ----

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken, (string Name, string Value)? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path.StartsWith("http", StringComparison.Ordinal) ? path : InstanceUrl + path);
        if (header is { } h)
        {
            request.Headers.TryAddWithoutValidation(h.Name, h.Value);
        }

        return await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Authorised request with retries (network errors and 5xx), one token renewal on 401 and the read-only guard.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
                throw new SourceConnectorException("integration.sync.errors.unreachable", ex.Message, ex);
            }

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

            if (body.Contains("REQUEST_LIMIT_EXCEEDED", StringComparison.Ordinal))
            {
                throw new SourceConnectorException("integration.sync.errors.apiLimit", ErrorOf(body));
            }

            if ((int)status >= 500 && attempt < MaxAttempts)
            {
                await transport.DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw new SourceConnectorException("integration.sync.errors.http", string.Create(CultureInfo.InvariantCulture, $"{(int)status} {ErrorOf(body)}"));
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

    private static string? ErrorOf(string body)
    {
        var text = body.Length > 300 ? body[..300] : body;
        return text.Replace('\n', ' ');
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,79}$", RegexOptions.CultureInvariant)]
    public static partial Regex ApiName();

    [GeneratedRegex("^[A-Za-z0-9]{15}([A-Za-z0-9]{3})?$", RegexOptions.CultureInvariant)]
    public static partial Regex SalesforceId();

    [GeneratedRegex("/services/data/v\\d{2,3}\\.0/jobs/query$", RegexOptions.CultureInvariant)]
    private static partial Regex BulkJobPath();
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
