using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Mime;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VestPocket.Server.Interfaces;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

            _host.MapGet("/get/{store}/{key}", async (
                [FromRoute] string store, 
                [FromRoute] string key,
                [FromHeader] string user,
                [FromHeader] string password) => 
            {
                if (_stores.TryGetValue(store, out var value))
                {
                    try
                    {
                        await value.OpenAsync(default);
                        var result = value.Get(key);
                        await value.Close(default);

                        return Results.Ok(result);
                    }
                    catch(Exception ex)
                    {
                        return Results.Json(ex.Message, (JsonSerializerOptions?)null, "text/plain", 500);
                    }
                }

                return Results.NotFound("Store not found.");
            });

            _host.MapPut("/set/{store}/{key}", async (
                [FromRoute] string store,
                [FromRoute] string key,
                [FromBody] TStore data,
                [FromHeader] string user,
                [FromHeader] string password) =>
            {
                if (_stores.TryGetValue(store, out var value))
                {
                    try
                    {
                        await value.OpenAsync(default);
                        // store.Get()? Not sure on how to proceed
                        // if yes, then the TStore should not be the direct type and yes an
                        // implementation of IEntity? So I could use store.Save(TEntity)?

                        return Results.Ok(null); // TODO: Change to reflect the operation result
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(ex.Message, (JsonSerializerOptions?)null, "text/plain", 500);
                    }
                }

                return Results.NotFound("Store not found.");
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
