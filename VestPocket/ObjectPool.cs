using System.Collections.Concurrent;

//namespace Microsoft.Extensions.ObjectPool;
namespace VestPocket;

/// <summary>
/// An object pool, which is a simplified version of Microsoft's DefaultPool from Microsoft.Extensions.ObjectPool.
/// </summary>
/// <typeparam name="T">The type to pool objects for.</typeparam>
/// <remarks>This implementation keeps a cache of retained objects. This means that if objects are returned when the pool has already reached "maximumRetained" objects they will be available to be Garbage Collected.</remarks>
internal sealed class ObjectPool<T> where T : class
{
    private readonly Func<T> _createFunc;
    private readonly int _maxCapacity;
    private int _numItems;

    private readonly ConcurrentQueue<T> _items = new();
    private T _fastItem;

     /// <summary>
     /// Instantiates a new ObjectPool, using the supplied Func to create new
     /// instances of pooled objects when needed, and retaining only a maximum
     /// number of items.
     /// </summary>
    public ObjectPool(Func<T> createFunc, int maximumRetained)
    {
        _createFunc = createFunc;
        _maxCapacity = maximumRetained - 1;  // -1 to account for _fastItem
    }

    public T Get()
    {
        var item = _fastItem;
        if (item == null || Interlocked.CompareExchange(ref _fastItem, null, item) != item)
        {
            if (_items.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _numItems);
                return item;
            }

            // no object available, so go get a brand new one
            return _createFunc();
        }

        return item;
    }


    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    public void Return(T obj)
    {
        if (_fastItem != null || Interlocked.CompareExchange(ref _fastItem, obj, null) != null)
        {
            if (Interlocked.Increment(ref _numItems) <= _maxCapacity)
            {
                _items.Enqueue(obj);
            }

            // no room, clean up the count and drop the object on the floor
            Interlocked.Decrement(ref _numItems);
        }
    }
}