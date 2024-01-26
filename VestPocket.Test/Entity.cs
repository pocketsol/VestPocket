using System.Text.Json.Serialization;

namespace VestPocket.Test;

public record TestDocument(string Body);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(TestDocument))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
