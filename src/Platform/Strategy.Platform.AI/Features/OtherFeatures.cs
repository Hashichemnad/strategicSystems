using System.Text;
using System.Text.Json;
using Strategy.Platform.Access;
using Strategy.Platform.AI.Application;
using Strategy.Platform.Audit;
using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Kernel.Time;
using Strategy.Platform.Settings;
using Strategy.Platform.Workflow;

namespace Strategy.Platform.AI.Features;

public sealed record ChangedFieldDto(string Field, string LabelAr, string LabelEn, string? Before, string? After);

/// <summary>An approver brief: the AI paragraph and points (drafts), plus the changes computed from the audit trail (facts).</summary>
public sealed record ApproverBriefDto(
    string ResourceType, Guid ResourceId, int Attempt, string Summary, IReadOnlyList<string> Changes, IReadOnlyList<string> AttentionPoints,
    IReadOnlyList<ChangedFieldDto> ChangedFields, string Model, DateTimeOffset GeneratedAt, Guid UsageId);

/// <summary>Approver brief (docs/design/ai.md §4.2).</summary>
public sealed class BriefService(
    IEnumerable<IAiRecordSource> sources, IApprovalEngine approvals, IAuditQuery auditQuery, ISystemState systems, ISettingsStore settings, AiGateway gateway,
    IRequestContext context, IClock clock)
{
    private static readonly JsonElement OutputSchema = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["summary","changes","attentionPoints"],
         "properties":{
           "summary":{"type":"string","maxLength":1500},
           "changes":{"type":"array","maxItems":10,"items":{"type":"string","maxLength":300}},
           "attentionPoints":{"type":"array","maxItems":6,"items":{"type":"string","maxLength":300}}}}
        """).RootElement.Clone();

    public async Task<Result<ApproverBriefDto>> BriefAsync(string resourceType, Guid resourceId, CancellationToken ct)
    {
        var ready = await gateway.PrepareAsync(AiFeatures.Brief, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var source = sources.FirstOrDefault(s => s.ResourceType == resourceType);
        var record = source is null ? null : await source.GetAsync(resourceId, context.User, ct).ConfigureAwait(false);
        if (record is null || !await systems.IsEnabledAsync(record.SystemCode, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        var attempts = await approvals.ListAttemptsAsync(resourceType, resourceId, ct).ConfigureAwait(false);
        if (attempts.Count == 0)
        {
            return Error.Rule("ai.brief.noApproval", "ai.errors.briefNoApproval");
        }

        var changed = attempts.Count > 1
            ? await ChangesAsync(record, attempts[1].Instance.SubmittedAt, ct).ConfigureAwait(false)
            : [];
        var sharing = (await settings.GetAsync<AiSettings>(ct).ConfigureAwait(false)).Sharing.System(record.SystemCode);
        var content = new StringBuilder();
        content.Append("Record ").Append(record.Number).Append(": ").Append(record.TitleEn ?? record.TitleAr).Append('\n');
        foreach (var field in record.Fields.Where(f => !sharing.Excludes(f.Name)))
        {
            content.Append(field.LabelEn).Append(": ").Append(field.Value ?? "—").Append('\n');
        }

        var history = new StringBuilder();
        foreach (var attempt in attempts.Take(3))
        {
            history.Append("Submission ").Append(attempt.Instance.Attempt).Append(" (").Append(attempt.Instance.Status).Append(')');
            if (!string.IsNullOrWhiteSpace(attempt.SubmitComment))
            {
                history.Append(", submitter comment: ").Append(attempt.SubmitComment);
            }

            history.Append('\n');
            foreach (var decision in attempt.Decisions)
            {
                history.Append("  ").Append(decision.StepNameEn).Append(": ").Append(decision.Outcome);
                if (!string.IsNullOrWhiteSpace(decision.Comment))
                {
                    history.Append(" — ").Append(decision.Comment);
                }

                history.Append('\n');
            }
        }

        var changes = new StringBuilder();
        foreach (var change in changed.Where(c => !sharing.Excludes(c.Field)))
        {
            changes.Append(change.LabelEn).Append(": \"").Append(change.Before ?? "—").Append("\" → \"").Append(change.After ?? "—").Append("\"\n");
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            AiFeatures.Brief,
            new Dictionary<string, string> { ["attempt"] = attempts[0].Instance.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            [new AiDocument("record", content.ToString()), new AiDocument("history", history.ToString()),
                new AiDocument("changes", changes.Length == 0 ? (attempts.Count > 1 ? "No field changed." : "First submission.") : changes.ToString())],
            OutputSchema, record.SystemCode, record.People, resourceType, resourceId.ToString()), ct).ConfigureAwait(false);
        if (call.IsFailure)
        {
            return call.Error;
        }

        var output = call.Value.Output;
        return new ApproverBriefDto(resourceType, resourceId, attempts[0].Instance.Attempt, output.GetProperty("summary").GetString()!, Strings(output, "changes"),
            Strings(output, "attentionPoints"), changed, call.Value.Model, clock.UtcNow, call.Value.UsageId);
    }

    /// <summary>Fields whose value differs between the previous submission and now, from the audit trail (never from the model).</summary>
    private async Task<IReadOnlyList<ChangedFieldDto>> ChangesAsync(AiRecordContent record, DateTimeOffset previousSubmission, CancellationToken ct)
    {
        var before = await auditQuery.AsOfAsync(record.EntityType, record.Id.ToString(), previousSubmission, ct).ConfigureAwait(false);
        var now = await auditQuery.AsOfAsync(record.EntityType, record.Id.ToString(), clock.UtcNow, ct).ConfigureAwait(false);
        string? Value(RecordSnapshotDto snapshot, string field) =>
            snapshot.Fields.FirstOrDefault(f => string.Equals(f.Key, field, StringComparison.OrdinalIgnoreCase)).Value;
        return record.Fields
            .Select(f => new ChangedFieldDto(f.Name, f.LabelAr, f.LabelEn, Value(before, f.Name), Value(now, f.Name)))
            .Where(c => !string.Equals(c.Before, c.After, StringComparison.Ordinal))
            .ToList();
    }

    private static List<string> Strings(JsonElement output, string name) =>
        output.TryGetProperty(name, out var items) ? items.EnumerateArray().Select(i => i.GetString()!).ToList() : [];
}

public sealed record TranslateInput(string? Text, string? From, string? To, string? SystemCode);

public sealed record TranslationDto(string Translation, string From, string To, Guid UsageId);

/// <summary>AR ⇄ EN translation of a text field (the result is a draft the user accepts).</summary>
public sealed class TranslationService(AiGateway gateway)
{
    public const int MaxLength = 4000;

    private static readonly JsonElement OutputSchema = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["translation"],"properties":{"translation":{"type":"string","maxLength":12000}}}
        """).RootElement.Clone();

    public async Task<Result<TranslationDto>> TranslateAsync(TranslateInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > MaxLength)
        {
            errors.Add(new FieldError("text", "validation.maximumLength", "validation.maximumLength", new Dictionary<string, object?> { ["maxLength"] = MaxLength }));
        }

        if (input.From is not ("ar" or "en") || input.To is not ("ar" or "en") || input.From == input.To)
        {
            errors.Add(new FieldError("to", "validation.unknownCode", "validation.unknownCode"));
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var call = await gateway.CompleteJsonAsync(new AiJsonRequest(
            AiFeatures.Translate,
            new Dictionary<string, string> { ["from"] = Name(input.From!), ["to"] = Name(input.To!) },
            [new AiDocument("text", input.Text!)],
            OutputSchema,
            input.SystemCode), ct).ConfigureAwait(false);
        return call.IsFailure
            ? call.Error
            : new TranslationDto(call.Value.Output.GetProperty("translation").GetString()!.Trim(), input.From!, input.To!, call.Value.UsageId);
    }

    private static string Name(string code) => code == "ar" ? "Arabic" : "English";
}

