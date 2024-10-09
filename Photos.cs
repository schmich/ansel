using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

record Portfolio
{
    public required List<PortfolioPhoto> Photos { get; init; } = [];

    public static Portfolio FromFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".gz")
        {
            return FromGzip(fileName);
        }
        else
        {
            return FromJson(fileName);
        }
    }

    public static Portfolio FromJson(string fileName)
    {
        using var file = File.OpenRead(fileName);
        return FromJson(file);
    }

    public static Portfolio FromGzip(string fileName)
    {
        using var file = File.OpenRead(fileName);
        return FromGzip(file);
    }

    public static Portfolio FromJson(Stream stream)
    {
        return JsonSerializer.Deserialize<Portfolio>(stream)
            ?? throw new Exception("invalid portfolio json");
    }

    public static Portfolio FromGzip(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return FromJson(gzip);
    }

    public void ToJson(string fileName)
    {
        using var file = File.OpenWrite(fileName);
        ToJson(file);
    }

    public void ToGzip(string fileName)
    {
        using var file = File.OpenWrite(fileName);
        ToGzip(file);
    }

    public void ToJson(Stream stream)
    {
        JsonSerializer.Serialize(stream, this);
    }

    public void ToGzip(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(gzip, this);
    }
}

partial record PortfolioPhoto
{
    public required string Id { get; init; }

    public required string[] Path { get; init; }

    public required long ModifiedAt { get; init; }

    public required Uri Url { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(ExifProfileConverter))]
    public ExifProfile? Exif { get; init; }

    [JsonIgnore]
    public string Collection => Path[0];

    [JsonIgnore]
    public string? Section => Path.Length <= 2 ? null : string.Join(" - ", Path.Skip(1).Take(Path.Length - 2));

    [JsonIgnore]
    public string FileName => Path[^1];

    [JsonIgnore]
    public bool IsCover => IsCoverPattern().IsMatch(FileName);

    [JsonIgnore]
    public string? Comment => global::Exif.GetComment(Exif);

    [JsonIgnore]
    public DateTimeOffset? TakenAt => global::Exif.GetTakenAt(Exif);

    [GeneratedRegex(@"(\b|^)cover(\b|$)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex IsCoverPattern();
}