using System.Net;
using Strategy.Platform.Kernel.Net;

namespace Strategy.UnitTests.Kernel;

/// <summary>ADR 0155: server-side request forgery guard for administrator-entered URLs.</summary>
public class OutboundUrlPolicyTests
{
    private static readonly OutboundUrlPolicy Default = new(new OutboundOptions());
    private static readonly OutboundUrlPolicy PublicOnly = new(new OutboundOptions(AllowPrivateNetworks: false));
    private static readonly OutboundUrlPolicy WithLoopback = new(new OutboundOptions(AllowLoopback: true));

    [Theory]
    [InlineData("https://api.anthropic.com/v1", OutboundRefusal.None)]
    [InlineData("http://ollama:11434/v1", OutboundRefusal.None)]
    [InlineData("https://10.1.2.3/v1", OutboundRefusal.None)]
    [InlineData("https://192.168.10.5", OutboundRefusal.None)]
    [InlineData("http://169.254.169.254/latest/meta-data/", OutboundRefusal.LinkLocal)]
    [InlineData("http://[fe80::1]/", OutboundRefusal.LinkLocal)]
    [InlineData("http://127.0.0.1:5432", OutboundRefusal.Loopback)]
    [InlineData("http://[::1]/", OutboundRefusal.Loopback)]
    [InlineData("http://[::ffff:127.0.0.1]/", OutboundRefusal.Loopback)]
    [InlineData("http://localhost:8080/api", OutboundRefusal.Loopback)]
    [InlineData("http://app.localhost/", OutboundRefusal.Loopback)]
    [InlineData("http://0.0.0.0/", OutboundRefusal.Reserved)]
    [InlineData("http://224.0.0.1/", OutboundRefusal.Reserved)]
    [InlineData("http://255.255.255.255/", OutboundRefusal.Reserved)]
    [InlineData("file:///etc/passwd", OutboundRefusal.Scheme)]
    [InlineData("gopher://example.org/", OutboundRefusal.Scheme)]
    [InlineData("ftp://example.org/", OutboundRefusal.Scheme)]
    [InlineData("https://user:secret@example.org/", OutboundRefusal.Credentials)]
    [InlineData("not a url", OutboundRefusal.InvalidUrl)]
    [InlineData("", OutboundRefusal.InvalidUrl)]
    [InlineData(null, OutboundRefusal.InvalidUrl)]
    public void Default_policy_allows_public_and_private_hosts_and_refuses_loopback_link_local_and_odd_schemes(string? url, OutboundRefusal expected) =>
        Default.Check(url).ShouldBe(expected);

    [Theory]
    [InlineData("https://10.0.0.5/", OutboundRefusal.PrivateNetwork)]
    [InlineData("https://172.16.0.1/", OutboundRefusal.PrivateNetwork)]
    [InlineData("https://172.31.255.255/", OutboundRefusal.PrivateNetwork)]
    [InlineData("https://172.32.0.1/", OutboundRefusal.None)]
    [InlineData("https://192.168.1.1/", OutboundRefusal.PrivateNetwork)]
    [InlineData("https://100.64.0.1/", OutboundRefusal.PrivateNetwork)]
    [InlineData("https://[fd00::1]/", OutboundRefusal.PrivateNetwork)]
    [InlineData("https://8.8.8.8/", OutboundRefusal.None)]
    [InlineData("https://[2001:4860:4860::8888]/", OutboundRefusal.None)]
    public void Public_only_policy_also_refuses_private_ranges(string url, OutboundRefusal expected) => PublicOnly.Check(url).ShouldBe(expected);

    [Fact]
    public void Loopback_is_allowed_only_when_configured_and_link_local_never()
    {
        WithLoopback.Check("http://localhost:11434/v1").ShouldBe(OutboundRefusal.None);
        WithLoopback.Check("http://127.0.0.1/").ShouldBe(OutboundRefusal.None);
        WithLoopback.Check("http://169.254.169.254/").ShouldBe(OutboundRefusal.LinkLocal);
    }

    [Theory]
    [InlineData("ldap.corp.local", OutboundRefusal.None)]
    [InlineData("10.0.0.10", OutboundRefusal.None)]
    [InlineData("169.254.169.254", OutboundRefusal.LinkLocal)]
    [InlineData("127.0.0.1", OutboundRefusal.Loopback)]
    [InlineData("  ", OutboundRefusal.InvalidUrl)]
    public void Host_names_and_literal_addresses_are_checked(string host, OutboundRefusal expected) => Default.CheckHost(host).ShouldBe(expected);

    [Fact]
    public async Task The_guarded_handler_refuses_a_forbidden_address_at_connect_time_and_follows_no_redirects()
    {
        using var handler = Default.CreateHandler(TimeSpan.FromSeconds(2));
        handler.AllowAutoRedirect.ShouldBeFalse();
        using var client = new HttpClient(handler);
        var failure = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://127.0.0.1:9/"), TestContext.Current.CancellationToken));
        failure.InnerException.ShouldBeOfType<OutboundRefusedException>().Reason.ShouldBe(OutboundRefusal.Loopback);

        // A host name that resolves to loopback is refused too (the check is on the resolved address, not the text).
        var named = await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://localhost:9/"), TestContext.Current.CancellationToken));
        named.InnerException.ShouldBeOfType<OutboundRefusedException>().Reason.ShouldBe(OutboundRefusal.Loopback);
    }

    [Fact]
    public void Mapped_v4_addresses_are_judged_as_v4() =>
        PublicOnly.CheckAddress(IPAddress.Parse("::ffff:10.0.0.1")).ShouldBe(OutboundRefusal.PrivateNetwork);
}
