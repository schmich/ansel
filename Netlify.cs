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
        var portfolioUrl = new Uri(new Uri(site.PublishedDeploy.SslUrl), "/portfolio.json.gz");

        Log.Info($"fetch deployed portfolio.json.gz from {portfolioUrl}");
        try
        {
            using var client = new HttpClient();
            using var stream = await client.GetStreamAsync(portfolioUrl, ct);
            return Portfolio.FromGzip(stream);
        }
        catch (JsonException)
        {
            Log.Warn("malformed portfolio.json.gz, using empty portfolio");
            return new Portfolio { Photos = [] };
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Log.Warn("http 404 for portfolio.json.gz, using empty portfolio");
            return new Portfolio { Photos = [] };
        }
    }
}