using VestPocket.ClientServer.Interfaces;

namespace VestPocket.ClientServer.Core
{
    public class VestPocketRestClient : IVestPocketClient
    {
        public IRemoteVestPocketStore<TEntity> Open<TEntity>(string store, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }

    public class RemoteVestPocketRestStore<T> : IRemoteVestPocketStore<T>
    {
        public Task Close(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<T> Get(string key, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<T> GetByPrefix(string prefix, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<T> Save(T entity, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
