using System.IO;
using System.Text.Json.Serialization.Metadata;

namespace VestPocket;

/// <summary>
/// Represents access to a VestPocket store for storing entities that inherit from TEntity.
/// This is the main API for interacting with a VestPocket store.
/// </summary>
/// <typeparam name="TEntity">The base entity type of the VestPocketStore</typeparam>
public class VestPocketStore<TEntity> : IDisposable where TEntity : class, IEntity
{
    private readonly VestPocketOptions options;
    private readonly JsonTypeInfo<TEntity> jsonTypeInfo;

    private TransactionQueue<TEntity> transactionQueue;
    private TransactionLog<TEntity> transactionStore;
    private EntityStore<TEntity> entityStore;
    private string directory;
    private bool disposing = false;

    /// <summary>
    /// If this store has already started disposing
    /// </summary>
    public bool IsDisposed => this.disposing;
    
    /// <summary>
    /// The number of dead (deleted or out of date) entities currently in the store
    /// </summary>
    public long DeadEntityCount => entityStore.DeadEntityCount;
    
    /// <summary>
    /// The number of entities currently in the store
    /// </summary>
    public long EntityCount => entityStore.EntityCount;
    
    /// <summary>
    /// The ratio of dead entities to living entities in the store
    /// </summary>
    public double DeadSpacePercentage => entityStore.EntityCount == 0 ?
        0.0 :
        entityStore.DeadEntityCount / entityStore.EntityCount;

    /// <summary>
    /// Metrics about transactions performed on this store since it was opened.
    /// </summary>
    public TransactionMetrics TransactionMetrics => this.transactionQueue.Metrics;

    /// <summary>
    /// Instantiates a new instance of the VestPocketStore class
    /// </summary>
    /// <param name="jsonTypeInfo">The JsonTypeInfo to use for serialization of entities inheriting from TEntity</param>
    /// <param name="options">The configuration options to use with this VestPocketStore when opening it</param>
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

    /// <summary>
    /// Opens the VestPocketStore. This will load records from the store if a file path has been given
    /// and will begin processing request methods called on the VestPocketStore as well as starting
    /// background tasks to monitor the store for maintenance. If a file path is provided, and the
    /// file does not exist, it will be created. If the store is opened for editing, the file will
    /// be opened with an exclusive lock.
    /// </summary>
    /// <param name="cancellationToken">
    /// Loading and parsing records from disk can take time. This cancellation token can be used to enforce a loading timeout,
    /// if desired.
    /// </param>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {

        this.entityStore = new EntityStore<TEntity>(readOnly: options.ReadOnly);

        TransactionLog<TEntity> transactionStore = null;

        if (options.FilePath == null)
        {
            var memoryStream = new MemoryStream();
            transactionStore = new TransactionLog<TEntity>(
                this,
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
                this,
                file,
                RewriteFileStreamFactory,
                SwapFileRewriteStream,
                entityStore,
                jsonTypeInfo,
                options
                );
        }

        this.transactionStore = transactionStore;
        await this.LoadRecordsFromStore(cancellationToken);

