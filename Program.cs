using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

class Program
{
    static ServiceProvider _provider = default!;
    static CancellationTokenSource _cts = new();

    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await Run(args);
        }
        finally
        {
            _cts?.Dispose();
            _provider?.Dispose();
            Log.Cleanup();
        }
    }

    public static async Task<int> Run(string[] args)
    {
        var app = new AppBuilder()
            .Name("ansel", "ansel command line tools")
            .Cancellation(() => _cts.Token)
            .GlobalHandler(
                RunCommand,
                new Option<string?>("--settings", () => null, "path to json settings file"),
                new Option<string>("--log-template", () => "[{Timestamp:HH:mm:ss} {Level:u3}] {Message}{NewLine}{Exception}", "log message template"),
                new Option<bool>("--log-debug", () => false, "whether to log debug information"),
                new Option<string>("--timeout", () => "", "max execution time for command")
            )
            .Command(cmd => cmd
                .Name("website", "website tools")
                .Alias("site")
                .Command(cmd => cmd
                    .Name("sync", "sync manifest with onedrive and build website from template")
                    .Handler(
                        SiteSync,
                        new Argument<string>("template dir")
                    )
                )
                .Command(cmd => cmd
                    .Name("build", "build website from template")
                    .Handler(
                        SiteBuild,
                        new Argument<string>("template dir"),
                        new Argument<string>("manifest.json.gz")
                    )
                )
                .Command(cmd => cmd
                    .Name("build-netlify", "request netlify build & deploy")
                    .Handler(
                        SiteBuildNetlify
                    )
                )
            )
            .Command(cmd => cmd
                .Name("onedrive", "onedrive tools")
                .Alias("od")
                .Command(cmd => cmd
                    .Name("photos", "list photos")
                    .Handler(OneDriveListPhotos)
                )
            )
            .Command(cmd => cmd
                .Name("manifest", "manifest.json.gz tools")
                .Command(cmd => cmd
                    .Name("from-netlify", "create manifest.json.gz from netlify site")
                    .Handler(
                        ManifestFromNetlify,
                        new Argument<string>("output file", () => "manifest.json.gz")
                    )
                )
                .Command(cmd => cmd
                    .Name("from-onedrive", "create manifest.json.gz from onedrive")
                    .Handler(
                        ManifestFromOneDrive,
                        new Argument<string>("output file", () => "manifest.json.gz")
                    )
                )
                .Command(cmd => cmd
                    .Name("from-netlify-onedrive", "create manifest.json.gz from netlify site and onedrive")
                    .Handler(
                        ManifestFromNetlifyOneDrive,
                        new Argument<string>("output file", () => "manifest.json.gz")
                    )
                )
                .Command(cmd => cmd
                    .Name("from-local-onedrive", "create manifest.json.gz from local and onedrive")
                    .Handler(
                        ManifestFromLocalOneDrive,
                        new Argument<string>("input & output file", () => "manifest.json.gz")
                    )
                )
                .Command(cmd => cmd
                    .Name("show-photos", "show photo information from manifest.json.gz")
                    .Handler(
                        ManifestShowPhotos,
                        new Argument<string>("file name or url", () => "manifest.json.gz")
                    )
                )
            )
            .Command(cmd => cmd
                .Name("exif", "exif tools")
                .Command(cmd => cmd
                    .Name("from-image", "show exif for image")
                    .Handler(
                        ExifFromImage,
                        new Argument<string>("file name or url")
                    )
                )
                .Command(cmd => cmd
                    .Name("from-manifest", "show exif for all images in manifest.json.gz")
                    .Handler(
                        ExifFromManifest,
                        new Argument<string>("file name or url")
                    )
                )
            )
            .Build();

        return await app.InvokeAsync(args);
    }

    public static async Task<int> RunCommand(InvocationContext ctx, ICommandHandler handler, string? settingsFileName, string logTemplate, bool logDebug, string timeoutExpr)
    {
        try
        {
            Log.Initialize(logTemplate, logDebug);
            Log.Debug("debug logging enabled");

            _provider = Services.CreateProvider(settingsFileName);

            using var timeoutSource = new CancellationTokenSource();
            using var interruptSource = new CancellationTokenSource();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, interruptSource.Token);

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                interruptSource.Cancel();
            };

            var timeout = ParseTimeSpan(timeoutExpr) ?? TimeSpan.Zero;
            if (timeout != TimeSpan.Zero)
            {
                Log.Info($"using command timeout of {timeout}");
                timeoutSource.CancelAfter(timeout);
            }

            var args = Environment.GetCommandLineArgs();
            var commandName = ctx.Parser.Configuration.RootCommand.Name;
            var commandLine = commandName;
            if (args.Length > 1)
            {
                commandLine += " " + string.Join(" ", args.Skip(1));
            }

            Log.Info(commandLine);

            var description = ctx.ParseResult.CommandResult.Command.Description;
            if (!string.IsNullOrEmpty(description))
            {
                Log.Info(description);
            }

            try
            {
                var result = await handler.InvokeAsync(ctx);
                Log.Info("done");
                return result;
            }
            catch (OperationCanceledException) when (timeoutSource.Token.IsCancellationRequested)
            {
                throw new CommandException($"command timeout after {timeout}");
            }
            catch (OperationCanceledException) when (interruptSource.Token.IsCancellationRequested)
            {
                throw new CommandException("command interrupted");
            }
        }
        catch (CommandException e)
        {
            Log.Error(e.Message);
            return 1;
        }
        catch (Exception e)
        {
            Log.Error(e.Message, e);
            return 1;
        }
    }

    static TimeSpan? ParseTimeSpan(string? timeSpanExpr)
    {
        if (string.IsNullOrEmpty(timeSpanExpr))
        {
            return null;
        }

        if (TimeSpan.TryParse(timeSpanExpr, out var timeSpan))
        {
            return timeSpan;
        }

        throw new CommandException($"invalid time span: {timeSpanExpr}");
    }

    static async Task SiteSync(string templateDir, CancellationToken ct)
    {
        var netlify = _provider.GetRequiredService<NetlifyClient>();
        var manager = _provider.GetRequiredService<ManifestManager>();
        var builder = _provider.GetRequiredService<SiteBuilder>();

        var manifest = await netlify.GetDeployedManifest(ct);
        var newManifest = await manager.SyncWithOneDrive(manifest, ct);

        await builder.Build(templateDir, newManifest, ct);
    }

    static async Task SiteBuild(string templateDir, string manifestFileName, CancellationToken ct)
    {
        var manifest = ManifestSerializer.LoadFile(manifestFileName);
        var builder = _provider.GetRequiredService<SiteBuilder>();

        await builder.Build(templateDir, manifest, ct);
    }

    static async Task SiteBuildNetlify(CancellationToken ct)
    {
        var netlify = _provider.GetRequiredService<NetlifyClient>();
        await netlify.RequestBuild(ct);
    }

    static async Task OneDriveListPhotos(CancellationToken ct)
    {
        var drive = _provider.GetRequiredService<OneDriveClient>();
        await foreach (var photo in drive.GetPhotos(ct))
        {
            Log.Info(photo);
        }
    }

    static async Task ManifestFromNetlify(string outputFile, CancellationToken ct)
    {
        if (File.Exists(outputFile))
        {
            throw new CommandException($"output file already exists: {outputFile}");
        }

        var netlify = _provider.GetRequiredService<NetlifyClient>();
        var manifest = await netlify.GetDeployedManifest(ct);
        ManifestSerializer.SaveGzip(manifest, outputFile);

        Log.Info($"manifest saved to {outputFile}");
    }

    static async Task ManifestFromOneDrive(string outputFile, CancellationToken ct)
    {
        if (File.Exists(outputFile))
        {
            throw new CommandException($"output file already exists: {outputFile}");
        }

        var manager = _provider.GetRequiredService<ManifestManager>();
        var manifest = await manager.SyncWithOneDrive(new Manifest { Photos = [] }, ct);
        ManifestSerializer.SaveGzip(manifest, outputFile);

        Log.Info($"manifest saved to {outputFile}");
    }

    static async Task ManifestFromNetlifyOneDrive(string outputFile, CancellationToken ct)
    {
        if (File.Exists(outputFile))
        {
            throw new CommandException($"output file already exists: {outputFile}");
        }

        var netlify = _provider.GetRequiredService<NetlifyClient>();
        var manager = _provider.GetRequiredService<ManifestManager>();

        var manifest = await netlify.GetDeployedManifest(ct);
        var newManifest = await manager.SyncWithOneDrive(manifest, ct);
        ManifestSerializer.SaveGzip(newManifest, outputFile);

        Log.Info($"manifest saved to {outputFile}");
    }

    static async Task ManifestFromLocalOneDrive(string inputOutputFile, CancellationToken ct)
    {
        var manager = _provider.GetRequiredService<ManifestManager>();

        var manifest = ManifestSerializer.LoadFile(inputOutputFile);
        var newManifest = await manager.SyncWithOneDrive(manifest, ct);
        ManifestSerializer.SaveGzip(newManifest, inputOutputFile);

        Log.Info($"manifest saved to {inputOutputFile}");
    }

    static async Task ManifestShowPhotos(string fileNameOrUrl, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var manifestStream = (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out var url) && url.Scheme.StartsWith("http"))
            ? await http.GetStreamAsync(url, ct)
            : File.OpenRead(fileNameOrUrl);

        var manifest = ManifestSerializer.LoadGzip(manifestStream);
        foreach (var photo in manifest.Photos)
        {
            Log.Info(photo);
        }
    }

    static async Task ExifFromImage(string fileNameOrUrl, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var imageStream = (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out var url) && url.Scheme.StartsWith("http"))
            ? await http.GetStreamAsync(url, ct)
            : File.OpenRead(fileNameOrUrl);

        using var image = await Image.LoadAsync(imageStream, ct);
        ShowExif(image.Metadata.ExifProfile);
    }

    static async Task ExifFromManifest(string fileNameOrUrl, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var manifestStream = (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out var url) && url.Scheme.StartsWith("http"))
            ? await http.GetStreamAsync(url, ct)
            : File.OpenRead(fileNameOrUrl);

        var manifest = ManifestSerializer.LoadGzip(manifestStream);
        foreach (var photo in manifest.Photos)
        {
            Log.Info(string.Join('/', photo.Path));
            if (photo.Exif == null)
            {
                Log.Info("no exif data present");
                continue;
            }

            ShowExif(photo.Exif);
        }
    }

    static void ShowExif(ExifProfile? profile)
    {
        if (profile == null)
        {
            Log.Info("no exif data present");
            return;
        }

        var table = Exif.ToTable(profile);
        Console.WriteLine(table);
    }
}

class CommandException : Exception
{
    public CommandException(string message) : base(message) { }
    public CommandException(string message, Exception inner) : base(message, inner) { }
}