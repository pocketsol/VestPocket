using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using VestPocket.ClientServer.Base;

namespace VestPocket.ClientServer.Core;

public class VestPocketRestOptions : VestPocketServerOptions
{
    public string Prefix { get; set; } = "http://";
    public string Hostname { get; set; } = "localhost:9597";
}

public class VestPocketItemPayload
{
    [Required]
    [JsonPropertyName("key")]
    public string? Key { get; set; }
    [Required]
    [JsonPropertyName("version")]
    public int Version { get; set; }
    [Required]
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
    [Required]
    [JsonPropertyName("item")]
    public object? Item { get; set; }
}

public class RestServerConnectionPayload
{
    public string? User { get; set; }
    public string? Password { get; set; }
    public DateTime Expiration { get; set; } = DateTime.Now.AddDays(7);
}

public class VestPocketRestServer : VestPocketServer
{
    public string URI { get; }

    private readonly WebApplication _host;

    public VestPocketRestServer(VestPocketRestOptions? options = null)
    {
        options ??= new();
        URI = options.Prefix + options.Hostname;

        Initialize(options);

        var hostBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
        _host = hostBuilder.Build();
        _host.Urls.Add(URI);

        InitializeEndpoints();
    }

    public async Task StartAsync()
    {
        await _host.StartAsync();
    }

    public async Task StopAsync()
    {
        await _host.StopAsync();
    }

    public string CreateStore(string name)
    {
        var path = $"{_storagePath}/{name}/{name}.db";
        var options = new VestPocketOptions { FilePath = path };
        var store = new VestPocketStore<VestPocketItem>(VestPocketJsonContext.Default.VestPocketItem, options);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _stores!.Add(name, store);
        return name;
    }

    private bool CheckConnection(string token)
    {
        if (_connections!.TryGetValue(token, out var connection))
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

    private string CreateConnection(DateTime expiration)
    {
        var token = Guid.NewGuid().ToString().Replace("-", "");
        _connections!.Add(token, new VestPocketConnection(_rootUser!, expiration));
        return token;
    }

    private void InitializeEndpoints()
    {
        _host.MapGet("/health/", () => true);

        _host.MapGet("/get/{store}/{key}", async (
            [FromRoute] string store,
            [FromRoute] string key,
            [FromHeader] string token) =>
        {
            if (!CheckConnection(token))
            {
                return Results.Unauthorized();
            }

            if (_stores!.TryGetValue(store, out var value))
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

        _host.MapGet("/get-by-prefix/{store}/{prefix}", async (
            [FromRoute] string store,
            [FromRoute] string prefix,
            [FromHeader] string token) =>
        {
            if (!CheckConnection(token))
            {
                return Results.Unauthorized();
            }

            if (_stores!.TryGetValue(store, out var value))
            {
                try
                {
                    await value.OpenAsync(default);
                    var result = value.GetByPrefix(prefix);
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

        _host.MapPut("/set/{store}", async (
            [FromRoute] string store,
            [FromBody] VestPocketItemPayload entity,
            [FromHeader] string token) =>
        {
            if (!CheckConnection(token))
            {
                return Results.Unauthorized();
            }

            if (_stores!.TryGetValue(store, out var value))
            {
                try
                {
                    await value.OpenAsync(default);
                    var vestPocketItem = new VestPocketItem(
                        entity.Key!,
                        entity.Version!, 
                        entity.Deleted!, 
                        entity.Item!);

                    var result = await value.Save(new[] { vestPocketItem });
                    var resultFirst = result.First();

                    var responseObject = new
                    {
                        resultFirst.Key,
                        resultFirst.Item,
                        resultFirst.Version,
                        resultFirst.Deleted
                    };

                    return Results.Ok(responseObject);
                }
                catch (Exception ex)
                {
                    return Results.Json(ex.Message, (JsonSerializerOptions?)null, "text/plain", 500);
                }
            }

            return Results.NotFound("Store not found.");
        });

        _host.MapPost("/connect", ([FromBody] RestServerConnectionPayload payload) =>
        {
            var areCredentialsCorrect = payload.User == _rootUser && payload.Password == _rootPassword;

            if (areCredentialsCorrect)
            {
                var token = CreateConnection(payload.Expiration);
                return Results.Ok(token);
            }

            return Results.Json(statusCode: 401, data: "Incorrect credentials");
        });
    }
}