﻿using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.IO.Compression;
using System.Buffers;
using System.Text.Json.Serialization;

namespace VestPocket;

/// <summary>
/// A store that serializes transactions by appending them to the
/// end of a file and can only be be loaded by loading the entire file.
/// Shrinking removed data from the file doubles as backing up the transaction
/// store, as it requires rewriting.
/// </summary>
internal class TransactionLog : IDisposable
{
    private readonly VestPocketStore store;
    private Stream outputStream;
    private FileStream fileOutputStream;
    private readonly Func<Stream> rewriteStreamFactory;
    private readonly Func<Stream, Stream, Stream> swapRewriteStreamCallback;
    private readonly EntityStore memoryStore;
    private readonly JsonSerializerContext jsonSerializerContext;
    private readonly VestPocketOptions options;
    private int unflushed = 0;
    private readonly object rewriteLock = new();
    private Task rewriteTask;
    private bool isDisposing = false;

    private MemoryStream rewriteTailBuffer;
    private Stream rewriteStream;
    private bool keepRewriteStreamOpen = false;

    private StoreHeader header;

    private bool rewriteReadyToComplete = false;
    private bool rewriteIsBackup = false;

    private bool hasFlushableOutput;
    private bool flushIntermediateFileBuffers;

    private RecordSerializer recordSerializer;

    public bool RewriteReadyToComplete { get => rewriteReadyToComplete; }

    public TransactionLog(
        VestPocketStore store,
        Stream outputStream,
        Func<Stream> rewriteStreamFactory,
        Func<Stream, Stream, Stream> swapRewriteStreamCallback,
        EntityStore memoryStore,
        VestPocketOptions options
        )
    {
        this.store = store;
        this.outputStream = outputStream;
        this.fileOutputStream = outputStream as FileStream;
        this.rewriteStreamFactory = rewriteStreamFactory;
        this.swapRewriteStreamCallback = swapRewriteStreamCallback;
        this.memoryStore = memoryStore;
        this.jsonSerializerContext = options.JsonSerializerContext;
        this.options = options;
        this.hasFlushableOutput = fileOutputStream != null;
        this.flushIntermediateFileBuffers = hasFlushableOutput && 
            options.Durability != VestPocketDurability.FileSystemCache;
        this.recordSerializer = new(options);
    }

    private void Rewrite(bool isBackup)
    {
        rewriteReadyToComplete = false;
        rewriteIsBackup = isBackup;

        var itemsRewritten = 0;


        Stream stream = rewriteStream;
        if (isDisposing) { return; }

        if (this.header == null)
        {
            this.header = new StoreHeader();
            this.header.Creation = DateTimeOffset.Now;
        }

        if (options.CompressOnRewrite)
        {
            var allItems = memoryStore.Lookup.SearchValues(ReadOnlySpan<byte>.Empty);
            header.CompressedEntities = GetCompressedRewriteSegments(allItems, CancellationToken.None);
        }

        this.header.LastRewrite = DateTimeOffset.Now;

        JsonSerializer.Serialize(
            rewriteStream,
            header,
            InternalSerializationContext.Default.StoreHeader
        );

        rewriteStream.Write(LF, 0, 1);

        if (!options.CompressOnRewrite)
        {
            recordSerializer.Reset();

            foreach (var item in memoryStore.Lookup.Search(ReadOnlySpan<byte>.Empty))
            {
                recordSerializer.Serialize(item.Key, item.Value);
                if (recordSerializer.Written > 16_384)
                {
                    if (isDisposing) return;
                    recordSerializer.WriteToStream(stream);
                }
                itemsRewritten++;
            }
            recordSerializer.WriteToStream(stream);
        }

        stream.Flush();
        rewriteReadyToComplete = true;

    }

    public void CompleteRewrite()
    {
        lock (rewriteLock)
        {
            if (this.rewriteIsBackup)
            {
                rewriteTailBuffer.WriteTo(rewriteStream);
                if (!keepRewriteStreamOpen)
                {
                    rewriteStream.Close();
                }
            }
            else
            {
                outputStream = swapRewriteStreamCallback(outputStream, rewriteStream);
                fileOutputStream = outputStream as FileStream;
                rewriteTailBuffer.WriteTo(outputStream);
            }
            rewriteTailBuffer.Dispose();
            rewriteStream = null;
            rewriteTailBuffer = null;
            header.CompressedEntities = null;
            this.rewriteReadyToComplete = false;
            this.rewriteIsBackup = false;
        }
    }

    public void ProcessDelay()
    {
        Flush();
        CheckForMaintenance();
    }

    public void Flush()
    {
        if (hasFlushableOutput)
        {
            lock (rewriteLock)
            {
                fileOutputStream.Flush(this.flushIntermediateFileBuffers);
            }
            store.TransactionMetrics.flushCount += 1;
        }
    }

