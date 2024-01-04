namespace VestPocket.ClientServer.Interfaces
{
    public interface IRemoteVestPocketStore<TEntity>
    {
        public Task Close(CancellationToken ct = default);
        public Task<TEntity> Get(string key, CancellationToken ct = default);
        public Task<TEntity> GetByPrefix(string prefix, CancellationToken ct = default);
        public Task<TEntity> Save(TEntity entity, CancellationToken ct = default);
    }
}
