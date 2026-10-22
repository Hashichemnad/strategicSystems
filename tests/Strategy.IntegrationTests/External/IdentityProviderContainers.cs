using System.DirectoryServices.Protocols;
using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Strategy.IntegrationTests.Infrastructure;

namespace Strategy.IntegrationTests.External;

/// <summary>
/// Real identity providers in containers for the external sign-in tests:
/// Keycloak 26 (OpenID Connect, an OAuth client standing in for UAE Pass, SAML 2.0) and OpenLDAP.
/// Images come from Docker Hub (set <c>TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX=mirror.gcr.io/</c> behind a rate limit).
/// When Docker is not available these tests fail with the container error rather than being skipped.
/// </summary>
public sealed class IdentityProviderContainers(PostgresFixture postgres) : IAsyncLifetime
{
    public const string Realm = "strategy-test";
    public const string KeycloakPassword = "Kc#Password2026";
    public const string LdapPassword = "Ldap#Password2026";
    public const string LdapAdminPassword = "ldap-admin-secret";

    private IContainer _keycloak = null!;
    private IContainer _ldap = null!;

    public ApiFactory Factory { get; private set; } = null!;

    public string KeycloakBase => $"http://localhost:{_keycloak.GetMappedPublicPort(8080)}";

    public string RealmUrl => $"{KeycloakBase}/realms/{Realm}";

    public int LdapPort => _ldap.GetMappedPublicPort(389);

    public async ValueTask InitializeAsync()
    {
        var resources = Path.Combine(AppContext.BaseDirectory, "External", "Resources");
        _keycloak = new ContainerBuilder("keycloak/keycloak:26.0")
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
            .WithResourceMapping(await File.ReadAllBytesAsync(Path.Combine(resources, "strategy-test-realm.json")),
                "/opt/keycloak/data/import/strategy-test-realm.json")
            .WithCommand("start-dev", "--import-realm")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(8080).ForPath($"/realms/{Realm}/.well-known/openid-configuration").ForStatusCode(HttpStatusCode.OK)))
            .Build();
        _ldap = new ContainerBuilder("osixia/openldap:1.5.0")
            .WithPortBinding(389, assignRandomHostPort: true)
            .WithEnvironment("LDAP_ORGANISATION", "Example")
            .WithEnvironment("LDAP_DOMAIN", "example.test")
            .WithEnvironment("LDAP_ADMIN_PASSWORD", LdapAdminPassword)
            .WithEnvironment("LDAP_TLS", "false")
            .WithResourceMapping(await File.ReadAllBytesAsync(Path.Combine(resources, "people.ldif")),
                "/container/service/slapd/assets/config/bootstrap/ldif/custom/50-people.ldif")
            .WithCommand("--copy-service")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(389))
            .Build();

        await Task.WhenAll(_keycloak.StartAsync(), _ldap.StartAsync());
        await WaitForLdapAsync();
        Factory = await ApiFactory.CreateAsync(postgres);
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Task.WhenAll(_keycloak.DisposeAsync().AsTask(), _ldap.DisposeAsync().AsTask());
    }

    /// <summary>The seeded entries are loaded after the port opens; wait until a user can be found.</summary>
    private async Task WaitForLdapAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                using var connection = new LdapConnection(new LdapDirectoryIdentifier("localhost", LdapPort)) { AuthType = AuthType.Basic };
                connection.SessionOptions.ProtocolVersion = 3;
                connection.Bind(new NetworkCredential("cn=admin,dc=example,dc=test", LdapAdminPassword));
                var response = (SearchResponse)connection.SendRequest(new SearchRequest("ou=people,dc=example,dc=test", "(uid=ldap.user)", SearchScope.Subtree));
                if (response.Entries.Count == 1)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is LdapException or DirectoryOperationException && DateTime.UtcNow < deadline)
            {
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("OpenLDAP did not load the test entries in time.");
            }

            await Task.Delay(1000);
        }
    }
}
