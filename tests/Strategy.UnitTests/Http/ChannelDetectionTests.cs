using Microsoft.AspNetCore.Http;
using Strategy.Platform.Http.Context;
using Strategy.Platform.Kernel.Context;

namespace Strategy.UnitTests.Http;

public class ChannelDetectionTests
{
    [Theory]
    [InlineData("/mcp", null, Channels.Mcp)]
    [InlineData("/mcp/tools", "spa", Channels.Mcp)]
    [InlineData("/api/wasel/v1/projects", "spa", Channels.Wasel)]
    [InlineData("/api/v1/sample/items", "spa", Channels.Ui)]
    [InlineData("/api/v1/sample/items", null, Channels.Api)]
    [InlineData("/api/v1/sample/items", "curl", Channels.Api)]
    public void Detects_channel_from_route_and_client_header(string path, string? client, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (client is not null)
        {
            context.Request.Headers[RequestContextMiddleware.ClientHeader] = client;
        }

        RequestContextMiddleware.DetectChannel(context.Request).ShouldBe(expected);
    }
}
