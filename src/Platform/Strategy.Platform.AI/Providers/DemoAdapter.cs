using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Strategy.Platform.AI.Domain;

namespace Strategy.Platform.AI.Providers;

/// <summary>
/// An offline provider for development and demonstrations, registered only when <c>AI:EnableDemoProvider=true</c> (never in production
/// configuration). Deterministic: answers the connection test, fills form fields from "Label: value" lines of the document, writes a short
/// brief, marks translations, and answers questions with one search.
/// </summary>
internal sealed class DemoAdapter : IAiProviderAdapter
{
    public string Type => AiProviderTypes.Demo;

    public bool RequiresEndpoint => false;

    public bool RequiresApiKey => false;

    public IChatClient Create(ProviderConnection connection) => new DemoChatClient(connection.Model);

    public Task<IReadOnlyList<string>> ListModelsAsync(ProviderConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["demo-small", "demo-large"]);
}

internal sealed partial class DemoChatClient(string model) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var system = list.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? string.Empty;
        var user = string.Join('\n', list.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        string text;
        if (system.Contains("You fill in the form", StringComparison.Ordinal))
        {
            text = Extract(system, user);
        }
        else if (system.Contains("You help an approver", StringComparison.Ordinal))
        {
            var arabic = system.Contains("Write in Arabic", StringComparison.Ordinal);
            var title = TitleLine().Match(user) is { Success: true } m ? m.Groups[1].Value.Trim() : "—";
            var changed = user.Contains("No field changed.", StringComparison.Ordinal) || user.Contains("First submission.", StringComparison.Ordinal) ? 0 : 1;
            text = JsonSerializer.Serialize(new
            {
                summary = arabic
                    ? $"ملخص تجريبي: يطلب السجل «{title}» الاعتماد. راجع البيانات والمرفقات قبل اتخاذ القرار."
                    : $"Demo brief: the record “{title}” is waiting for approval. Check its values and attachments before you decide.",
                changes = changed == 0 ? Array.Empty<string>() : [arabic ? "تم تعديل بعض الحقول منذ التقديم السابق." : "Some fields changed since the previous submission."],
                attentionPoints = new[] { arabic ? "تحقق من المبلغ المطلوب." : "Check the requested amount." },
            });
        }
        else if (system.Contains("You translate", StringComparison.Ordinal))
        {
            var toArabic = system.Contains("to Arabic", StringComparison.Ordinal);
            var source = Document().Match(user) is { Success: true } d ? d.Groups[1].Value.Trim() : string.Empty;
            text = JsonSerializer.Serialize(new { translation = (toArabic ? "(ترجمة تجريبية) " : "(demo translation) ") + source });
        }
        else if (options?.Tools is { Count: > 0 } && !list.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
        {
            var question = list.Last(m => m.Role == ChatRole.User).Text;
            var call = new FunctionCallContent("demo-1", "search_records", new Dictionary<string, object?> { ["query"] = LastWord(question) });
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])) { ModelId = model, Usage = Usage(question, string.Empty) });
        }
        else
        {
            var results = list.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Select(r => r.Result?.ToString() ?? string.Empty).LastOrDefault() ?? string.Empty;
            var count = Regex.Count(results, "\"link\"", RegexOptions.None, TimeSpan.FromSeconds(1));
            text = system.Contains("Answer in Arabic", StringComparison.Ordinal)
                ? $"إجابة تجريبية: وجدت {count} من السجلات المطابقة."
                : $"Demo answer: I found {count} matching record(s).";
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = model, Usage = Usage(system + user, text) });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>"Label: value" lines whose label is a field's English or Arabic label (or name) become proposals quoting that line.</summary>
    private static string Extract(string system, string user)
    {
        var document = Document().Match(user) is { Success: true } d ? d.Groups[1].Value : string.Empty;
        var lines = document.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(':', StringComparison.Ordinal)).ToList();
        var fields = new JsonArray();
        foreach (Match field in FieldLine().Matches(system))
        {
            var (name, labels) = (field.Groups[1].Value, field.Groups[2].Value.Split(" / ").Select(l => l.Split(" — ")[0].Trim()).Append(field.Groups[1].Value));
            // The label may follow scanner noise on an OCR line, so it is searched anywhere in the line.
            var match = lines.Select(l => (Line: l, At: labels.Select(label => (label, i: l.IndexOf(label + ":", StringComparison.OrdinalIgnoreCase)))
                    .Where(x => x.i >= 0).Select(x => x.i + x.label.Length + 1).DefaultIfEmpty(-1).First()))
                .FirstOrDefault(x => x.At >= 0);
            if (match.Line is null)
            {
                continue;
            }

            var line = match.Line;
            var value = line[match.At..].Trim();
            fields.Add(new JsonObject { ["name"] = name, ["value"] = value, ["confidence"] = 0.9, ["source"] = line.Length > 300 ? line[..300] : line });
        }

        var arabic = document.Count(c => c is >= '؀' and <= 'ۿ');
        return new JsonObject { ["language"] = arabic > document.Length / 4 ? "ar" : "en", ["fields"] = fields }.ToJsonString();
    }

    private static string LastWord(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('?', '؟', '.', '!')).LastOrDefault(w => w.Length > 2) ?? text;

    private static UsageDetails Usage(string input, string output) =>
        new() { InputTokenCount = Math.Max(1, Encoding.UTF8.GetByteCount(input) / 4), OutputTokenCount = Math.Max(1, Encoding.UTF8.GetByteCount(output) / 4) };

    [GeneratedRegex(@"<untrusted_document[^>]*>\n?(.*?)\n?</untrusted_document", RegexOptions.Singleline)]
    private static partial Regex Document();

    [GeneratedRegex(@"^- (\w+) \([^)]*\): (.+)$", RegexOptions.Multiline)]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"Record [^:]*: (.+)")]
    private static partial Regex TitleLine();
}
