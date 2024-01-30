
namespace VestPocket
{
    internal sealed class MultipleTransaction : Transaction, IDisposable
    {
        private static ObjectPool<MultipleTransaction> pool = new ObjectPool<MultipleTransaction>(
            () => new MultipleTransaction(), 1000
        );

        public Kvp[] Entities => entities;
        private static readonly Kvp[] EmptyEntities = [];

        public static MultipleTransaction Create(VestPocketOptions options, Kvp[] entities, bool throwOnError)
        {
            var transaction = pool.Get();
            transaction.Reset(options, entities, throwOnError);
            foreach (var entity in entities)
            {
                transaction.serializer.Serialize(entity.Key, entity.Value);
            }
            return transaction;
        }

        private MultipleTransaction() : base()
        {

        }

        public void Reset(VestPocketOptions options, Kvp[] entities, bool throwOnError)
        {
            base.Reset(options, throwOnError);
            this.entities = entities;
        }

        public void Dispose()
        {
            pool.Return(this);
        }
        private Kvp[] entities;
        public override Kvp this[int index]
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
