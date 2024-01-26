using System.Buffers;
using System.IO;

namespace VestPocket;

/// <summary>
/// Represents access to a VestPocket store for storing entities that inherit from TEntity.
/// This is the main API for interacting with a VestPocket store.
/// </summary>
public class VestPocketStore : IDisposable
{
    private readonly VestPocketOptions options;
    //private readonly JsonWriterOptions jsonWriterOptions;
    //private const byte LF_Byte = 10;

    private TransactionQueue transactionQueue;
    private TransactionLog transactionStore;
    private EntityStore entityStore;
    private string directory;
    private bool disposing = false;

    private RecordSerializerFactory recordSerializerFactory;

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
    /// <param name="options">The configuration options to use with this VestPocketStore when opening it</param>
    public VestPocketStore(VestPocketOptions options)
    {
        this.options = options;
        ValidateOptions();
        this.options.Freeze();
        this.recordSerializerFactory = new(options);
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

        this.entityStore = new EntityStore(readOnly: options.ReadOnly);

        TransactionLog transactionStore = null;

        if (options.FilePath == null)
        {
            var memoryStream = new MemoryStream();
            transactionStore = new TransactionLog(
                this,
                memoryStream,
                () => new MemoryStream(),
                SwapMemoryRewriteStream,
                entityStore,
                options,
                new RecordSerializerFactory(options)
            );
        }
        else
        {
            this.directory = Path.GetDirectoryName(options.FilePath);

            var fileAccess = options.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite;
            var fileShare = options.ReadOnly ? FileShare.ReadWrite : FileShare.None;

            var file = new FileStream(options.FilePath, FileMode.OpenOrCreate, fileAccess, fileShare, 4096);

            transactionStore = new TransactionLog(
                this,
                file,
                RewriteFileStreamFactory,
                SwapFileRewriteStream,
                entityStore,
                options, 
                new RecordSerializerFactory(options)
                );
        }

        this.transactionStore = transactionStore;
        await this.LoadRecordsFromStore(cancellationToken);

        if (!options.ReadOnly)
        {
            this.transactionQueue = new TransactionQueue(transactionStore, entityStore);
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
    public async Task Save(Kvp[] entities)
    {
        EnsureWriteAccess();
        var transaction = new MultipleTransaction(entities, true);
        transaction.Utf8JsonPayload = SerializeRecords(entities); ;
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
        ArrayPool<byte>.Shared.Return(transaction.Utf8JsonPayload.Array);
    }

    //public async Task Delete<T>(T entity) where T : class, TEntity
    //{
    //    await Update(null, entity);
    //}

    /// <summary>
    /// Saves an entity to the store, but only if the entity in the store
    /// still matches the entity that this change was based off of. Throws a ConcurrencyException
    /// if the entity currently stored does not match the basedOn parameter.
    /// </summary>
    /// <typeparam name="T">The type of entity to save</typeparam>
    /// <param name="entity">The entity to save</param>
    /// <param name="basedOn">The entity that is expected to currently to be in the store that
    /// the changes to the 'entity' parameter were applied to. Pass as null if no stored value is expected.</param>
    public async Task Update<T>(Kvp entity, T basedOn)
    {
        var transaction = new UpdateTransaction(entity, basedOn, true);
        EnsureWriteAccess();
        transaction.Utf8JsonPayload = SerializeRecord(entity);
        transactionQueue.Enqueue(transaction);
        await transaction.Task.ConfigureAwait(false);
        ArrayPool<byte>.Shared.Return(transaction.Utf8JsonPayload.Array);

    }

    /// <summary>
    /// Saves an entity to the store, but only if the entity in the store
    /// still matches the entity that this change was based on. This is similar to 
    /// a compare and swap operation.
    /// </summary>
    /// <typeparam name="T">The type of entity to save</typeparam>
    /// <param name="entity">The entity to save</param>
    /// <param name="basedOn">The entity that is expected to currently to be in the store that
    /// the changes to the 'entity' parameter were applied to. Pass as null if no stored value is expected.</param>
    /// <returns>The entity stored for the key after the operation</returns>
    public async Task<T> Swap<T>(Kvp entity, T basedOn)
    {
        EnsureWriteAccess();
        var transaction = new UpdateTransaction(entity, basedOn, false);
        transaction.Utf8JsonPayload = SerializeRecord(entity);
        transactionQueue.Enqueue(transaction);
        await transaction.Task.ConfigureAwait(false);
        ArrayPool<byte>.Shared.Return(transaction.Utf8JsonPayload.Array);
        return (T)transaction.Record.Value;
    }

    private ArraySegment<byte> SerializeRecords(Kvp[] entities)
    {
        var serializer = recordSerializerFactory.Create();
        foreach (var entity in entities)
        {
            serializer.Serialize(entity.Key, entity.Value);
        }
        return serializer.RentWrittenBuffer();
    }

    private ArraySegment<byte> SerializeRecord(Kvp record)
    {
        var serializer = recordSerializerFactory.Create();
        serializer.Serialize(record.Key, record.Value);
        return serializer.RentWrittenBuffer();
    }

    /// <summary>
    /// Saves an entity to the store.
    /// </summary>
    /// <param name="entity">The entity to save to the store</param>
    /// <returns>The entity that was saved in the store with an updated version</returns>
    /// <exception>ConcurrencyException</exception>
    public async Task<Kvp> Save(Kvp entity)
    {
        EnsureWriteAccess();
        var transaction = new SingleTransaction(entity, true);
        transaction.Utf8JsonPayload = SerializeRecord(entity);
        transactionQueue.Enqueue(transaction);
        await transaction.Task.ConfigureAwait(false);
        ArrayPool<byte>.Shared.Return(transaction.Utf8JsonPayload.Array);
        return transaction.Entity;
    }


    /// <summary>
    /// Looks up an entity by key
    /// </summary>
    /// <typeparam name="T">The type of the entity</typeparam>
    /// <param name="key">The exact key of the entity to find</param>
    /// <returns>The entity stored with the key specified or null</returns>
    public T Get<T>(string key) where T : class
    {
        return entityStore.Get(key) as T;
    }

    /// <summary>
    /// Looks up an entity by key. Returned as a base entity type
    /// </summary>
    /// <param name="key">The exact key of the entity to find</param>
    /// <returns>The entity stored with the key specified or null</returns>
    public Kvp Get(string key)
    {
        return new Kvp(key, entityStore.Get(key));
    }

    /// <summary>
    /// Performs a prefix search for values that have keys starting with the supplied 
    /// prefix value.
    /// </summary>
    /// <param name="prefix">The case sensitive key prefix to search for</param>
    public PrefixLookup<object>.Enumerator GetByPrefix(string prefix)
    {
        return entityStore.GetByPrefix(prefix);
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

    /// <summary>
    /// Creates a backup of a VestPocket store to a writable System.IO.Stream. This can be used
    /// while the store is still open to make a copy of the store to a location other than
    /// to disk.
    /// </summary>
    /// <param name="stream">The stream to write the backup to</param>
    public Task CreateBackup(Stream stream)
    {
        return transactionStore.CreateBackup(stream, true);
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
        var transaction = new NoOpTransaction();
        transactionQueue.Enqueue(transaction);
        await transaction.Task;
    }

}