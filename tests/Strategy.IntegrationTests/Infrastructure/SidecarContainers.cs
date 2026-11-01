using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>
/// Mailpit (SMTP catcher with an HTTP API) and Gotenberg (HTML → PDF with Chromium), as in deploy/docker-compose.yml.
/// Images come from Docker Hub (<c>TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX=mirror.gcr.io/</c> behind a rate limit).
/// </summary>
public sealed class SidecarContainers(PostgresFixture postgres) : IAsyncLifetime
{
    private IContainer? _mailpit;
    private IContainer? _gotenberg;

    public string MailpitHost => _mailpit!.Hostname;

    public int MailpitSmtpPort => _mailpit!.GetMappedPublicPort(1025);

    public Uri MailpitApi => new($"http://{_mailpit!.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/");

    public Uri GotenbergUrl => new($"http://{_gotenberg!.Hostname}:{_gotenberg.GetMappedPublicPort(3000)}/");

    public Task<string> CreateDatabaseAsync() => postgres.CreateDatabaseAsync();

    public async ValueTask InitializeAsync()
    {
        _mailpit = new ContainerBuilder("axllent/mailpit:v1.29")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
            .Build();
        _gotenberg = new ContainerBuilder("gotenberg/gotenberg:8")
            .WithPortBinding(3000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3000).ForPath("/health")))
            .Build();
        await Task.WhenAll(_mailpit.StartAsync(), _gotenberg.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        if (_mailpit is not null)
        {
            await _mailpit.DisposeAsync();
        }

        if (_gotenberg is not null)
        {
            await _gotenberg.DisposeAsync();
        }
    }
}
