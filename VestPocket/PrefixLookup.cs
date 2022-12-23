namespace VestPocket;

/// <summary>
/// A class for inserting and looking up values
/// based on a key (like a dictionary) that also
/// supports looking up matching values by a key
/// prefix value. Internally implemented with a radix
/// tree where leaf nodes also store entities.
/// </summary>
/// <seealso>https://en.wikipedia.org/wiki/Radix_tree</seealso>
/// 
/// <typeparam name="T"></typeparam>
internal class PrefixLookup<T> where T : class, IEntity
{

    private readonly Node<T> root;
    private bool readOnly;

    internal bool ReadOnly { get => readOnly; set => readOnly = value; }

    /// <summary>
    /// Creates a PrefixLookup of type T.
    /// </summary>
    /// <param name="readOnly">If read and write access should be syncrhonized behind reader writer locks</param>
    public PrefixLookup(bool readOnly)
    {
        root = new Node<T>();
        this.readOnly = readOnly;
    }

    public int NodeCount => root.GetChildrenCount();

    public int Count => root.GetValuesCount();

    public void Set(ReadOnlySpan<char> keyBytes, T value)
    {
        if (readOnly) throw new InvalidOperationException("Cannot set values in a PrefixLookup that is read only");
        root.SetValue(keyBytes, value);
    }

    public T Get(ReadOnlySpan<char> key)
    {
        return root.GetValue(key)?.Value;
    }

    public void Clear()
    {
        if (readOnly) throw new InvalidOperationException("Cannot clear a PrefixLookup that is read only");
        this.root.Value = null;
        this.root.Children = null;
    }

    public PrefixResult<TSelection> GetByPrefix<TSelection>(string keyPrefix) where TSelection : class, T
    {

        PrefixResult<TSelection> result = new(keyPrefix);
        if (keyPrefix == string.Empty)
        {
            root.CollectValues(result);
        }
        else
        {
            root.GetValuesByPrefix(keyPrefix, result);
        }
        return result;

    }

}
