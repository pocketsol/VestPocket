using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    internal sealed class SingleTransaction<T> : Transaction<T> where T : IEntity
    {
        private T entity;
        public T Entity { get => entity; internal set => entity = value; }

        public SingleTransaction(T entity, bool throwOnError) : base(throwOnError)
        {
            this.entity = entity;
        }

        public override int Count => 1;

        public override T this[int index] { 
            get
            {
                if (index != 0) throw new IndexOutOfRangeException();
                return entity;
            }
            set
            {
                if (index != 0) throw new IndexOutOfRangeException();
                entity = value;
            }
        }
    }
}
