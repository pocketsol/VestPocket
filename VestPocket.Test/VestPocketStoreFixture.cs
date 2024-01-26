using System;
using System.Text.Json.Serialization;
using System.Threading;

namespace VestPocket.Test;

// https://xunit.net/docs/shared-context
public class VestPocketStoreFixture : IDisposable
{
    public VestPocketStoreFixture()
    {
    }

    private VestPocketStore _connection;

    public VestPocketStore Get(VestPocketOptions options, bool blankFilePath = true)
    {
        if (blankFilePath)
        {
            options.FilePath = null;
        }
        options.JsonSerializerContext = SourceGenerationContext.Default;

        options.AddType<TestDocument>();
        var result = new VestPocketStore(options);
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
