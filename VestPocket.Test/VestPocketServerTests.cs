using Xunit;
using VestPocket.ClientServer.Core;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;

namespace VestPocket.Test
{
    public class VestPocketServerTests
    {

        [Fact(DisplayName = "Ensures the server will start and work gracefully")]
        public async void ExecutesGracefully()
        {
            var server = new VestPocketRestServer();
            var client = new HttpClient();

            await server.StartAsync();

            var result = await client.GetAsync("http://localhost:9597/health");

            await server.StopAsync();
            server.ForceClear();

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        [Fact(DisplayName = "Ensures the server will properly create a store and create/get an item")]
        public async void CreateAndGetStoreItem()
        {
            var server = new VestPocketRestServer(new()
            {
                Hostname = "localhost:9598",
            });
            server.ForceClear();
            server.CreateStore("create_and_get_store");

            var client = new HttpClient();

            await server.StartAsync();

            // Auth
            var authPayload = new StringContent(JsonSerializer.Serialize(new
            {
                User = "admin",
                Password = "admin"
            })); 
            authPayload.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var authResult = await client.PostAsync("http://localhost:9598/connect", authPayload);
            var token = JsonSerializer.Deserialize<string>(await authResult.Content.ReadAsStringAsync());

            // Operation
            var payload = new StringContent(JsonSerializer.Serialize(new 
            {
                Key = "test_item",
                Item = "test_value",
                Version = 0,
                Deleted = false
            }));
            payload.Headers.Add("token", token);
            payload.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var result = await client.PutAsync("http://localhost:9598/set/create_and_get_store", payload);
            var contentString = await result.Content.ReadAsStringAsync();
            var content = JsonSerializer.Deserialize<VestPocketItemPayload>(contentString);

            await server.StopAsync();

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("test_item", content.Key.ToString());
            Assert.Equal("test_value", content.Item.ToString());
        }
    }


}
