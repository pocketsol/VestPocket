using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VestPocket
{

    /// <summary>
    /// A context used to source generate System.Text.Json serialization logic for VestPocket types
    /// that are used internally in VestPocket logic (such as the StoreHeader).
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(StoreHeader))]
    internal partial class InternalSerializationContext : JsonSerializerContext
    {
    }
}
