using SixLabors.ImageSharp;

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
            if (photo.Path.Length < 3)
            {
                continue;
            }

            using var stream = await photo.Fetch(ct);
            using var image = await Image.LoadAsync(stream, ct);

            byId[photo.Id] = new PortfolioPhoto
            {
                Id = photo.Id,
                Path = [..photo.Path.Skip(1)],
                ModifiedAt = photo.ModifiedAt,
                Url = photo.Url,
                Width = image.Width,
                Height = image.Height,
                Exif = Exif.FilterProfile(image.Metadata.ExifProfile, maxEntrySizeBytes: 1024)
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
}