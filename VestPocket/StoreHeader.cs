namespace VestPocket;

/// <summary>
/// Represents the JSON data that is stored in the first row of a VestPocket file.
/// It contains meta data and any compressed entities from the last rewrite.
/// </summary>
internal class StoreHeader
{
    public DateTimeOffset Creation { get; set; }
    public DateTimeOffset? LastRewrite { get; set; }
    public IAsyncEnumerable<byte[]> CompressedEntities { get; set; }
}
