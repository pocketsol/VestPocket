using System.Text.Json.Serialization.Metadata;

namespace VestPocket.Server.Interfaces
{
    public interface IServer<TStore> where TStore: IEntity
    {
        public Task StartAsync();
        public Task StopAsync();
        public string CreateStore(JsonTypeInfo<TStore> typeInfo, string name = "default");
    }
}
