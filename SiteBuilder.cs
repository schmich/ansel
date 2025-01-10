using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

record TemplateConfig
{
    [JsonPropertyName("build")]
    public required string[] Build { get; init; }

    [JsonPropertyName("output")]
    public required string Output { get; init; }
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

        var configFileName = Path.Join(templateDir, "ansel.json");
        if (!File.Exists(configFileName))
        {
            throw new Exception($"ansel config does not exist at {Path.GetFullPath(configFileName)}");
        }

        Log.Info($"using ansel config at {configFileName}");
        using var configFile = File.OpenRead(configFileName);
        var template = await JsonSerializer.DeserializeAsync<TemplateConfig>(configFile, options: null, ct)
            ?? throw new Exception($"invalid ansel config at {configFileName}");

        if (template.Build.Length == 0)
        {
            throw new Exception("build command required");
        }

        Log.Info($"generating in directory {templateDir}");
        Log.Info($"generating with command '{string.Join(' ', template.Build)}'");

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var command = isWindows ? "cmd" : template.Build[0];
        var args = isWindows ? new[] { "/C" }.Concat(template.Build) : template.Build.Skip(1);
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

        var outputDir = Path.GetFullPath(Path.Join(templateDir, template.Output));
        if (!Directory.Exists(outputDir))
        {
            throw new Exception($"expected output dir does not exist at {outputDir}");
        }

        Log.Info($"using output dir at {outputDir}");
        var anselDir = Path.Join(outputDir, ".ansel");
        if (!Directory.Exists(anselDir))
        {
            Directory.CreateDirectory(anselDir);
        }

        var manifestPath = Path.Join(anselDir, "manifest.json.gz");
        Log.Info($"writing manifest to {manifestPath}");
        ManifestSerializer.SaveGzip(manifest, manifestPath);
    }
}