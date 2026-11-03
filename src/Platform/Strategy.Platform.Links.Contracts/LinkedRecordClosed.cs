using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Platform.Links;

/// <summary>
/// Integration event (ADR 0043): a record other systems link to reached its closing point — e.g. Portfolio publishes it with reason
/// <c>closure-approved</c> when a project's closure is approved. Teams opens the evaluation of the linked project teams.
/// </summary>
/// <param name="ResourceType">Linkable type of the closed record, e.g. <c>portfolio.project</c>.</param>
/// <param name="ResourceId">The record.</param>
/// <param name="Reason">Why it closed (code), e.g. <c>closure-approved</c>, <c>completed</c>, <c>cancelled</c>.</param>
/// <param name="ClosedAt">When.</param>
public sealed record LinkedRecordClosed(string ResourceType, Guid ResourceId, string Reason, DateTimeOffset ClosedAt) : IIntegrationEvent;
