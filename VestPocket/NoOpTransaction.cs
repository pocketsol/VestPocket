namespace VestPocket
{
    internal sealed class NoOpTransaction : Transaction, IDisposable
    {

        private static ObjectPool<NoOpTransaction> pool = new ObjectPool<NoOpTransaction>(
            () => new NoOpTransaction(), 100
        );

        public static NoOpTransaction Create() => pool.Get();

        public void Reset()
        {
            this.valueCompletionSource.Reset();
            this.valueTaskGenerated = false;
        }

        public void Dispose()
        {
            pool.Return(this);
        }

        private NoOpTransaction():base()
        {

        }

        public override Kvp this[int index] { 
            get => throw new NotImplementedException(); 
            set => throw new NotImplementedException(); 
        }

        public override int Count => 0;
    }
}