        if (!options.ReadOnly)
        {
            this.transactionQueue = new TransactionQueue<TEntity>(transactionStore, entityStore, options);
            await this.transactionQueue.Start();
        }

    }

    /// <summary>
    /// Closes the connection to the VestPocket store. This will stop pending transactions and
    /// wait for any pending rewrite task to complete
    /// </summary>
    /// <param name="cancellationToken"></param>
    ///<remarks>
    /// The CancellationToken paramter is not currently utilized, but may be in the future
    ///</remarks>
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

    /// <summary>
    /// Takes an array of entities and saves them as a transaction to the store.
    /// </summary>
    /// <param name="entities">The entities to save</param>
    /// <returns>
    /// Returns an array that contains the saved entities with their new version numbers.
    /// </returns>
    /// <exception>ConcurrencyException</exception>
    public async Task<TEntity[]> Save(TEntity[] entities)
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entities, true);
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
        return transaction.Entities;
    }

    /// <summary>
    /// Tries to save an array of entities as a transaction to the store.
    /// Does not throw an optimistic concurrency exception if entities fail to save
    /// due to having stale versions. 
    /// </summary>
    /// <typeparam name="T">The type of entities to save</typeparam>
    /// <param name="entities">The entities to save to the store</param>
    /// <returns>True if all the entities saved successfully or false if the transaction failed due to any entity being out of date.</returns>
    public async Task<bool> TrySave<T>(T[] entities) where T : class, TEntity
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entities, false);
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
        return !transaction.FailedConcurrency;
    }

    /// <summary>
    /// Saves an entity to the store. Will throw a ConcurrencyException if the version of the entity saved
    /// is not the current version in the store
    /// </summary>
    /// <typeparam name="T">The type of entity to save</typeparam>
    /// <param name="entity">The entity to save to the store</param>
    /// <returns>The entity that was saved in the store with an updated version</returns>
    /// <exception>ConcurrencyException</exception>
    public async Task<T> Save<T>(T entity) where T : class, TEntity
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entity, true);
        transactionQueue.Enqueue(transaction);
        await transaction.Task.ConfigureAwait(false);
        return (T)transaction.Entity;
    }

    /// <summary>
    /// Saves an entity to the store. Will not throw exceptions if the entity fails to save due to
    /// concurrency issues.
    /// </summary>
    /// <typeparam name="T">The type of the entity to save</typeparam>
    /// <param name="entity">The entity to save to the store</param>
    /// <returns>True if the entity was saved or false if it failed to save</returns>
    public async Task<bool> TrySave<T>(T entity) where  T : class, TEntity
    {
        EnsureWriteAccess();
        var transaction = new Transaction<TEntity>(entity, false);
        transactionQueue.Enqueue(transaction);
        await transaction.Task.ConfigureAwait(false);
        return !transaction.FailedConcurrency;
    }

    /// <summary>
    /// Looks up an entity by key
    /// </summary>
    /// <typeparam name="T">The type of the entity</typeparam>
    /// <param name="key">The exact key of the entity to find</param>
    /// <returns>The entity stored with the key specified or null</returns>
    public T Get<T>(string key) where T : class, TEntity
    {
        return entityStore.Get(key) as T;
    }

    /// <summary>
    /// Performs a prefix search for values that have keys starting with the supplied 
    /// prefix value.
    /// </summary>
    /// <typeparam name="T">The type of entities to retrieve</typeparam>
    /// <param name="prefix">The case sensitive key prefix to search for</param>
    /// <returns>tThe search results. PrefixResults implement IDisposable</returns>
    public IEnumerable<T> GetByPrefix<T>(string prefix) where T : class, TEntity
    {
        return entityStore.GetByPrefix<T>(prefix);
    }
    
    /// <summary>
    /// Disposes of the VestPocketStore. This stops processing queued requests and closes
    /// the access to the underlying file of the store (if any).
    /// </summary>
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

    /// <summary>
    /// A dangerous method that clears all records from the store that exists primarily for testing purposes.
    /// </summary>
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

    /// <summary>
    /// Forces the store to rewrite itself and clear out any dead entities.
    /// This is performed automatically as the store grows in size, so calling this method
    /// manually is usually not required.
    /// </summary>
    /// <returns></returns>
    public Task ForceMaintenance()
    {
        EnsureWriteAccess();
        return transactionStore.ForceMaintenance();
    }

    /// <summary>
    /// Creates a backup file. A writeable VestPocketStore will not share access to
    /// to its backing file. This method can be used while the store is still open to make a copy of the store.
    /// </summary>
    /// <param name="filePath">The path to write the backup store file to</param>
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

    internal async Task QueueNoOpTransaction()
    {
        var transaction = new Transaction<TEntity>();
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
    }

}