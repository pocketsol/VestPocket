namespace VestPocket
{
    internal sealed class NoOpTransaction : Transaction
    {

        public NoOpTransaction():base(false)
        {

        }

        public override Kvp this[int index] { 
            get => throw new NotImplementedException(); 
            set => throw new NotImplementedException(); 
        }

        public override int Count => 0;
    }
}
