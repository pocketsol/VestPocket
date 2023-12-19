using Xunit;
using VestPocket.ClientServer.Core;
using System.Net.Http;
using System.Net;
using VestPocket.Server.Interfaces;

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
    }


}
