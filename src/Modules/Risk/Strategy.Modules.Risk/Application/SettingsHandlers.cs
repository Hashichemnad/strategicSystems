using Strategy.Platform.Kernel.Context;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;
using Strategy.Platform.Settings;
using Strategy.Platform.Settings.Application;

namespace Strategy.Modules.Risk.Application;

public sealed record GetRiskSettings : IQuery<SettingsDocument<RiskSettings>>;

public sealed record SaveRiskSettings(RiskSettings Settings, uint Version) : ICommand<SettingsDocument<RiskSettings>>;

/// <summary>Settings → Risk (<c>risk.framework.manage</c>).</summary>
internal sealed class RiskSettingsHandlers(ISettingsEditor editor, RiskReader reader, IRequestContext context)
    : IQueryHandler<GetRiskSettings, SettingsDocument<RiskSettings>>, ICommandHandler<SaveRiskSettings, SettingsDocument<RiskSettings>>
{
    public async Task<Result<SettingsDocument<RiskSettings>>> HandleAsync(GetRiskSettings query, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, RiskPermissions.FrameworkManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.GetDocumentAsync<RiskSettings>(cancellationToken).ConfigureAwait(false);

    public async Task<Result<SettingsDocument<RiskSettings>>> HandleAsync(SaveRiskSettings command, CancellationToken cancellationToken) =>
        !await reader.CanAsync(context.User, RiskPermissions.FrameworkManage, cancellationToken).ConfigureAwait(false)
            ? Error.Forbidden()
            : await editor.SaveAsync(command.Settings, command.Version, RiskSettings.Validate, cancellationToken).ConfigureAwait(false);
}
