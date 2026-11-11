using Microsoft.EntityFrameworkCore;
using Strategy.Modules.Portfolio.Contracts;
using Strategy.Modules.Portfolio.Domain;
using Strategy.Platform.Kernel.Messaging;

namespace Strategy.Modules.Portfolio.Application;

/// <summary>Publishes <see cref="ProjectStageChanged"/> when a lifecycle action moved the project's stage (Phase 22 webhooks, ADR 0194).</summary>
internal static class ProjectStageEvents
{
    /// <summary>In the caller's transaction (<paramref name="db"/>), so the event exists exactly when the change commits.</summary>
    public static async Task PublishIfChangedAsync(
        IIntegrationEventPublisher events, Project project, string fromStage, DateTimeOffset at, DbContext db, CancellationToken cancellationToken)
    {
        if (project.Stage == fromStage)
        {
            return;
        }

        await events.PublishAsync(new ProjectStageChanged(project.Id, project.Number, fromStage, project.Stage, project.UnitId, at), db, cancellationToken)
            .ConfigureAwait(false);
    }
}
