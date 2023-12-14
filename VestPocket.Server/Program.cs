using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using VestPocket;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});


var vestPocketStore = new VestPocketStore<Entity>(AppJsonSerializerContext.Default.Entity, VestPocketOptions.Default);
await vestPocketStore.OpenAsync(CancellationToken.None);

var app = builder.Build();
app.UseWebSockets();

var storeApi = app.MapGroup("/store");
storeApi.MapGet("/search/{prefix}", (string prefix) => vestPocketStore.GetByPrefix<Entity>(prefix));
storeApi.MapGet("/{key}", (string key) => vestPocketStore.Get<Entity>(key));
storeApi.MapPost("/{key}", async (string key, [FromBody] Entity entity) =>
{
    return await vestPocketStore.Save(entity);
});
app.Run();


[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Entity))]
[JsonSerializable(typeof(IEnumerable<Entity>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}


[JsonDerivedType(typeof(Entity), nameof(Entity))]
public record class Entity(string Key, int Version, bool Deleted, string Body) : IEntity
{
    public IEntity WithVersion(int version)
    {
        return this with { Version = version };
    }
}