    private long GetNextLineFeedPosition(Stream stream, byte[] buffer)
    {
        long originalPosition = stream.Position;
        try
        {
            int read;
            do
            {
                read = stream.Read(buffer);

                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == LF_Byte)
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

    public async IAsyncEnumerable<Kvp> LoadRecords([EnumeratorCancellation] CancellationToken cancellationToken)
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
            var firstLinePosition = GetNextLineFeedPosition(outputStream, findNewLineBuffer);
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

                    await foreach (var entity in ReadEntitiesFromStream(entityMemory, findNewLineBuffer, cancellationToken))
                    {
                        yield return entity;
                    }

                }
            }
        }

        await foreach (var entity in ReadEntitiesFromStream(outputStream, findNewLineBuffer, cancellationToken))
        {
            yield return entity;
        }


    }

    private async IAsyncEnumerable<Kvp> ReadEntitiesFromStream(Stream stream, byte[] findNewLineBuffer, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var viewStream = new ViewStream();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);

        while (true)
        {

            var startPosition = stream.Position;

            if (startPosition == stream.Length)
            {
                break;
            }

            var nextLineFeedPosition = GetNextLineFeedPosition(stream, findNewLineBuffer);

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

            Kvp record = default;
            try
            {
                var lengthToRead = Convert.ToInt32(viewStream.Length);
                if (buffer.Length < lengthToRead)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = ArrayPool<byte>.Shared.Rent(lengthToRead);
                }
                await viewStream.ReadAsync(buffer, 0, lengthToRead);
                record = DeserializeRecordFromBuffer(buffer, lengthToRead);
            }
            catch (Exception)
            {
            }
            if (record.Value != null)
            {
                yield return record;
            }
        }
    }

    private Kvp DeserializeRecordFromBuffer(byte[] buffer, int length)
    {
        // TODO: this isn't a ref struct anymore, doesn't need to be retreived every method call
        // (which is what we were doing because caller was async method).
        var sequence = new ReadOnlySequence<byte>(buffer, 0, length);
        return recordSerializer.Deserialize(sequence);
    }

    public void Dispose()
    {
        if (isDisposing) return;
        isDisposing = true;

        if (outputStream == null)
        {
            return;
        }
        lock (rewriteLock)
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
                catch (Exception)
                {
                }
            }

        }
    }

    public long WriteTransaction(Transaction transaction)
    {
        long bytesWritten = WriteDocumentPayload(transaction.Utf8JsonPayload);
        transaction.Complete();
        if (options.Durability == VestPocketDurability.FlushEachTransaction)
        {
            Flush();
        }
        return bytesWritten;
    }

    private readonly byte[] LF = new byte[] { 10 };
    private const byte LF_Byte = 10;

    public long WriteDocumentPayload(ReadOnlySpan<byte> payload)
    {
        long startingPosition;
        long endingPosition;

        startingPosition = outputStream.Position;

        // If the file is blank, dump a header value
        if (startingPosition == 0)
        {
            JsonSerializer.Serialize(outputStream, this.header, InternalSerializationContext.Default.StoreHeader);
            outputStream.Write(LF, 0, 1);
        }
        outputStream.Write(payload);

        var written = outputStream.Position - startingPosition;
        unflushed += (int)written;

        if (rewriteTailBuffer != null)
        {
            if (rewriteTailBuffer != null)
            {
                outputStream.Write(payload);
            }
        }

        endingPosition = outputStream.Position;

        return endingPosition - startingPosition;
    }

    public void CheckForMaintenance()
    {
        // Check if we need to start rewriting
        if (rewriteTailBuffer == null)
        {
            var ratio = memoryStore.DeadEntityCount / memoryStore.EntityCount;
            if (ratio > options.RewriteRatio && memoryStore.EntityCount > options.RewriteMinimum)
            {
                lock (rewriteLock)
                {
                    if (rewriteTailBuffer == null)
                    {
                        rewriteTailBuffer = new MemoryStream();
                        rewriteStream = rewriteStreamFactory();

                        memoryStore.ResetDeadSpace();
                        rewriteTask = Task.Run(() => Rewrite(false));
                    }
                }

            }
        }
    }

    public void RemoveAllDocuments()
    {
        lock (rewriteLock)
        {
            outputStream.SetLength(0);
            outputStream.Flush();
            unflushed = 0;
        }
    }

    public async Task ForceMaintenance()
    {
        lock (rewriteLock)
        {
            //no-op if we are already building a rewrite
            if (rewriteTailBuffer == null)
            {
                rewriteTailBuffer = new MemoryStream();
                rewriteStream = rewriteStreamFactory();
                memoryStore.ResetDeadSpace();
                rewriteTask = Task.Run(() => Rewrite(false));
            }
        }
        await rewriteTask;
        await store.QueueNoOpTransaction();
    }

    public async Task CreateBackup(Stream backupStream, bool keepStreamOpen)
    {
        while (true)
        {
            if (rewriteTask != null && !rewriteTask.IsCompleted)
            {
                await rewriteTask;
            }

            // We can't lock and start a backup rewrite when
            // a rewrite is ongoing, but we also can't guarantee
            // another thread won't start a rewrite after we await
            // the rewrite task
            lock (rewriteLock)
            {
                if (rewriteTailBuffer != null) continue;
                this.keepRewriteStreamOpen = keepStreamOpen;
                rewriteTailBuffer = new MemoryStream();
                rewriteStream = backupStream;
                rewriteTask = Task.Run(() => Rewrite(true));
            }

            await rewriteTask;
            await store.QueueNoOpTransaction();
            break;
        }
    }

    public async Task CreateBackup(string filePath)
    {
        var backupFileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await CreateBackup(backupFileStream, false);
    }

    private async IAsyncEnumerable<byte[]> GetCompressedRewriteSegments(IEnumerable<object> entities, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var serializeStream = new MemoryStream();
        var compressedBackingStream = new MemoryStream();
        var brotliStream = new BrotliStream(compressedBackingStream, CompressionLevel.Optimal);

        foreach (var entity in entities)
        {
            await JsonSerializer.SerializeAsync(serializeStream, entity, entity.GetType(), jsonSerializerContext, cancellationToken);
            serializeStream.Write(LF, 0, 1);

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
    }

    public Task RewriteTask => rewriteTask;
}