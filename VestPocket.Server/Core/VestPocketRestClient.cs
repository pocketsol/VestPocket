using VestPocket.Server.Interfaces;

namespace VestPocket.ClientServer.Core
{
    public class VestPocketRestClient<T> : IClient<T> where T : class, IEntity
    {
        public string Hostname { get; }

        private readonly string _rootUser;
        private readonly string _rootPassword;
        private readonly HttpClient _httpClient; 

        public VestPocketRestClient(string hostname, string rootUser, string rootPassword, HttpClient httpClient)
        {
            Hostname = hostname;
            _rootUser = rootUser;
            _rootPassword = rootPassword;
            _httpClient = httpClient;
        }

        public async Task OpenAsync(CancellationToken? token)
        {

        }

        public async Task CloseAsync(CancellationToken? token)
        {

        }

        public Task<ILockTransaction<T>> CreateTransaction()
        {
            throw new NotImplementedException();
        }

        public Task<bool> TryDeleteAsync(string store, string key)
        {
            throw new NotImplementedException();
        }

        public Task<bool> TryGetAsync(string store, string key, out T value)
        {
            throw new NotImplementedException();
        }

        public Task<bool> TrySetAsync(string store, string key, out T value)
        {
            throw new NotImplementedException();
        }
    }
}
