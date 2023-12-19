namespace VestPocket.ClientServer.Interfaces
{
    public interface IOpenStore<TEntity>
    {
        public Task<TEntity> Get(string key);
        public Task Set(TEntity entity);
    }
}
