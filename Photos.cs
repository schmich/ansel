using System.Text.Json.Serialization;

interface IDrivePhoto
{
    string Id { get; }
    string[] Path { get; }
    long ModifiedAt { get; }
    Uri Url { get; init; }

    Task<Stream> Fetch(CancellationToken ct);
}

record Portfolio
{
    public required List<PortfolioPhoto> Photos { get; init; } = [];
}

record PortfolioPhoto
{
    public required string Id { get; init; }

    public required string Collection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Section { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; init; }

    public required bool IsCover { get; init; }

    public required long ModifiedAt { get; init; }

    public required Uri Url { get; init; }

    // long? takenAt
    // array<decimal>? location
}