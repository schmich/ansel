using System.Text.RegularExpressions;
using System.Web;
using Fluid;
using Fluid.Values;
using Fluid.ViewEngine;
using Microsoft.Extensions.FileProviders;

partial class SiteGenerator
{
    public async Task Generate(Portfolio portfolio, string templateDir, string outputDir)
    {
        var templateDirFull = Path.GetFullPath(templateDir);
        var outputDirFull = Path.GetFullPath(outputDir);

        Log.Info($"generate website with {portfolio.Photos.Count} photos");
        Log.Info($"template: {templateDirFull}");
        Log.Info($"output: {outputDirFull}");

        if (!Directory.Exists(templateDir))
        {
            throw new Exception($"template directory does not exist: {templateDir}");
        }

        if (Directory.Exists(outputDir))
        {
            throw new Exception($"output directory already exists: {outputDir}");
        }

        Directory.CreateDirectory(outputDir);

        var collections = portfolio
            .Photos
            .ToLookup(p => p.Collection, p => p)
            .Select(collection => new
            {
                Name = StripOrdinal(collection.Key),
                Slug = Slug(collection.Key),
                Ordinal = GetOrdinal(collection.Key),
                Sections = collection
                    .ToLookup(p => p.Section, p => p)
                    .Select(section => new
                    {
                        Name = StripOrdinal(section.Key),
                        Slug = Slug(section.Key ?? ""),
                        Photos = section.OrderByDescending(p =>
                        {
                            var ordinal = GetOrdinal(p.FileName);
                            return ordinal.HasValue
                                ? long.MaxValue - ordinal
                                : p.TakenAt?.ToUnixTimeSeconds() ?? 0;
                        }).ToArray(),
                        Ordinal = GetOrdinal(section.Key)
                    })
                    .OrderByDescending(s =>
                    {
                        return s.Ordinal.HasValue
                            ? long.MaxValue - s.Ordinal
                            : s.Photos.Select(p => p.TakenAt?.ToUnixTimeSeconds() ?? 0).Max();
                    })
                    .ToArray()
            })
            .Select(c => new
            {
                c.Name,
                c.Slug,
                c.Ordinal,
                c.Sections,
                Cover = c.Sections.SelectMany(s => s.Photos).Where(p => p.IsCover).FirstOrDefault()
                     ?? c.Sections.SelectMany(p => p.Photos).First()
            })
            .OrderByDescending(c =>
            {
                return c.Ordinal.HasValue
                    ? long.MaxValue - c.Ordinal
                    : c.Sections.SelectMany(s => s.Photos).Select(p => p.TakenAt?.ToUnixTimeSeconds() ?? 0).Max();
            })
            .ToArray();

        await Generate(new { Collections = collections }, templateDir, "home.liquid", Path.Combine(outputDir, "index.html"));

        foreach (var collection in collections)
        {
            var collectionDir = Path.Combine(outputDir, collection.Slug);
            Directory.CreateDirectory(collectionDir);
            await Generate(collection, templateDir, "collection.liquid", Path.Combine(collectionDir, "index.html"));

            var lightboxDir = Path.Combine(collectionDir, "lightbox");
            Directory.CreateDirectory(lightboxDir);
            await Generate(collection, templateDir, "lightbox.liquid", Path.Combine(lightboxDir, "index.html"));
        }

        foreach (var templateFileName in Directory.EnumerateFiles(templateDir, "*", SearchOption.AllDirectories))
        {
            var relativeFileName = Path.GetRelativePath(templateDirFull, Path.GetFullPath(templateFileName));
            var extension = Path.GetExtension(relativeFileName);
            if (extension?.ToLowerInvariant() == ".liquid")
            {
                continue;
            }

            var relativeDir = Path.GetDirectoryName(relativeFileName);
            if (!string.IsNullOrEmpty(relativeDir))
            {
                Directory.CreateDirectory(Path.Combine(outputDirFull, relativeDir));
            }

            var sourcePath = templateFileName;
            var destPath = Path.Combine(outputDir, relativeFileName);

            Log.Info($"copy {sourcePath} -> {destPath}");
            File.Copy(sourcePath, destPath);
        }

        portfolio.ToGzip(Path.Join(outputDir, "portfolio.json.gz"));
    }

