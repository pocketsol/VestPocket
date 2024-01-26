using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    /// <summary>
    /// A Key Value Pair where the key is a string and the value is untyped.
    /// </summary>
    /// <param name="Key">The key associated with the value</param>
    /// <param name="Value">The untyped value associated with the key</param>
    public record struct Kvp(string Key, Object Value);
}
