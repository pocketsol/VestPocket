using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.IO.Compression;

namespace VestPocket;

/// <summary>
/// A store that serializes transactions by appending them to the
/// end of a file and can only be be loaded by loading the entire file.
/// Shrinking removed data from the file doubles as backing up the transaction
/// store, as it requires rewriting.
/// </summary>
internal class TransactionLog<T> : IDisposable where T : class, IEntity
{
    private Stream outputStream;
    private readonly Func<Stream> rewriteStreamFactory;
    private readonly Func<Stream, Stream, Stream> swapRewriteStreamCallback;
    private readonly EntityStore<T> memoryStore;
    private readonly JsonTypeInfo<T> jsonTypeInfo;
    private readonly VestPocketOptions options;
    private Task flushTask;
    private int unflushed = 0;
    private readonly object writeLock = new();
    private Task rewriteTask;
    private bool isDisposing = false;

    private MemoryStream rewriteTailBuffer;
    private Stream rewriteStream;

    private StoreHeader header;

    public TransactionLog(
        Stream outputStream,
        Func<Stream> rewriteStreamFactory,
        Func<Stream, Stream, Stream> swapRewriteStreamCallback,
        EntityStore<T> memoryStore,
        JsonTypeInfo<T> jsonTypeInfo,
        VestPocketOptions options
        )
    {
        this.outputStream = outputStream;
        this.rewriteStreamFactory = rewriteStreamFactory;
        this.swapRewriteStreamCallback = swapRewriteStreamCallback;
        this.memoryStore = memoryStore;
        this.jsonTypeInfo = jsonTypeInfo;
        this.options = options;
    }

    private async Task Rewrite()
    {
        
        var startingLength = outputStream.Length;

        var itemsRewritten = 0;

        var allItems = memoryStore.GetByPrefix<T>("", false);
        
        Stream stream = rewriteStream;
        if (isDisposing) { return; }

        if (this.header == null)
        {
            this.header = new StoreHeader();
            this.header.Creation = DateTimeOffset.Now;
        }

        if (options.CompressOnRewrite)
        {
            header.CompressedEntities = GetCompressedRewriteSegments(allItems, CancellationToken.None);
        }

        this.header.LastRewrite = DateTimeOffset.Now;

        await JsonSerializer.SerializeAsync(
            rewriteStream,
            header,
            InternalSerializationContext.Default.StoreHeader,
            CancellationToken.None
        );

        rewriteStream.WriteByte(LF);

        if (!options.CompressOnRewrite)
        {
            foreach (var item in allItems)
            {
                if (isDisposing) return;
                JsonSerializer.Serialize(stream, item, jsonTypeInfo);
                stream.WriteByte(LF);
                itemsRewritten++;
            }
        }

        stream.Flush();

        lock (writeLock)
        {
            outputStream = swapRewriteStreamCallback(outputStream, rewriteStream);
            rewriteTailBuffer.WriteTo(outputStream);
            rewriteTailBuffer.Dispose();
            rewriteStream = null;
            rewriteTailBuffer = null;
            header.CompressedEntities = null;
        }


    }

    public void StartFileManagement()
    {
        if (options.ReadOnly) { return; }

        if (flushTask == null)
        {
            flushTask = Task.Run(async () =>
           {
               while (!isDisposing)
               {
                   await Task.Delay(1000);

                   if (unflushed > 0)
                   {
                       lock (writeLock)
                       {
                           if (unflushed == 0)
                           {
                               continue;
                           }

                           if (outputStream == null)
                           {
                               return;
                           }
                           if (outputStream is FileStream fileStream)
                           {
                               fileStream.Flush(true);
                           }
                           else
                           {
                               outputStream.Flush();
                           }

                           unflushed = 0;
                       }
                   }
               }

           });
        }

    }

