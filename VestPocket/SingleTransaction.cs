
namespace VestPocket
{
    internal sealed class SingleTransaction : Transaction
    {
        private Kvp entity;
        public Kvp Entity { get => entity; internal set => entity = value; }

        public SingleTransaction(Kvp entity, bool throwOnError) : base(throwOnError)
        {
            this.entity = entity;
        }

        public override int Count => 1;

        public override Kvp this[int index] { 
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
