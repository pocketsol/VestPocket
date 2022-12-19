using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    internal static class VestPocketExtensionMethods
    {

        public static IMemoryOwner<T> ToOwnedMemory<T> (this IEnumerable<T> source)
        {
            var mem = MemoryPool<T>.Shared.Rent(1024);
            var span = mem.Memory.Span;

            int i = 0;
            foreach(var item in source) 
            {

                span[i] = item;    
                i++;

                if (i == span.Length)
                {
                    var newMem = MemoryPool<T>.Shared.Rent(span.Length * 2);
                    var newSpan = newMem.Memory.Span;
                    span.CopyTo(newSpan);
                    mem.Dispose();
                    mem = newMem;
                }
            }
            return mem;
        }

    }
}
