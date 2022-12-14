using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    internal class StoreHeader
    {
        public DateTimeOffset Creation { get; set; }
        public DateTimeOffset? LastRewrite { get; set; }
        public IAsyncEnumerable<byte[]> CompressedEntities { get; set; }
    }
}
