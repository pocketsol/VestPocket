
namespace VestPocket
{
    internal sealed class MultipleTransaction : Transaction
    {

        public Kvp[] Entities => entities;
        public MultipleTransaction(Kvp[] entities, bool throwOnError) : base(throwOnError)
        {
            this.entities = entities;
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
