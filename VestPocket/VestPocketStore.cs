using System.IO;
using System.Text.Json.Serialization.Metadata;
using System.IO.Compression;

namespace VestPocket;

/// <summary>
/// Represents access to a VestPocket store.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class VestPocketStore<TEntity> : IDisposable where TEntity : class, IEntity
{
    private readonly VestPocketOptions options;
    private readonly JsonTypeInfo<TEntity> jsonTypeInfo;

    private TransactionQueue<TEntity> transactionQueue;
    private TransactionLog<TEntity> transactionStore;
    private EntityStore<TEntity> memoryStore;
    private string directory;
    private bool disposing = false;

    public bool IsDisposed => this.disposing;
    public long DeadEntityCount => memoryStore.DeadEntityCount;
    public long EntityCount => memoryStore.EntityCount;
    public double DeadSpacePercentage => memoryStore.EntityCount == 0 ?
        0.0 :
        memoryStore.DeadEntityCount / memoryStore.EntityCount;

    public TransactionMetrics TransactionMetrics => this.transactionQueue.Metrics;

    public VestPocketStore(
        JsonTypeInfo<TEntity> jsonTypeInfo, VestPocketOptions options
        )
    {
        this.options = options;
        this.jsonTypeInfo = jsonTypeInfo;
        ValidateOptions();
    }

    private void ValidateOptions()
    {
        var optionError = options.Validate();

        if (optionError != null)
        {
            throw new Exception($"VestPocketOptions are not valid: {optionError}");
        }
    }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {

        this.memoryStore = new EntityStore<TEntity>(synchronized: !options.ReadOnly);

        TransactionLog<TEntity> transactionStore = null;

        if (options.FilePath == null)
        {

            var memoryStream = new MemoryStream();
            transactionStore = new TransactionLog<TEntity>(
                memoryStream,
                () => new MemoryStream(),
                SwapMemoryRewriteStream,
                memoryStore,
                jsonTypeInfo,
                options
            );
        }
        else
        {
            this.directory = Path.GetDirectoryName(options.FilePath);

            var fileAccess = options.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite;
            var fileShare = options.ReadOnly ? FileShare.ReadWrite : FileShare.None;

            var file = new FileStream(options.FilePath, FileMode.OpenOrCreate, fileAccess, fileShare, 65536);

            transactionStore = new TransactionLog<TEntity>(
                file,
                RewriteFileStreamFactory,
                SwapFileRewriteStream,
                memoryStore,
                jsonTypeInfo,
                options
                );
        }

        this.transactionStore = transactionStore;

        this.transactionStore.StartFileManagement();
        await this.LoadRecordsFromStore(cancellationToken);

        if (!options.ReadOnly)
        {
            this.transactionQueue = new TransactionQueue<TEntity>(transactionStore, memoryStore);
            await this.transactionQueue.Start();
        }

    }

    /// <summary>
    /// Closes the connection to the VestPocket store. This will stop pending transactions and
    /// wait for any pending rewrite task to complete
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task Close(CancellationToken cancellationToken)
    {
        this.disposing = true;
        
        if (!this.options.ReadOnly)
        {
            await this.transactionQueue.Stop();
        }

        this.transactionStore.Dispose();

        if (this.transactionStore.RewriteTask != null && !this.transactionStore.RewriteTask.IsCompleted)
        {
            await this.transactionStore.RewriteTask;
        }
    }

    public async Task<TEntity[]> Save(TEntity[] entities)
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entities);
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
        return transaction.Entities;
    }

    public async Task<T> Save<T>(T entity) where T : class, TEntity
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entity);
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
        return (T)transaction.Entity;
    }

    public T Get<T>(string key) where T : class, TEntity
    {
        return memoryStore.Get(key) as T;
    }

    public T[] GetByPrefix<T>(string prefix, bool sortResults) where T : class, TEntity
    {
        return memoryStore.GetByPrefix<T>(prefix, sortResults);
    }

    public void Dispose()
    {
        if (disposing)
        {
            return; // Already disposing
        }
        disposing = true;
        try
        {
            this.transactionQueue.Stop().Wait();
        }
        catch(OperationCanceledException)
        {
        }
        catch(AggregateException)
        {

        }

        try
        {
            this.transactionStore.Dispose();
        }
        catch(OperationCanceledException)
        {

        }
        catch (AggregateException)
        {

        }
    }

    public void Clear()
    {
        EnsureWriteAccess();
        this.memoryStore.Lock();
        try
        {
            this.memoryStore.RemoveAllDocuments();
            this.transactionStore.RemoveAllDocuments();
        }
        finally
        {
            this.memoryStore.Unlock();
        }
    }

    public Task ForceMaintenance()
    {
        EnsureWriteAccess();
        return transactionStore.ForceMaintenance();
    }

    private async Task LoadRecordsFromStore(CancellationToken cancellationToken)
    {
        await foreach (var record in this.transactionStore.LoadRecords(cancellationToken))
        {
            memoryStore.LoadChange(record.Key, record);
        }
    }

    private void EnsureWriteAccess()
    {
        if (options.ReadOnly)
        {
            throw new Exception("Cannot apply changes to this store, as it was opened with the ReadOnly option");
        }
    }

    private Stream SwapFileRewriteStream(Stream outputStream, Stream rewriteStream)
    {

        string rewriteFilePath;

        var rewriteFileStream = (FileStream)rewriteStream;
        rewriteFilePath = rewriteFileStream.Name;

        rewriteFileStream.Close();
        outputStream.Close();

        File.Move(rewriteFilePath, options.FilePath, true);
        var newOutputStream = new FileStream(options.FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 65536);
        newOutputStream.Position = newOutputStream.Length;
        return newOutputStream;
    }

    private Stream RewriteFileStreamFactory()
    {
        var fileName = Guid.NewGuid().ToString() + ".tmp";
        var filePath = Path.Combine(this.directory, fileName);
        var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 65536);
        return fs;
        //if (options.CompressOnRewrite)
        //{
        //    var compressStream = new BrotliStream(fs, CompressionLevel.Fastest);
        //    return compressStream;
        //}
        //else
        //{
        //    return fs;
        //}
    
    }

    private static Stream SwapMemoryRewriteStream(Stream outputStream, Stream rewriteStream)
    {
        outputStream.Dispose();
        return rewriteStream;
    }

}