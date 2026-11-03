using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Modules.Kpi.Application;

public sealed record GetKpiSettings : IQuery<SettingsDocument<KpiSettings>>;

public sealed record SaveKpiSettings(KpiSettings Settings, uint Version) : ICommand<SettingsDocument<KpiSettings>>;

/// <summary>Settings → KPI (<c>kpi.settings.manage</c>; read also by plan managers for the window rules).</summary>
internal sealed class KpiSettingsHandlers(ISettingsEditor editor, KpiReader reader, IRequestContext context)
    : IQueryHandler<GetKpiSettings, SettingsDocument<KpiSettings>>, ICommandHandler<SaveKpiSettings, SettingsDocument<KpiSettings>>
{
    public async Task<Result<SettingsDocument<KpiSettings>>> HandleAsync(GetKpiSettings query, CancellationToken cancellationToken)
    {
        if (!await reader.CanAsync(context.User, KpiPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            && !await reader.CanAsync(context.User, KpiPermissions.PlanManage, cancellationToken).ConfigureAwait(false))
        {
            return Error.Forbidden();
        }

        return await editor.GetDocumentAsync<KpiSettings>(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SettingsDocument<KpiSettings>>> HandleAsync(SaveKpiSettings command, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, KpiPermissions.SettingsManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.SaveAsync(command.Settings, command.Version, KpiSettings.Validate, cancellationToken).ConfigureAwait(false);
}
