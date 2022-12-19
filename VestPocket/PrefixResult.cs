using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    public class PrefixResult<TSelection> : IDisposable where TSelection : class, IEntity
    {
        private TSelection[] buffer;
        private int size = 1024;
        private int length = 0;
        private static readonly ArrayPool<TSelection> pool = ArrayPool<TSelection>.Create();
        private readonly string keyPrefix;

        public PrefixResult(string keyPrefix)
        {
            buffer = pool.Rent(size);
            this.keyPrefix = keyPrefix;
        }

        public void Add(TSelection entity)
        {
            buffer[length] = entity;
            length++;
            if (length == size)
            {
                size = size * 4;
                var newBuffer = pool.Rent(size);
                Array.Copy(buffer, newBuffer, buffer.Length);
                pool.Return(buffer);
                buffer = newBuffer;
            }
        }

        public IEnumerable<TSelection> Results => GetResults();

        public IEnumerable<TSelection> GetResults()
        {
            for(int i = 0; i < length; i++)
            {
                yield return buffer[i];
            }
        }

        public int Count => length;

        public string KeyPrefix => keyPrefix;


        public void Dispose()
        {
            pool.Return(buffer);
        }


    }
}
