using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    internal sealed class NoOpTransaction<T> : Transaction<T> where T : IEntity
    {

        public NoOpTransaction():base(false)
        {

        }

        public override T this[int index] { 
            get => throw new NotImplementedException(); 
            set => throw new NotImplementedException(); 
        }

        public override int Count => 0;
    }
}
