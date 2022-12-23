using System.Buffers;

namespace VestPocket;
public class PrefixResult<TSelection> : IDisposable where TSelection : class, IEntity
{
    private TSelection[] buffer;
    private int length = 0;
    private static readonly ArrayPool<TSelection> pool = ArrayPool<TSelection>.Create();
    private readonly string keyPrefix;

    public PrefixResult(string keyPrefix)
    {
        this.keyPrefix = keyPrefix;
    }

    public void SetSize(int size)
    {
        buffer = pool.Rent(size);
        length = 0;
    }

    public void Add(TSelection entity)
    {
        buffer[length] = entity;
        length++;
    }

    public IEnumerable<TSelection> Results => GetResults();

    public IEnumerable<TSelection> GetResults()
    {
        for(int i = 0; i < length; i++)
        {
            yield return buffer[i];
        }
    }

    public int Count => length;

    public string KeyPrefix => keyPrefix;


    public void Dispose()
    {
        if (buffer != null)
        {
            pool.Return(buffer);
        }
    }


}