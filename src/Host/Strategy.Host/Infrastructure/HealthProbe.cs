namespace Strategy.Host.Infrastructure;

/// <summary>Minimal HTTP probe used as the container healthcheck (<c>Strategy.Host --health-check</c>).</summary>
internal static class HealthProbe
{
    public static async Task<int> RunAsync(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await http.GetAsync(new Uri(url)).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