public sealed record SimilarInput(string? Text, Guid? ExcludeId);

/// <summary>Similar records without a model: full-text + trigram through the module's search source, within the user's scope.</summary>
public sealed class SimilarService(IEnumerable<IRecordSearchSource> sources, IAccessService access, ISystemState systems, AiGateway gateway, IRequestContext context)
{
    public async Task<Result<IReadOnlyList<RecordSearchHit>>> FindAsync(string resourceType, SimilarInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ready = await gateway.PrepareAsync(AiFeatures.Similar, createClient: false, ct).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            return ready.Error;
        }

        var source = sources.FirstOrDefault(s => s.ResourceType == resourceType);
        if (source is null || !await systems.IsEnabledAsync(source.SystemCode, ct).ConfigureAwait(false))
        {
            return Error.NotFound();
        }

        if (!await access.CanAsync(context.User, source.ReadPermission, cancellationToken: ct).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        var text = input.Text?.Trim() ?? string.Empty;
        if (text.Length < 3)
        {
            return Array.Empty<RecordSearchHit>();
        }

        var hits = await source.SimilarAsync(text.Length > 500 ? text[..500] : text, input.ExcludeId, context.User, 5, ct).ConfigureAwait(false);
        await gateway.RecordAsync(ready.Value, resourceType, input.ExcludeId?.ToString(), true, null, 0, 0, 0, ct).ConfigureAwait(false);
        return hits.ToList();
    }
}
