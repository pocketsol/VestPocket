using TrieHard.PrefixLookup;

namespace VestPocket;

/// <summary>
/// A light transactional wrapper around storing entities in memory.
/// The in-memory representation is implemented using a <see cref="PrefixLookup{T}"/>.
/// </summary>
internal class EntityStore<T> : IDisposable where T : class, IEntity
{

    /// <summary>
    /// For performance reasons we'll leak the implementation of the backing lookup
    /// </summary>
    internal readonly PrefixLookup<T> Lookup;

    private bool readOnly;
    private bool originalReadOnly;
    private long deadEntityCount;
    private long entityCount;
    public long DeadEntityCount => deadEntityCount;
    public long EntityCount => entityCount;

    private bool disposed = false;
    public EntityStore(bool readOnly)
    {
        Lookup = new();
        this.readOnly = readOnly;
        this.originalReadOnly = readOnly;
    }

    /// <summary>
    /// Temporarily allows changes to this entity store.
    /// Useful to load the initial records from disk.
    /// </summary>
    internal void BeginLoading()
    {
        readOnly = false;
    }

    internal void EndLoading()
    {
        readOnly = this.originalReadOnly;
    }

    /// <summary>
    /// Retrieves an entity by its key value, using an exact (case sensitive) match.
    /// </summary>
    /// <param name="key">The key of the entity to find</param>
    /// <returns>Returns an Entity of type T, or null if the entity is not found or has been deleted</returns>
    public T Get(string key)
    {
        var result = Lookup[key];
        if (result == null) { return null; }
        return result.Deleted ? null : result;
    }

    /// <summary>
    /// Retrieves an IEnumerable&lt;T&gt; of all entities that have keys that start with the exact string value of <paramref name="prefix"/>
    /// </summary>
    /// <typeparam name="TSelection">The type that the Entity should be selected as. Must be castable from <typeparamref name="T"/></typeparam>
    /// <param name="prefix">The prefix that will be used to search for matches.</param>
    /// <returns></returns>
    public IEnumerable<TSelection> GetByPrefix<TSelection>(string prefix) where TSelection : class, T
    {
        return Lookup.SearchValues(prefix).Cast<TSelection>();
    }

    /// <summary>
    /// Retrieves an IEnumerable&lt;T&gt; of all entities that have keys that start with the exact string value of <paramref name="prefix"/>
    /// </summary>
    /// <param name="prefix">The prefix that will be used to search for matches.</param>
    /// <returns></returns>
    public PrefixLookup<T>.ValueEnumerator GetByPrefix(string prefix)
    {
        return Lookup.SearchValues(prefix);
    }

    /// <summary>
    /// Applies a transaction, with one or more changes, to the entity store and sets any concurrency errors encountered.
    /// </summary>
    /// <param name="transaction">The transaction to apply to the Entity Store</param>
    /// <returns>Returns true if the transaction was applied without encountering a <see cref="ConcurrencyException"/>, false if one was set on the result of the transaction.</returns>
    public bool ProcessTransaction(Transaction<T> transaction)
    {
        if (readOnly) throw new InvalidOperationException("The store is marked readonly and cannot accept transactions");
        if (transaction.IsSingleChange)
        {
            var entity = transaction.Entity;

            var existingDocument = Lookup[entity.Key];
            if (existingDocument != null)
            {

                if (
                    (existingDocument.Version > entity.Version) ||
                    (existingDocument.Deleted && entity.Version != 0)
                    )
                {
                    transaction.Entity = existingDocument;
                    if (transaction.ThrowOnError)
                    {
                        transaction.SetError(new ConcurrencyException(entity.Key, entity.Version, existingDocument.Version));
                    }
                    else
                    {
                        transaction.FailedConcurrency = true;
                        transaction.Complete();
                    }
                    return false;
                }
                deadEntityCount++;
            }
            else
            {
                entityCount++;
            }

            entity = (T)entity.WithVersion(entity.Version + 1);
            Lookup[entity.Key] = entity;
            //Lookup.Set(entity.Key, entity);

            transaction.Entity = entity;
        }
        else
        {
            int deadEntitiesInExisting = 0;
            int newEntitiesCount = 0;
            bool failedTrySave = false;
            for (int i = 0; i < transaction.Entities.Length; i++)
            {
                var entity = transaction.Entities[i];
                var existingDocument = Lookup[entity.Key];

                if (existingDocument != null)
                {
                    if (
                        (existingDocument.Version > entity.Version) ||
                        (existingDocument.Deleted && entity.Version != 0)
                        )
                    {
                        transaction.Entities[i] = existingDocument;
                        if (transaction.ThrowOnError)
                        {
                            transaction.SetError(new ConcurrencyException(entity.Key, entity.Version, existingDocument.Version));
                            return false;
                        }
                        else
                        {
                            failedTrySave = true;
                            continue;
                        }

                    }
                    deadEntitiesInExisting++;
                }
                else
                {
                    newEntitiesCount++;
                }
            }
            if (failedTrySave )
            {
                transaction.FailedConcurrency = true;
                transaction.Complete();
                return false;
            }
            this.deadEntityCount += deadEntitiesInExisting;
            this.entityCount += newEntitiesCount;

            for (int i = 0; i < transaction.Entities.Length; i++)
            {
                var change = transaction.Entities[i];
                change = (T)change.WithVersion(change.Version + 1);
                Lookup[change.Key] = change;
                transaction.Entities[i] = change;
            }
        }

        return true;
    }

    /// <summary>
    /// Loads an entity into the Entity Store without using a transaction wrapper.
    /// This should only be called while initially loading the Entity Store from a known source
    /// (such as a VestPocket File). It should not be used in other situations, as it does not enforce
    /// optimistic concurrency. Changes applied in this way are also not written to the <see cref="TransactionLog{T}"/>
    /// </summary>
    /// <param name="entity">The entity to load into the store</param>
    public void LoadChange(T entity)
    {

        var existingRecord = Lookup[entity.Key];
        if (existingRecord != null)
        {
            if (entity.Version > existingRecord.Version)
            {
                Lookup[entity.Key] = entity;
            }
            deadEntityCount++;
        }
        else
        {
            entityCount++;
            Lookup[entity.Key] = entity;
        }
    }


    public void RemoveAllDocuments()
    {
        Lookup.Clear();
    }

    public void IncrementDeadEntities()
    {
        deadEntityCount++;
    }

    public void ResetDeadSpace()
    {
        deadEntityCount = 0;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            //Lookup.Dispose();
        }
    }
}
