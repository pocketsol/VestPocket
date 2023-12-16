namespace VestPocket.Server.Interfaces
{
    public interface ILockTransaction<T>
    {
        public Task<bool> SaveChangesAsync();
        public Task<bool> RollbackChangesAsync();
        public Task<bool> TryGetAsync(string store, string key, out T value);
        public Task<bool> TrySetAsync(string store, string key, out T value);
        public Task<bool> TryDeleteAsync(string store, string key);
    }
}
