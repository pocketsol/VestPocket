using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VestPocket.ConsoleTest
{
    [JsonDerivedType(typeof(Entity), nameof(Entity))]
    [JsonDerivedType(typeof(TestEntity), nameof(TestEntity))]
    public record class Entity(string Key, int Version, bool Deleted, string Body) : IEntity
    {
        public IEntity WithVersion(int version)
        {
            return this with { Version = version };
        }
    }

    public record TestEntity(string Key, int Version, bool Deleted, string Body, string Address) : Entity(Key, Version, Deleted, Body)
    {

    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(Entity))]
    internal partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
