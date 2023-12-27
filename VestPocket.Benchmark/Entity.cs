﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VestPocket.Benchmark
{
    public record class Entity(string Key, bool Deleted, string Body) : IEntity
    {
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(Entity))]
    internal partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