    private async Task<long> GetNextLineFeedPosition(Stream stream, byte[] buffer)
    {
        long originalPosition = stream.Position;
        try
        {
            int read;
            do
            {
                read = await stream.ReadAsync(buffer);

                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == LF)
                    {
                        // position in stream of this LF is the stream position
                        // minus the bytes read, plus the index of the byte
                        // E.g. stream is at 10,000 position
                        // Read 4000 bytes
                        // index (i) is 2000
                        // This LF would be at stream position 8000

                        return (stream.Position - read) + i;
                    }
                }

            } while (read > 0);

            return -1;
        }
        finally
        {
            stream.Position = originalPosition;
        }

    }

    public async IAsyncEnumerable<T> LoadRecords([EnumeratorCancellation] CancellationToken cancellationToken)
    {

        if (outputStream.Length == 0)
        {
            this.header = new StoreHeader { CompressedEntities = null, Creation = DateTimeOffset.Now, LastRewrite = null };
            yield break;
        }

        var leaveOpen = !options.ReadOnly;

        
        byte[] findNewLineBuffer = new byte[4096];
        var lineView = new ViewStream();

        if (outputStream.Length > 0)
        {
            var firstLinePosition = await GetNextLineFeedPosition(outputStream, findNewLineBuffer);
            lineView.SetStream(outputStream, 0, firstLinePosition);

            this.header = await JsonSerializer.DeserializeAsync(lineView, InternalSerializationContext.Default.StoreHeader, cancellationToken);
            if (header.CompressedEntities != null)
            {
                await foreach (var segment in header.CompressedEntities)
                {
                    using var compressedMemory = new MemoryStream(segment);
                    using var deflate = new BrotliStream(compressedMemory, CompressionMode.Decompress, false);
                    using var entityMemory = new MemoryStream();
                    deflate.CopyTo(entityMemory);
                    entityMemory.Position = 0;

                    await foreach(var entity in ReadEntitiesFromStream(entityMemory, findNewLineBuffer, cancellationToken))
                    {
                        yield return entity;
                    }

                }
            }

        }

        await foreach(var entity in ReadEntitiesFromStream(outputStream, findNewLineBuffer, cancellationToken))
        {
            yield return entity;
        }


    }

    private async IAsyncEnumerable<T> ReadEntitiesFromStream(Stream stream, byte[] findNewLineBuffer, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var viewStream = new ViewStream();

        while(true)
        {
            
            var startPosition = stream.Position;

            if (startPosition == stream.Length)
            {
                break;
            }

            var nextLineFeedPosition = await GetNextLineFeedPosition(stream, findNewLineBuffer);

            if (nextLineFeedPosition == -1)
            {
                nextLineFeedPosition = stream.Length;
            }

            if (nextLineFeedPosition == startPosition)
            {
                stream.ReadByte();
                continue;
            }

            viewStream.SetStream(stream, startPosition, (nextLineFeedPosition - startPosition) + 1);

            T entity = null;
            try
            {
                entity = await JsonSerializer.DeserializeAsync<T>(viewStream, jsonTypeInfo, cancellationToken);
            }
            catch(Exception)
            {
            }
            if (entity != null) 
            {
                yield return entity;
            }

        }
    }

    public void Dispose()
    {
        if (isDisposing) return;
        isDisposing = true;

        if (outputStream == null)
        {
            return;
        }
        lock (writeLock)
        {
            if (unflushed > 0)
            {
                outputStream.Flush();
            }
            outputStream?.Dispose();
            outputStream = null;
        }

        if (rewriteStream != null)
        {
            this.rewriteTask.Wait();
            FileStream streamToCleanup = null;
            if (rewriteStream is FileStream rewriteFileStream)
            {
                streamToCleanup = rewriteFileStream;
            }
            if (rewriteStream is BrotliStream brotliStream &&
                brotliStream.BaseStream is FileStream baseFileStream)
            {
                streamToCleanup = baseFileStream;
            }
            if (streamToCleanup != null)
            {
                streamToCleanup.Close();
                try
                {
                    File.Delete(streamToCleanup.Name);
                }
                catch(Exception)
                {
                }
            }

        }
    }

    public long WriteTransaction(Transaction<T> transaction)
    {
        long bytesWritten = 0;
        if (transaction.IsSingleChange)
        {
            bytesWritten = WriteDocumentChange(transaction.Entity);
        }
        else
        {
            for (int i = 0; i < transaction.Entities.Length; i++)
            {
                bytesWritten += WriteDocumentChange(transaction.Entities[i]);
            }
        }
        transaction.Complete();
        return bytesWritten;
    }

    private const byte LF = 10;

    private long WriteDocumentChange(T entity)
    {
        long startingPosition;
        long endingPosition;

        lock (writeLock)
        {
            startingPosition = outputStream.Position;

            // If the file is blank, dump a header value
            if (startingPosition == 0)
            {
                JsonSerializer.Serialize(outputStream, this.header, InternalSerializationContext.Default.StoreHeader);
                outputStream.WriteByte(LF);
            }

            JsonSerializer.Serialize(outputStream, entity, jsonTypeInfo);
            outputStream.WriteByte(LF);

            var written = outputStream.Position - startingPosition;
            unflushed += (int)written;

            // Check if we need to start rewriting
            if (rewriteTailBuffer == null)
            {
                var ratio = memoryStore.DeadEntityCount / memoryStore.EntityCount;
                if (ratio > options.RewriteRatio && memoryStore.EntityCount > options.RewriteMinimum)
                {
                    rewriteTailBuffer = new MemoryStream();
                    rewriteStream = rewriteStreamFactory();

                    memoryStore.ResetDeadSpace();
                    rewriteTask = Task.Run(Rewrite);
                }
            }

            if (rewriteTailBuffer != null)
            {
                JsonSerializer.Serialize(rewriteTailBuffer, entity, jsonTypeInfo);
                rewriteTailBuffer.WriteByte(LF);
            }

            endingPosition = outputStream.Position;
        }

        return endingPosition - startingPosition;

    }

    public void RemoveAllDocuments()
    {
        lock (writeLock)
        {
            outputStream.SetLength(0);
            outputStream.Flush();
            unflushed = 0;
        }
    }

    public Task ForceMaintenance()
    {
        lock (writeLock)
        {
            //no-op if we are already building a rewrite
            if (rewriteStream == null)
            {
                rewriteTailBuffer = new MemoryStream();
                rewriteStream = rewriteStreamFactory();
                memoryStore.ResetDeadSpace();
                rewriteTask = Task.Run(() => Rewrite());
            }
        }
        return rewriteTask;
    }

    private async IAsyncEnumerable<byte[]> GetCompressedRewriteSegments(T[] entities, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();
        using var serializeStream = new MemoryStream();

        var compressedBackingStream = new MemoryStream();
        var brotliStream = new BrotliStream(compressedBackingStream, CompressionLevel.Optimal);

        for(int i = 0; i < entities.Length; i++)
        {
            
            await JsonSerializer.SerializeAsync<T>(serializeStream, entities[i], jsonTypeInfo, cancellationToken);
            serializeStream.WriteByte(LF);

            // half of the large object heap size
            if (serializeStream.Position > 42_500) 
            {
                serializeStream.SetLength(serializeStream.Position);
                serializeStream.Position = 0;
                await serializeStream.CopyToAsync(brotliStream, cancellationToken);
                serializeStream.Position = 0;
                await brotliStream.FlushAsync(cancellationToken);
                yield return compressedBackingStream.ToArray();

                brotliStream.Dispose();

                compressedBackingStream = new MemoryStream();
                brotliStream = new BrotliStream(compressedBackingStream, CompressionLevel.Optimal);
            }
        }

        if (serializeStream.Position > 0)
        {
            serializeStream.SetLength(serializeStream.Position);
            serializeStream.Position = 0;
            await serializeStream.CopyToAsync(brotliStream, cancellationToken);
            await brotliStream.FlushAsync(cancellationToken);
            yield return compressedBackingStream.ToArray();
        }

        brotliStream.Dispose();

        //Console.WriteLine("Rewrite compression took: " + sw.ElapsedMilliseconds + "ms");
    }

    public Task RewriteTask => rewriteTask;
}