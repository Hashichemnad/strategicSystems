using System.Security.Claims;

namespace Strategy.Platform.Reporting;

/// <summary>A counter on a My Work card. <paramref name="Tone"/>: neutral, info, success, warning or danger.</summary>
public sealed record MyWorkCount(string Key, int Value, string Tone = "neutral", string? Link = null);

/// <summary>One item on a card (deep link to the record).</summary>
public sealed record MyWorkCardItem(
    string ResourceType,
    Guid Id,
    string? Number,
    string? TitleAr,
    string? TitleEn,
    string MessageKey,
    string Link,
    DateOnly? DueDate = null,
    bool Overdue = false);

/// <summary>
/// A My Work card (SPEC §0.4 level 1, ADR 0149). <paramref name="TitleKey"/> is a translation key (web namespace
/// <c>reporting</c>, <c>myWork.cards.*</c>); each <see cref="MyWorkCount.Key"/> is a bare code that the web client looks up
/// under <c>reporting:myWork.counts.&lt;key&gt;</c> (e.g. <c>"overdue"</c>, not <c>"myWork.counts.overdue"</c>). Return no card when there is nothing relevant for the user in this system.
/// </summary>
public sealed record MyWorkCard(
    string Code,
    string SystemCode,
    string TitleKey,
    IReadOnlyList<MyWorkCount> Counts,
    IReadOnlyList<MyWorkCardItem> Items,
    string Link,
    int Sort);

/// <summary>Implemented per module (and by Workflow, Notifications, Reporting); called only when the user may open the system.</summary>
public interface IMyWorkCardSource
{
    string SystemCode { get; }

    Task<IReadOnlyList<MyWorkCard>> GetCardsAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}
