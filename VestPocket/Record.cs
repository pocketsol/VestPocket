using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    public struct Record<T> where T : class, IEntity
    {
        public Record(string key, T entity)
        {
            Key = key;
            Entity = entity;
        }
        public string Key { get; }
        public T Entity { get; }
    }
}
