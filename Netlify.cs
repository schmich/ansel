using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text.Json;
using Tomlyn;

record NetlifyConfig
{
    public required string AccessToken { get; init; }
    public required string SiteId { get; init; }
    public required Uri BuildHookUrl { get; init; }
}

class NetlifyClient(NetlifySharp.NetlifyClient netlify, NetlifyConfig config)
{
    public static readonly string PublicManifestPath = "ansel/manifest.json.gz";

    public async Task<Manifest> GetDeployedManifest(CancellationToken ct)
    {
        var site = await netlify.GetSiteAsync(config.SiteId, ct);
        var siteUrl = new Uri(site.PublishedDeploy.SslUrl);
        var manifestUrl = new Uri(siteUrl, PublicManifestPath);

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

record NetlifySiteConfig
{
    [DataMember(Name = "ansel")]
    public AnselConfig? Ansel { get; init; }

    [DataMember(Name = "build")]
    public BuildConfig? Build { get; init; }
}

record AnselConfig
{
    [DataMember(Name = "build")]
    public string[]? Build { get; init; }
}

record BuildConfig
{
    [DataMember(Name = "publish")]
    public string? Publish { get; init; }
}

class SiteBuilder
{
    public async Task Build(string templateDir, Manifest manifest, CancellationToken ct)
    {
        templateDir = Path.GetFullPath(templateDir);
        if (!Directory.Exists(templateDir))
        {
            throw new Exception($"template dir does not exist at {templateDir}");
        }

        var netlifyTomlFileName = Path.GetFullPath(Path.Join(templateDir, "netlify.toml"));
        if (!File.Exists(netlifyTomlFileName))
        {
            throw new Exception($"netlify.toml does not exist at {netlifyTomlFileName}");
        }

        Log.Info($"using netlify.toml at {netlifyTomlFileName}");
        var config = Toml.ToModel<NetlifySiteConfig>(
            File.ReadAllText(netlifyTomlFileName),
            sourcePath: netlifyTomlFileName,
            options: new TomlModelOptions
            {
                IgnoreMissingProperties = true
            }
        );

        var anselBuild = config.Ansel?.Build;
        if (anselBuild == null || anselBuild.Length == 0)
        {
            throw new Exception("ansel build command required");
        }

        Log.Info($"generating in directory {templateDir}");
        Log.Info($"generating with command '{string.Join(' ', anselBuild)}'");

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "cmd" : anselBuild[0];
        var args = isWindows ? new[] { "/C" }.Concat(anselBuild) : anselBuild.Skip(1);
        var psi = new ProcessStartInfo(command, args)
        {
            WorkingDirectory = templateDir,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi)
            ?? throw new Exception("failed to run build process");

        await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, manifest, cancellationToken: ct);
        process.StandardInput.Close();

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            throw new Exception($"build process failed, exit code {process.ExitCode}");
        }

        var publishDir = Path.GetFullPath(Path.Join(templateDir, config.Build?.Publish ?? ""));
        if (!Directory.Exists(publishDir))
        {
            throw new Exception($"expected publish dir does not exist at {publishDir}");
        }

        Log.Info($"using publish dir at {publishDir}");
        var anselDir = Path.Join(publishDir, Path.GetDirectoryName(NetlifyClient.PublicManifestPath));
        if (!Directory.Exists(anselDir))
        {
            Directory.CreateDirectory(anselDir);
        }

        var manifestPath = Path.Join(anselDir, Path.GetFileName(NetlifyClient.PublicManifestPath));
        Log.Info($"writing manifest to {manifestPath}");
        ManifestSerializer.SaveGzip(manifest, manifestPath);
    }
}