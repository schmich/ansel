using System.Text.Json;

record NetlifyConfig
{
    public required string AccessToken { get; init; }
    public required string SiteId { get; init; }
    public required Uri BuildHookUrl { get; init; }
}

class NetlifyClient(NetlifySharp.NetlifyClient netlify, NetlifyConfig config)
{
    public async Task<Manifest> GetDeployedManifest(CancellationToken ct)
    {
        var site = await netlify.GetSiteAsync(config.SiteId, ct);
        var siteUrl = new Uri(site.PublishedDeploy.SslUrl);
        var manifestUrl = new Uri(siteUrl, "/.ansel/manifest.json.gz");

        Log.Info($"fetch deployed manifest.json.gz from {manifestUrl}");
        try
        {
            using var client = new HttpClient();
            using var stream = await client.GetStreamAsync(manifestUrl, ct);
            return ManifestSerializer.LoadGzip(stream);
        }
        catch (JsonException)
        {
            Log.Warn("malformed manifest.json.gz, using empty manifest");
            return new Manifest { Photos = [] };
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Log.Warn("http 404 for manifest.json.gz, using empty manifest");
            return new Manifest { Photos = [] };
        }
    }

    public async Task RequestBuild(CancellationToken ct)
    {
        Log.Info($"post {config.BuildHookUrl}");

        using var client = new HttpClient();
        var content = new StringContent("{}");
        await client.PostAsync(config.BuildHookUrl, content, ct);
    }
}