using System.IO;
using System.Text.Json.Serialization.Metadata;

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
    private EntityStore<TEntity> entityStore;
    private string directory;
    private bool disposing = false;

    public bool IsDisposed => this.disposing;
    public long DeadEntityCount => entityStore.DeadEntityCount;
    public long EntityCount => entityStore.EntityCount;
    public double DeadSpacePercentage => entityStore.EntityCount == 0 ?
        0.0 :
        entityStore.DeadEntityCount / entityStore.EntityCount;

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

        this.entityStore = new EntityStore<TEntity>(readOnly: options.ReadOnly);

        TransactionLog<TEntity> transactionStore = null;

        if (options.FilePath == null)
        {

            var memoryStream = new MemoryStream();
            transactionStore = new TransactionLog<TEntity>(
                memoryStream,
                () => new MemoryStream(),
                SwapMemoryRewriteStream,
                entityStore,
                jsonTypeInfo,
                options
            );
        }
        else
        {
            this.directory = Path.GetDirectoryName(options.FilePath);

            var fileAccess = options.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite;
            var fileShare = options.ReadOnly ? FileShare.ReadWrite : FileShare.None;

            var file = new FileStream(options.FilePath, FileMode.OpenOrCreate, fileAccess, fileShare, 4096);

            transactionStore = new TransactionLog<TEntity>(
                file,
                RewriteFileStreamFactory,
                SwapFileRewriteStream,
                entityStore,
                jsonTypeInfo,
                options
                );
        }

        this.transactionStore = transactionStore;

        this.transactionStore.StartFileManagement();
        await this.LoadRecordsFromStore(cancellationToken);

        if (!options.ReadOnly)
        {
            this.transactionQueue = new TransactionQueue<TEntity>(transactionStore, entityStore);
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
        await transaction.Task.ConfigureAwait(false);
        return transaction.Entities;
    }

    public async Task<T> Save<T>(T entity) where T : class, TEntity
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entity);
        transactionQueue.Enqueue(transaction);
        await transaction.Task.ConfigureAwait(false);
        return (T)transaction.Entity;
    }

    public T Get<T>(string key) where T : class, TEntity
    {
        return entityStore.Get(key) as T;
    }

    public PrefixResult<T> GetByPrefix<T>(string prefix) where T : class, TEntity
    {
        return entityStore.GetByPrefix<T>(prefix);
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
        catch (OperationCanceledException)
        {
        }
        catch (AggregateException)
        {

        }

        try
        {
            this.transactionStore.Dispose();
        }
        catch (OperationCanceledException)
        {

        }
        catch (AggregateException)
        {

        }
    }

    public void Clear()
    {
        EnsureWriteAccess();
        try
        {
            this.entityStore.RemoveAllDocuments();
            this.transactionStore.RemoveAllDocuments();
        }
        finally
        {
        }
    }

    public Task ForceMaintenance()
    {
        EnsureWriteAccess();
        return transactionStore.ForceMaintenance();
    }

    public Task CreateBackup(string filePath)
    {
        return transactionStore.CreateBackup(filePath);
    }

    private async Task LoadRecordsFromStore(CancellationToken cancellationToken)
    {
        entityStore.BeginLoading();
        await foreach (var record in this.transactionStore.LoadRecords(cancellationToken))
        {
            entityStore.LoadChange(record);
        }
        entityStore.EndLoading();
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
        var newOutputStream = new FileStream(options.FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);
        newOutputStream.Position = newOutputStream.Length;
        return newOutputStream;
    }

    private Stream RewriteFileStreamFactory()
    {
        var fileName = Guid.NewGuid().ToString() + ".tmp";
        var filePath = Path.Combine(this.directory, fileName);
        var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);
        return fs;

    }

    private static Stream SwapMemoryRewriteStream(Stream outputStream, Stream rewriteStream)
    {
        outputStream.Dispose();
        return rewriteStream;
    }

}