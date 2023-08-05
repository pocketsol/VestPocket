using System.Buffers;

namespace VestPocket;

/// <summary>
/// The results of a VestPocketStore key lookup by prefix. 
/// Because prefix key searches can be arbitrarily large, 
/// this class implements some optimizations to reduce wasted allocations
/// and implements IDisposable to ensure pooled resources can be resused after
/// the search results are no longer necessary
/// </summary>
/// <typeparam name="TSelection">The type of entity of the prefix search results</typeparam>
public class PrefixResult<TSelection> : IDisposable where TSelection : class, IEntity
{
    private TSelection[] buffer;
    private int length = 0;
    private static readonly ArrayPool<TSelection> pool = ArrayPool<TSelection>.Create();
    private readonly string keyPrefix;


    internal PrefixResult(string keyPrefix)
    {
        this.keyPrefix = keyPrefix;
    }

    internal void SetSize(int size)
    {
        buffer = pool.Rent(size);
        length = 0;
    }

    internal void Add(TSelection entity)
    {
        buffer[length] = entity;
        length++;
    }

    /// <summary>
    /// The results of the prefix search
    /// </summary>
    public IEnumerable<TSelection> Results => GetResults();

    internal IEnumerable<TSelection> GetResults()
    {
        for (int i = 0; i < length; i++)
        {
            yield return buffer[i];
        }
    }

    /// <summary>
    /// The number of prefix search results that were found
    /// </summary>
    public int Count => length;

    /// <summary>
    /// The key prefix that was used in the search
    /// </summary>
    public string KeyPrefix => keyPrefix;


    /// <summary>
    /// Disposes of the PrefixResult object, and allows internal arrays to be reused.
    /// </summary>
    public void Dispose()
    {
        if (buffer != null)
        {
            pool.Return(buffer);
        }
    }


}