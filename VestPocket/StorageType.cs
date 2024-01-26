using System.Text.Json.Serialization.Metadata;

namespace VestPocket
{
    internal record class StorageType(string TypeName, Type Type, JsonTypeInfo JsonTypeInfo, byte[] Utf8TypeName);
}
