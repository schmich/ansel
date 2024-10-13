using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Shares.Item;

record OneDrivePhoto(OneDriveClient Client)
{
    public required string Id { get; init; }
    public required string ETag { get; init; }
    public required string CTag { get; init; }
    public required string[] Path { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public required Uri Url { get; init; }

    public Task<Stream> Fetch(CancellationToken ct) => Client.FetchPhoto(Id, ct);
}

record OneDriveConfig
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required Uri ShareUrl { get; init; }
}

class OneDriveClient
{
    readonly GraphServiceClient _graph;
    readonly SharedDriveItemItemRequestBuilder _share;

    public OneDriveClient(GraphServiceClient graph, Uri shareUrl)
    {
        var shareUrlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(shareUrl.ToString()));
        var encodedShareUrl = $"u!{shareUrlBase64.TrimEnd('=').Replace('/', '_').Replace('+', '-')}";
        _graph = graph;
        _share = _graph.Shares[encodedShareUrl];
    }

    public async IAsyncEnumerable<OneDrivePhoto> GetPhotos([EnumeratorCancellation] CancellationToken ct)
    {
        Log.Info("enumerate onedrive photos");

        var share = await _share.DriveItem.GetAsync(config =>
        {
            config.QueryParameters.Expand = ["children"];
        }, ct) ?? throw new Exception("failed to get shared item");

        await foreach (var photo in GetPhotos([], share, ct))
        {
            yield return photo;
        }
    }

    public async Task<Stream> FetchPhoto(string id, CancellationToken ct)
    {
        Log.Info($"download photo {id}");
        var stream = await _share.Items[id].Content.GetAsync(cancellationToken: ct);
        return stream ?? throw new Exception("failed to get photo stream");
    }

    async IAsyncEnumerable<OneDrivePhoto> GetPhotos(string[] path, DriveItem item, [EnumeratorCancellation] CancellationToken ct)
    {
        if (item.Deleted != null)
        {
            yield break;
        }

        var name = item.Name ?? throw new Exception("unknown item name");

        if (item.File?.MimeType == "image/jpeg")
        {
            yield return new OneDrivePhoto(this)
            {
                Id = item.Id ?? throw new Exception("unknown item id"),
                ETag = item.ETag ?? throw new Exception("unknown item etag"),
                CTag = item.CTag ?? throw new Exception("unknown item ctag"),
                Path = [..path, name],
                ModifiedAt = item.LastModifiedDateTime ?? throw new Exception("unknown item modified time"),
                Url = item.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out var url) && url is string downloadUrl
                    ? new Uri(downloadUrl)
                    : throw new Exception("unknown item download url")
            };
        }
        else if (item.Folder?.ChildCount > 0)
        {
            var folder = await _share.Items[item.Id].GetAsync(config =>
            {
                config.QueryParameters.Expand = ["children"];
            }, ct) ?? throw new Exception("failed to get shared item");

            foreach (var child in folder.Children ?? [])
            {
                await foreach (var photo in GetPhotos([..path, name], child, ct))
                {
                    yield return photo;
                }
            }
        }
    }
}