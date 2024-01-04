namespace VestPocket.ClientServer.Interfaces
{
    public interface IVestPocketClient
    {
        public IRemoteVestPocketStore<TEntity> Open<TEntity>(string store, CancellationToken ct = default);
    }
}
