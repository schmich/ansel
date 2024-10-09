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
                    .Name("sync", "sync portfolio with onedrive and generate website")
                    .Handler(
                        SiteSync,
                        new Argument<string>("template dir"),
                        new Argument<string>("output dir")
                    )
                )
                .Command(cmd => cmd
                    .Name("generate", "generate website from template")
                    .Handler(
                        SiteGenerate,
                        new Argument<string>("portfolio.json.gz"),
                        new Argument<string>("template dir"),
                        new Argument<string>("output dir")
                    )
                )
                .Command(cmd => cmd
                    .Name("build", "request netlify build & deploy")
                    .Handler(
                        SiteBuild
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
                .Name("portfolio", "portfolio.json.gz tools")
                .Command(cmd => cmd
                    .Name("download-deployed", "download deployed portfolio.json.gz")
                    .Handler(
                        PortfolioDownloadDeployed,
                        new Argument<string>("output file")
                    )
                )
                .Command(cmd => cmd
                    .Name("download-latest", "download latest portfolio.json.gz")
                    .Handler(
                        PortfolioDownloadLatest,
                        new Argument<string>("output file")
                    )
                )
                .Command(cmd => cmd
                    .Name("dump", "dump photo information from portfolio.json.gz")
                    .Handler(
                        PortfolioDump,
                        new Argument<string>("file name or url")
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
                    .Name("from-portfolio", "show exif for all images in portfolio.json.gz")
                    .Handler(
                        ExifFromPortfolio,
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

    static async Task SiteSync(string templateDir, string outputDir, CancellationToken ct)
    {
        var netlify = _provider.GetRequiredService<NetlifyClient>();
        var portfolioManager = _provider.GetRequiredService<PortfolioManager>();
        var siteGenerator = _provider.GetRequiredService<SiteGenerator>();

        var portfolio = await netlify.GetDeployedPortfolio(ct);
        var (changed, newPortfolio) = await portfolioManager.UpdatePortfolio(portfolio, ct);
        Log.Info($"portfolio has{(changed ? " " : " not ")}changed");

        await siteGenerator.Generate(newPortfolio, templateDir, outputDir);
    }

    static async Task SiteGenerate(string portfolioFileName, string templateDir, string outputDir, CancellationToken ct)
    {
        if (!File.Exists(portfolioFileName))
        {
            throw new CommandException($"portfolio json does not exist: {portfolioFileName}");
        }

        var portfolio = Portfolio.FromFile(portfolioFileName);
        var generator = _provider.GetRequiredService<SiteGenerator>();
        await generator.Generate(portfolio, templateDir, outputDir);
    }

    static async Task OneDriveListPhotos(CancellationToken ct)
    {
        var drive = _provider.GetRequiredService<OneDriveClient>();
        await foreach (var photo in drive.GetPhotos(ct))
        {
            Log.Info(photo);
        }
    }

    static async Task PortfolioDownloadDeployed(string outputFile, CancellationToken ct)
    {
        if (File.Exists(outputFile))
        {
            throw new CommandException($"output file already exists: {outputFile}");
        }

        var netlify = _provider.GetRequiredService<NetlifyClient>();
        var portfolio = await netlify.GetDeployedPortfolio(ct);
        portfolio.ToGzip(outputFile);

        Log.Info($"deployed portfolio.json.gz saved to {outputFile}");
    }

    static async Task PortfolioDownloadLatest(string outputFile, CancellationToken ct)
    {
        if (File.Exists(outputFile))
        {
            throw new CommandException($"output file already exists: {outputFile}");
        }

        var netlify = _provider.GetRequiredService<NetlifyClient>();
        var portfolioManager = _provider.GetRequiredService<PortfolioManager>();

        var portfolio = await netlify.GetDeployedPortfolio(ct);
        var (_, newPortfolio) = await portfolioManager.UpdatePortfolio(portfolio, ct);
        newPortfolio.ToGzip(outputFile);

        Log.Info($"latest portfolio.json.gz saved to {outputFile}");
    }

    static async Task PortfolioDump(string fileNameOrUrl, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var portfolioStream = (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out var url) && url.Scheme.StartsWith("http"))
            ? await http.GetStreamAsync(url, ct)
            : File.OpenRead(fileNameOrUrl);

        var portfolio = Portfolio.FromGzip(portfolioStream);
        foreach (var photo in portfolio.Photos)
        {
            Log.Info(photo);
        }
    }

    static async Task SiteBuild(CancellationToken ct)
    {
        var config = _provider.GetRequiredService<NetlifyConfig>();
        Log.Info($"post {config.BuildHookUrl}");

        using var client = new HttpClient();
        await client.PostAsync(config.BuildHookUrl, new StringContent("{}"), ct);
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

    static async Task ExifFromPortfolio(string fileNameOrUrl, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var portfolioStream = (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out var url) && url.Scheme.StartsWith("http"))
            ? await http.GetStreamAsync(url, ct)
            : File.OpenRead(fileNameOrUrl);

        var portfolio = Portfolio.FromGzip(portfolioStream);
        foreach (var photo in portfolio.Photos)
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