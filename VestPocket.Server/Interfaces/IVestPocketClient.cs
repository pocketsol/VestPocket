namespace VestPocket.ClientServer.Interfaces
{
    public interface IVestPocketClient
    {
        public IOpenStore<TEntity> Open<TEntity>(string store);
    }
}
