using System.Net.Http.Json;
using System.Text.Json;

record NetlifyConfig
{
    public required string AccessToken { get; init; }
    public required string SiteId { get; init; }
    public required Uri BuildHookUrl { get; init; }
}

class NetlifyClient(NetlifySharp.NetlifyClient netlify, NetlifyConfig config)
{
    public async Task<Portfolio> GetDeployedPortfolio(CancellationToken ct)
    {
        var site = await netlify.GetSiteAsync(config.SiteId, ct);

        var portfolioJsonUrl = new Uri(new Uri(site.PublishedDeploy.SslUrl), "/portfolio.json");

        using var client = new HttpClient();

        Log.Info($"fetch deployed portfolio.json from {portfolioJsonUrl}");
        try
        {
            return await client.GetFromJsonAsync<Portfolio>(portfolioJsonUrl, ct)
                ?? throw new CommandException("failed to get deployed portfolio json");
        }
        catch (JsonException)
        {
            return new Portfolio { Photos = [] };
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new Portfolio { Photos = [] };
        }
    }
}