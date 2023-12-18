using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VestPocket.Server.Interfaces;

namespace VestPocket.ClientServer.Core
{
    public class RestServerOptions
    {
        public string Hostname { get; set; } = "0.0.0.0:9597";
        public string RootUser { get; set; } = "admin";
        public string RootPassword { get; set; } = "admin";
        public string StoragePath { get; set; } = "/";
    }

    public class RestServerConnectionPayload
    {
        public string? User { get; set; }
        public string? Password { get; set; }
        public DateTime Expiration { get; set; } = DateTime.Now.AddDays(7);
    }

    public class RestServer<TStore> : IServer<TStore> where TStore : class, IEntity
    {
        public string Hostname { get; }

        private readonly WebApplication _host;
        private readonly string _rootUser;
        private readonly string _rootPassword;
        private readonly Dictionary<string, VestPocketStore<TStore>> _stores;
        private readonly Dictionary<string, VestPocketConnection> _connections;

        public RestServer(RestServerOptions options)
        {
            _stores = new();
            _connections = new();
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
                [FromHeader] string token) =>
            {
                if (!CheckConnection(token))
                {
                    return Results.Unauthorized();
                }

                if (_stores.TryGetValue(store, out var value))
                {
                    try
                    {
                        await value.OpenAsync(default);
                        var result = value.Get(key);
                        await value.Close(default);

                        return Results.Ok(result);
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(ex.Message, (JsonSerializerOptions?)null, "text/plain", 500);
                    }
                }

                return Results.NotFound("Store not found.");
            });

            _host.MapPut("/set/{store}/{key}", async (
                [FromRoute] string store,
                [FromBody] TStore entity,
                [FromHeader] string token) =>
            {
                if (!CheckConnection(token))
                {
                    return Results.Unauthorized();
                }

                if (_stores.TryGetValue(store, out var value))
                {
                    try
                    {
                        await value.OpenAsync(default);
                        var result = await value.Save(new[] { entity });

                        return Results.Ok(result.First());
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(ex.Message, (JsonSerializerOptions?)null, "text/plain", 500);
                    }
                }

                return Results.NotFound("Store not found.");
            });

            _host.MapPost("/connect", async ([FromBody] RestServerConnectionPayload payload) =>
            {
                var areCredentialsCorrect = payload.User == _rootUser && payload.Password == _rootPassword;

                if (areCredentialsCorrect)
                {
                    var token = CreateConnection(payload.Expiration);
                    return Results.Ok(token);
                }

                return Results.Unauthorized();
            });

            await _host.RunAsync();
        }

        public async Task StopAsync()
        {
            await _host.StopAsync();
        }

        public string CreateStore(JsonTypeInfo<TStore> typeInfo, string name = "default")
        {
            var options = new VestPocketOptions { FilePath = name + ".db" };
            var store = new VestPocketStore<TStore>(typeInfo, options);
            _stores.Add(name, store);
            return name;
        }

        private string CreateConnection(DateTime expiration)
        {
            var token = Guid.NewGuid().ToString().Replace("-", "");
            _connections.Add(token, new VestPocketConnection(_rootUser, expiration));
            return token;
        }

        private bool CheckConnection(string token)
        {
            if (_connections.TryGetValue(token, out var connection))
            {
                if (connection.ExpiresAt < DateTime.Now)
                {
                    _connections.Remove(token);
                    return false;
                }

                return true;
            }

            return false;
        }
    }
}
