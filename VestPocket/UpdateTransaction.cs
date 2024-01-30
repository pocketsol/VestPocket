namespace VestPocket;

internal class UpdateTransaction : Transaction, IDisposable
{
    private static ObjectPool<UpdateTransaction> pool = new ObjectPool<UpdateTransaction>(
        () => new UpdateTransaction(), 1000
    );

    private Kvp entity;
    private object basedOn;

    public Kvp Record { get => entity; internal set => entity = value; }

    public static UpdateTransaction Create(VestPocketOptions options, Kvp entity, object basedOn, bool throwOnError)
    {
        var transaction = pool.Get();
        transaction.Reset(options, entity, basedOn, throwOnError);
        transaction.serializer.Serialize(entity.Key, entity.Value);
        return transaction;
    }

    private UpdateTransaction() : base()
    {
    }

    public override bool Validate(object existingEntity)
    {
        var matches = MatchesExisting(existingEntity);
        if (!matches)
        {
            entity = new Kvp(entity.Key, existingEntity);
            if (ThrowOnError)
            {
                SetError(new ConcurrencyException(entity.Key));
            }
            else
            {
                Complete();
            }
        }
        return matches;
    }

    private bool MatchesExisting(object existingEntity)
    {
        if (basedOn is null && existingEntity is null)
        {
            return true;
        }
        //if (basedOn is IEquatable equatable)
        //{
        //    return equatable.Equals(existingEntity);
        //}
        return existingEntity.Equals(basedOn);
    }

    public void Reset(VestPocketOptions options, Kvp entity, object basedOn, bool throwOnError)
    {
        base.Reset(options, throwOnError);
        this.entity = entity;
        this.basedOn = basedOn;
    }

    public void Dispose()
    {
        pool.Return(this);
    }

    public override int Count => 1;

    public override Kvp this[int index]
    {
        get
        {
            if (index != 0) throw new IndexOutOfRangeException();
            return entity;
        }
        set
        {
            if (index != 0) throw new IndexOutOfRangeException();
            entity = value;
        }
    }
}
