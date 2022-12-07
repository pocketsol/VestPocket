using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VestPocket.Test
{
    [JsonDerivedType(typeof(TestDocument), typeDiscriminator: "TestDocument")]
    [JsonDerivedType(typeof(Entity), typeDiscriminator: "Entity")]
    public record class Entity(string Key, int Version, bool Deleted) : IEntity
    {
        public IEntity WithVersion(int version) { return this with { Version = version }; }
    }

    public record TestDocument(string Key, int Version, bool Deleted, string Body) : Entity(Key, Version, Deleted);

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(Entity))]
    [JsonSerializable(typeof(TestDocument))]
    internal partial class SourceGenerationContext : JsonSerializerContext
    {
    }


}
