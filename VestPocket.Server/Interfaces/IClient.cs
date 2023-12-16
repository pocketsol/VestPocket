namespace VestPocket.Server.Interfaces
{
    public interface IClient<T>
    {
        public Task<ILockTransaction<T>> CreateTransaction();
        public Task<bool> TryGetAsync(string store, string key, out T value);
        public Task<bool> TrySetAsync(string store, string key, out T value);
        public Task<bool> TryDeleteAsync(string store, string key);
    }
}
