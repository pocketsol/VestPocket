namespace VestPocket.Server.Interfaces
{
    public interface IClient<T>
    {
        public Task OpenAsync(CancellationToken? token);
        public Task CloseAsync(CancellationToken? token);
        public Task<ILockTransaction<T>> CreateTransaction();
        public Task<bool> TryGetAsync(string store, string key, out T value);
        public Task<bool> TrySetAsync(string store, string key, out T value);
        public Task<bool> TryDeleteAsync(string store, string key);
    }
}
