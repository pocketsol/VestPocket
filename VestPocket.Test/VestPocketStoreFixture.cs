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
    public class VestPocketStoreFixture : IDisposable
    {
        public VestPocketStoreFixture()
        {
        }

        private VestPocketStore<Entity> _connection;

        public VestPocketStore<Entity> Get(VestPocketOptions options, bool blankFilePath = true)
        {
            if (blankFilePath)
            {
                options.FilePath = null;
            }
            var result = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, options);
            result.OpenAsync(CancellationToken.None).Wait();
            _connection = result;
            return result;
        }

        public void Dispose()
        {
            if (_connection != null && !_connection.IsDisposed)
            {
                _connection.Close(CancellationToken.None).Wait();
            }
        }
    }
}
