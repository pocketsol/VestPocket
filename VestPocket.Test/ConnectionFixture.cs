using VestPocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VestPocket.Test
{
    // https://xunit.net/docs/shared-context
    public class ConnectionFixture : IDisposable
    {
        public Connection<Entity> Connection { get; private set; }

        public ConnectionFixture()
        {
            this.Connection = Connection<Entity>.CreateTransient(SourceGenerationContext.Default.Entity);
            this.Connection.OpenAsync(CancellationToken.None).Wait();
        }

        public void Dispose()
        {
            this.Connection.Close(CancellationToken.None).Wait();

        }
    }
}