    static string? StripOrdinal(string? title)
    {
        if (title == null)
        {
            return null;
        }

        return OrdinalPattern().Replace(title, "");
    }

    static int? GetOrdinal(string? title)
    {
        if (title == null)
        {
            return null;
        }

        var match = OrdinalPattern().Match(title);
        return match.Success
            ? int.Parse(match.Groups[1].Value)
            : null;
    }

    static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", e => "-").Trim();

    static ValueTask<FluidValue> NetlifyImage(FluidValue url, FilterArguments arguments, TemplateContext context)
    {
        var netlifyUrl = $"/.netlify/images?url={HttpUtility.UrlEncode(url.ToStringValue())}";
        return new StringValue(netlifyUrl);
    }

    static ValueTask<FluidValue> Size(FluidValue url, FilterArguments arguments, TemplateContext context)
    {
        if (arguments.Count < 2)
        {
            throw new Exception("todo");
        }

        // todo uri parsing, better logic here
        return new StringValue(url.ToStringValue() + $"&w={arguments.At(0).ToStringValue()}&h={arguments.At(1).ToStringValue()}");
    }

    static ValueTask<FluidValue> Format(FluidValue url, FilterArguments arguments, TemplateContext context)
    {
        if (arguments.Count < 1)
        {
            throw new Exception("todo");
        }

        // todo uri parsing, better logic here
        return new StringValue(url.ToStringValue() + $"&fm={arguments.At(0).ToStringValue()}");
    }

    static ValueTask<FluidValue> Quality(FluidValue url, FilterArguments arguments, TemplateContext context)
    {
        if (arguments.Count < 1)
        {
            throw new Exception("todo");
        }

        // todo uri parsing, better logic here
        return new StringValue(url.ToStringValue() + $"&q={arguments.At(0).ToStringValue()}");
    }

    static ValueTask<FluidValue> Fit(FluidValue url, FilterArguments arguments, TemplateContext context)
    {
        if (arguments.Count < 1)
        {
            throw new Exception("todo");
        }

        // todo uri parsing, better logic here
        return new StringValue(url.ToStringValue() + $"&fit={arguments.At(0).ToStringValue()}");
    }

    static async Task Generate(object model, string templateDir, string templateFileName, string outputFileName)
    {
        var fileProvider = new PhysicalFileProvider(Path.GetFullPath(templateDir));
        var options = new FluidViewEngineOptions
        {
            ViewsFileProvider = fileProvider,
            PartialsFileProvider = fileProvider,
        };

        options.TemplateOptions.Trimming = TrimmingFlags.TagLeft | TrimmingFlags.TagRight;
        options.TemplateOptions.MemberAccessStrategy = new UnsafeMemberAccessStrategy();
        options.TemplateOptions.FileProvider = fileProvider;
        options.TemplateOptions.Filters.AddFilter("netlify_image", NetlifyImage);
        options.TemplateOptions.Filters.AddFilter("size", Size);
        options.TemplateOptions.Filters.AddFilter("format", Format);
        options.TemplateOptions.Filters.AddFilter("quality", Quality);
        options.TemplateOptions.Filters.AddFilter("fit", Fit);

        var context = new TemplateContext(model, options.TemplateOptions);

        using var output = File.Open(outputFileName, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(output);
        var renderer = new FluidViewRenderer(options);
        await renderer.RenderViewAsync(writer, templateFileName, context);
    }

    [GeneratedRegex(@"^#?(\d+)[^\w]+")]
    private static partial Regex OrdinalPattern();
}