using SixLabors.ImageSharp;

class PortfolioManager(OneDriveClient drive)
{
    public async Task<Portfolio> SyncWithOneDrive(Portfolio portfolio, CancellationToken ct)
    {
        var deployedPhotos = portfolio.Photos.ToDictionary(p => p.Id, p => p);
        var oneDrivePhotos = await drive.GetPhotos(ct).ToListAsync(ct);
        var existingPhotos = oneDrivePhotos.Where(p => deployedPhotos.ContainsKey(p.Id));

        // change type 1: photo removed from one drive
        var removedPhotoIds = portfolio.Photos.Select(p => p.Id)
            .Except(oneDrivePhotos.Select(p => p.Id))
            .ToHashSet();

        var finalPhotos = new Dictionary<string, PortfolioPhoto>(deployedPhotos.Where(p => !removedPhotoIds.Contains(p.Key)));

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

        // change type 3: photo added from one drive
        // change type 4: photo content changed on one drive
        var newPhotos = oneDrivePhotos.Where(p => !deployedPhotos.ContainsKey(p.Id));
        var contentChangedPhotos = existingPhotos.Where(p => deployedPhotos[p.Id].CTag != p.CTag);

        foreach (var photo in newPhotos.Concat(contentChangedPhotos))
        {
            if (photo.Path.Length < 3)
            {
                continue;
            }

            using var stream = await photo.Fetch(ct);
            using var image = await Image.LoadAsync(stream, ct);

            finalPhotos[photo.Id] = new PortfolioPhoto
            {
                Id = photo.Id,
                Path = photo.Path,
                Url = photo.Url,
                Width = image.Width,
                Height = image.Height,
                ETag = photo.ETag,
                CTag = photo.CTag,
                ModifiedAt = photo.ModifiedAt,
                Exif = Exif.FilterProfile(image.Metadata.ExifProfile, maxEntrySizeBytes: 1024)
            };
        }

        return new Portfolio { Photos = [..finalPhotos.Values] };
    }
}