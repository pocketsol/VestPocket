using System.Text.Json.Serialization;

namespace VestPocket.ClientServer.Core
{
    [JsonDerivedType(typeof(Entity), nameof(Entity))]
    public record class Entity(string Key, int Version, bool Deleted) : IEntity
    {
        public IEntity WithVersion(int version) { return this with { Version = version }; }
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(VestPocketItem))]
    internal partial class VestPocketJsonContext : JsonSerializerContext { }

    public record VestPocketItem(
        string Key, 
        int Version,
        bool Deleted,
        object Item) : Entity(Key, Version, Deleted)
    {
        public object Item = Item;
    }
}
