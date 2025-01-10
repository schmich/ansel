using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Blurhash.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

class ManifestManager(OneDriveClient drive)
{
    public async Task<Manifest> SyncWithOneDrive(Manifest manifest, CancellationToken ct)
    {
        var deployedPhotos = manifest.Photos.ToDictionary(p => p.Id, p => p);
        var oneDrivePhotos = await drive.GetPhotos(ct).ToListAsync(ct);
        var existingPhotos = oneDrivePhotos.Where(p => deployedPhotos.ContainsKey(p.Id));

        // change type 1: photo removed from onedrive
        var removedPhotoIds = manifest.Photos.Select(p => p.Id)
            .Except(oneDrivePhotos.Select(p => p.Id))
            .ToHashSet();

        var finalPhotos = new Dictionary<string, Photo>(deployedPhotos.Where(p => !removedPhotoIds.Contains(p.Key)));

        // change type 2: photo metadata changed
        var metaChangedPhotos = existingPhotos
            .Select(p => new { OneDrive = p, Deployed = deployedPhotos[p.Id] })
            .Where(p => p.OneDrive.Url != p.Deployed.Url
                    || !p.OneDrive.Path.SequenceEqual(p.Deployed.Path));

        foreach (var photo in metaChangedPhotos)
        {
            finalPhotos[photo.Deployed.Id] = photo.Deployed with
            {
                Path = photo.OneDrive.Path,
                Url = photo.OneDrive.Url
            };
        }

        // change type 3: photo added to onedrive
        // change type 4: photo content changed on onedrive
        var newPhotos = oneDrivePhotos.Where(p => !deployedPhotos.ContainsKey(p.Id));
        var contentChangedPhotos = existingPhotos.Where(p => deployedPhotos[p.Id].CTag != p.CTag);

        foreach (var photo in newPhotos.Concat(contentChangedPhotos))
        {
            if (photo.Path.Length < 3)
            {
                continue;
            }

            using var stream = await photo.Fetch(ct);
            using var image = await Image.LoadAsync<Rgba32>(stream, ct);

            finalPhotos[photo.Id] = new Photo
            {
                Id = photo.Id,
                Path = photo.Path,
                Url = photo.Url,
                Width = image.Width,
                Height = image.Height,
                BlurHash = Blurhasher.Encode(image, 5, 4),
                ETag = photo.ETag,
                CTag = photo.CTag,
                ModifiedAt = photo.ModifiedAt,
                Exif = Exif.FilterProfile(image.Metadata.ExifProfile, maxEntrySizeBytes: 1024)
            };
        }

        return new Manifest { Photos = [..finalPhotos.Values] };
    }
}

static class ManifestSerializer
{
    public static Manifest LoadFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".gz")
        {
            return LoadGzip(fileName);
        }
        else
        {
            return LoadJson(fileName);
        }
    }

    public static Manifest LoadJson(string fileName)
    {
        using var file = File.OpenRead(fileName);
        return LoadJson(file);
    }

    public static Manifest LoadGzip(string fileName)
    {
        using var file = File.OpenRead(fileName);
        return LoadGzip(file);
    }

    public static Manifest LoadJson(Stream stream)
    {
        return JsonSerializer.Deserialize<Manifest>(stream)
            ?? throw new Exception("invalid manifest json");
    }

    public static Manifest LoadGzip(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return LoadJson(gzip);
    }

    public static void SaveJson(Manifest manifest, string fileName)
    {
        using var file = File.OpenWrite(fileName);
        SaveJson(manifest, file);
    }

    public static void SaveGzip(Manifest manifest, string fileName)
    {
        using var file = File.OpenWrite(fileName);
        SaveGzip(manifest, file);
    }

    public static void SaveJson(Manifest manifest, Stream stream)
    {
        JsonSerializer.Serialize(stream, manifest);
    }

    public static void SaveGzip(Manifest manifest, Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(gzip, manifest);
    }
}

record Manifest
{
    [JsonPropertyName("photos")]
    public required List<Photo> Photos { get; init; } = [];
}

partial record Photo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("path")]
    public required string[] Path { get; init; }

    [JsonPropertyName("url")]
    public required Uri Url { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("blurHash")]
    public required string BlurHash { get; init; }

    [JsonPropertyName("etag")]
    public required string ETag { get; init; }

    [JsonPropertyName("ctag")]
    public required string CTag { get; init; }

    [JsonPropertyName("modifiedAt")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public required DateTimeOffset ModifiedAt { get; init; }

    [JsonPropertyName("exif")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(ExifProfileConverter))]
    public ExifProfile? Exif { get; init; }

    [JsonIgnore]
    public string Collection => Path[1];

    [JsonIgnore]
    public string? Section => Path.Length <= 3 ? null : string.Join(" - ", Path.Skip(2).Take(Path.Length - 3));

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

class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("expected number token");
        }

        var seconds = reader.GetInt64();
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var seconds = value.ToUnixTimeSeconds();
        writer.WriteNumberValue(seconds);
    }
}