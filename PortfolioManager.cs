using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

class PortfolioManager(OneDriveClient drive)
{
    public async Task<(bool Changed, Portfolio Portfolio)> UpdatePortfolio(Portfolio portfolio, CancellationToken ct)
    {
        var byId = portfolio.Photos.ToDictionary(p => p.Id, p => p);

        var curPhotos = await drive.GetPhotos(ct).ToListAsync(ct);
        var addPhotos = curPhotos.Where(p => !byId.ContainsKey(p.Id)).ToList();
        // var changePhotos = curPhotos.Where(p => byId.ContainsKey(p.Id) && (byId[p.Id].ModifiedAt != p.ModifiedAt || byId[p.Id].Collection != p.Collection)).ToList();
        // todo change detection
        var changePhotos = curPhotos.Where(p => byId.ContainsKey(p.Id) && (byId[p.Id].ModifiedAt != p.ModifiedAt)).ToList();
        // // todo we do not need to update photo if only collection changed (e.g. folder rename)
        var updatePhotos = addPhotos.Concat(changePhotos).ToList();

        var removePhotos = portfolio.Photos.Select(p => p.Id)
            .Except(curPhotos.Select(p => p.Id))
            .ToHashSet();

        if (updatePhotos.Count == 0 && removePhotos.Count == 0)
        {
            Log.Info("no portfolio changes found");
            return (false, portfolio);
        }

        foreach (var photo in updatePhotos)
        {
            if (photo.Path is not [var _, var collection, ..var sections, var name])
            {
                continue;
            }

            var exif = await GetPhotoExif(photo, ct);
            var comment = exif.GetString(ExifTag.XPComment)
                       ?? exif.GetString(ExifTag.ImageDescription)
                       ?? exif.GetString(ExifTag.UserComment);

            // ExifTag.DateTime
            // ExifTag.DateTimeDigitized
            // ExifTag.DateTimeOriginal
            // ExifTag.OffsetTime
            // ExifTag.OffsetTimeDigitized
            // ExifTag.OffsetTimeOriginal

            byId[photo.Id] = new PortfolioPhoto
            {
                Id = photo.Id,
                Caption = comment,
                Collection = collection,
                Section = sections.Length == 0 ? null : string.Join(" - ", sections),
                IsCover = Regex.IsMatch(name, @"(\b|^)cover(\b|$)", RegexOptions.IgnoreCase),
                ModifiedAt = photo.ModifiedAt,
                Url = photo.Url
            };
        }

        var updatedGallery = new Portfolio
        {
            Photos = [..byId.Where(e => !removePhotos.Contains(e.Key)).Select(e => e.Value)]
        };

        Log.Info($"{removePhotos.Count} photos removed");
        Log.Info($"{addPhotos.Count} photos added");
        Log.Info($"{changePhotos.Count} photos changed");

        return (true, updatedGallery);
    }

    static async Task<ExifProfile> GetPhotoExif(IDrivePhoto photo, CancellationToken ct)
    {
        using var stream = await photo.Fetch(ct);
        using var image = await Image.LoadAsync(stream, ct);
        return image.Metadata.ExifProfile ?? new ExifProfile();
    }
}

static class Extensions
{
    public static string? GetString(this ExifProfile exif, ExifTag<string> tag)
    {
        var value = exif.TryGetValue(tag, out var result)
            ? result.Value?.Trim()?.Trim('\0')
            : null;

        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? GetString(this ExifProfile exif, ExifTag<EncodedString> tag)
    {
        var value = exif.TryGetValue(tag, out var result)
            ? result?.Value.Text?.Trim()?.Trim('\0')
            : null;

        return string.IsNullOrEmpty(value) ? null : value;
    }
}