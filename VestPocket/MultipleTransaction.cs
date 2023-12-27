using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    internal sealed class MultipleTransaction<T> : Transaction<T> where T : IEntity
    {

        public T[] Entities => entities;
        public MultipleTransaction(T[] entities, bool throwOnError) : base(throwOnError)
        {
            this.entities = entities;
        }

        private T[] entities;
        public override T this[int index]
        {
            get
            {
                return entities[index];
            }
            set
            {
                entities[index] = value;
            }
        }

        public override int Count => entities.Length;
    }
}
