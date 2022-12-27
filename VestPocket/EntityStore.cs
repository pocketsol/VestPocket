namespace VestPocket;

/// <summary>
/// A light transactional wrapper around storing entities in memory.
/// The in-memory representation is implemented using a <see cref="PrefixLookup{T}"/>.
/// </summary>
internal class EntityStore<T> where T : class, IEntity
{

    private readonly PrefixLookup<T> lookup;
    private readonly bool readOnly;
    private long deadEntityCount;
    private long entityCount;
    public long DeadEntityCount => deadEntityCount;
    public long EntityCount => entityCount;
    public EntityStore(bool readOnly)
    {
        lookup = new(readOnly);
        this.readOnly = readOnly;
    }

    /// <summary>
    /// Temporarily allows changes to this entity store.
    /// Useful to load the initial records from disk.
    /// </summary>
    internal void BeginLoading()
    {
        lookup.ReadOnly = false;
    }

    internal void EndLoading()
    {
        lookup.ReadOnly = this.readOnly;
    }

    /// <summary>
    /// Retreives an entity by its key value, using an exact (case sensitive) match.
    /// </summary>
    /// <param name="key">The key of the entity to find</param>
    /// <returns>Returns an Entity of type T, or null if the entity is not found or has been deleted</returns>
    public T Get(string key)
    {
        var result = lookup.Get(key);
        if (result == null) { return null; }
        return result.Deleted ? null : result;
    }

    /// <summary>
    /// Retreives an IEnumerable<T> of all entities that have keys that start with the exact string value of <paramref name="prefix"/>
    /// </summary>
    /// <typeparam name="TSelection">The type that the Entity should be selected as. Must be castable from <typeparamref name="T"/></typeparam>
    /// <param name="prefix">The prefix that will be used to search for matches.</param>
    /// <param name="sortResults">If the results will be sorted by key when returned.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PrefixResult<TSelection> GetByPrefix<TSelection>(string prefix) where TSelection : class, T
    {
        return lookup.GetByPrefix<TSelection>(prefix);
    }

    /// <summary>
    /// Applies a transaction, with one or more changes, to the entity store and sets any concurrency errors encountered.
    /// </summary>
    /// <param name="transaction">The transaction to apply to the Entity Store</param>
    /// <returns>Returns true if the transaction was applied without encountering a <see cref="ConcurrencyException"/>, false if one was set on the result of the transaction.</returns>
    public bool ProcessTransaction(Transaction<T> transaction)
    {
        if (transaction.IsSingleChange)
        {
            var entity = transaction.Entity;

            var existingDocument = lookup.Get(entity.Key);
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
            lookup.Set(entity.Key, entity);

            transaction.Entity = entity;
        }
        else
        {
            int deadEntitiesInExisting = 0;
            int newEntitiesCount = 0;
            for (int i = 0; i < transaction.Entities.Length; i++)
            {
                var entity = transaction.Entities[i];

                var existingDocument = lookup.Get(entity.Key);
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
                            return false;
                        }
                        else
                        {
                            transaction.Complete();
                        }
                    }
                    deadEntitiesInExisting++;
                }
                else
                {
                    newEntitiesCount++;
                }
            }
            if (transaction.IsComplete)
            {
                return;
            }
            this.deadEntityCount += deadEntitiesInExisting;
            this.entityCount += newEntitiesCount;

            for (int i = 0; i < transaction.Entities.Length; i++)
            {
                var change = transaction.Entities[i];
                change = (T)change.WithVersion(change.Version + 1);
                lookup.Set(change.Key, change);
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
    /// <param name="key"></param>
    /// <param name="entity"></param>
    public void LoadChange(T entity)
    {

        var existingRecord = lookup.Get(entity.Key);
        if (existingRecord != null)
        {
            if (entity.Version > existingRecord.Version)
            {
                lookup.Set(entity.Key, entity);
            }
            deadEntityCount++;
        }
        else
        {
            entityCount++;
            lookup.Set(entity.Key, entity);
        }
    }


    public void RemoveAllDocuments()
    {
        lookup.Clear();
    }

    public void IncrementDeadEntities()
    {
        deadEntityCount++;
    }

    public void ResetDeadSpace()
    {
        deadEntityCount = 0;
    }

}
