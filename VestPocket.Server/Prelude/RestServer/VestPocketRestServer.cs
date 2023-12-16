using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System;
using System.Security;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VestPocket.Server.Interfaces;

namespace VestPocket.ClientServer.Prelude.RestServer
{
    public class RestServerOptions
    {
        public string Hostname { get; set; } = "0.0.0.0:9597";
        public string RootUser { get; set; } = "admin";
        public string RootPassword { get; set; } = "admin";
        public string StoragePath { get; set; } = "/";
    }

    public class RestServer<TStore> : IServer<TStore> where TStore : class, IEntity
    {
        public string Hostname { get; }

        private readonly WebApplication _host;
        private readonly string _rootUser;
        private readonly string _rootPassword;
        private readonly IDictionary<string, VestPocketStore<TStore>> _stores;

        private bool _serverRunning = false;

        public RestServer(RestServerOptions options)
        {
            _stores = new Dictionary<string, VestPocketStore<TStore>>();
            Hostname = options.Hostname;
            _rootUser = options.RootUser;
            _rootPassword = options.RootPassword;

            var hostBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
            _host = hostBuilder.Build();
        }

        public async Task StartAsync()
        {
            _host.Urls.Add(Hostname);

            _host.MapGet("/get/{store}/{key}", (
                [FromRoute] string store, 
                [FromRoute] string key,
                [FromHeader] string user,
                [FromHeader] string password) =>
            {
                if (_stores.TryGetValue(key, out var value ))
                {

                }
            });

            _host.MapPut("/set/{store}/{key}", () =>
            {

            });

            _serverRunning = true;

            try
            {
                await _host.RunAsync();
            }
            catch
            {
                _serverRunning = false;
            }
        }

        public async Task StopAsync()
        {
            await _host.StopAsync();
        }

        public string CreateStore(JsonTypeInfo<TStore> typeInfo, string name = "default", CancellationToken token = default)
        {
            var options = new VestPocketOptions { FilePath = name + ".db" };
            var store = new VestPocketStore<TStore>(typeInfo, options);
            _stores.Add(name, store);
            return name;
        }
    }
}
