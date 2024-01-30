
namespace VestPocket
{
    internal sealed class SingleTransaction : Transaction, IDisposable
    {
        private static ObjectPool<SingleTransaction> pool = new ObjectPool<SingleTransaction>(
            () => new SingleTransaction(), 1000
        );

        private Kvp entity;
        public Kvp Entity { get => entity; internal set => entity = value; }

        //private static Kvp NoKvp = default;

        public static SingleTransaction Create(VestPocketOptions options, Kvp entity, bool throwsOnError)
        {
            var transaction = pool.Get();
            transaction.Reset(options, entity, throwsOnError);
            transaction.serializer.Serialize(entity.Key, entity.Value);
            return transaction;
        }

        private SingleTransaction() : base()
        {
        }

        public void Reset(VestPocketOptions options, Kvp entity, bool throwOnError)
        {
            base.Reset(options, throwOnError);
            this.entity = entity;
        }
        public void Dispose()
        {
            pool.Return(this);
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
