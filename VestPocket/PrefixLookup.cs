using System.Collections;
using System.Collections.Concurrent;
using System.Text.Unicode;

namespace VestPocket;

/// <summary>
/// A collection for storing values keyed by string that offers efficient lookup by key prefixes.
/// </summary>
/// <remarks>
/// Implemented as a Radix Tree.
/// </remarks>
/// <seealso>https://en.wikipedia.org/wiki/Radix_tree</seealso>
/// <typeparam name="T"></typeparam>
public class PrefixLookup<T> : IPrefixLookup<T>
{

    private Node<T> root;
    
    /// <summary>
    /// Instantiates a new PrefixLookup
    /// </summary>
    public PrefixLookup()
    {
        root = new Node<T>();
    }

    /// <summary>
    /// The number of values stored in the lookup.
    /// </summary>
    public int Count => root.GetValuesCount();

    /// <summary>
    /// Gets the value of the supplied key. Unlike a Dictionary,
    /// this will return a default or null value if the value does not
    /// exist in the lookup.
    /// </summary>
    /// <param name="key">The key to match on</param>
    /// <returns>The value associated with the key or default(TElement)</returns>
    public T this[string key]
    {
        get
        {
            Span<byte> keyBuffer = stackalloc byte[key.Length * 4];
            Span<byte> keySpan = GetKeyStringBytes(key, keyBuffer);
            return Get(keySpan);
        }
        set
        {
            Span<byte> keyBuffer = stackalloc byte[key.Length * 4];
            Span<byte> keySpan = GetKeyStringBytes(key, keyBuffer);
            Set(keySpan, value);
        }
    }

    /// <summary>
    /// Gets the value of the supplied key. Unlike a Dictionary,
    /// this will return a default or null value if the value does not
    /// exist in the lookup.
    /// </summary>
    /// <param name="key">The key to match on</param>
    /// <returns>The value associated with the key or default(TElement)</returns>
    public T this[ReadOnlySpan<byte> key]
    {
        get
        {
            return Get(key);
        }
        set
        {
            Set(key, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Span<byte> GetKeyStringBytes(string key, Span<byte> buffer)
    {
        Utf8.FromUtf16(key, buffer, out var _, out var bytesWritten, false, true);
        return buffer.Slice(0, bytesWritten);
    }

    /// <summary>
    /// Sets the value to the key supplied
    /// </summary>
    /// <param name="keyBytes">The UTF8 byes of the key</param>
    /// <param name="value">The value</param>
    public void Set(ReadOnlySpan<byte> keyBytes, T value)
    {
        root.SetValue(ref root, keyBytes, value);
    }
    /// <summary>
    /// Gets the value associated with the supplied key
    /// </summary>
    /// <param name="key">The UTF8 bytes of the key</param>
    /// <returns></returns>
    public T Get(ReadOnlySpan<byte> key)
    {
        return root.Get(key);
    }

    /// <summary>
    /// Removes all values from the lookup. Implementations are free to decide if this
    /// clears any other internal storage or node connections.
    /// </summary>
    public void Clear()
    {
        root.Reset();
    }

    /// <summary>
    /// Gets an enumerator of all values contained in the lookup
    /// </summary>
    /// <returns></returns>
    public IEnumerator<KeyValue<T>> GetEnumerator()
    {
        return Search(ReadOnlySpan<byte>.Empty).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Gets the values the have been stored with a key that starts with the supplied value
    /// </summary>
    /// <param name="keyPrefix">The value that matching keys will start with</param>
    /// <returns></returns>
    public ValueEnumerator SearchValues(string keyPrefix)
    {
        Span<byte> keyBuffer = stackalloc byte[keyPrefix.Length * 4];
        keyBuffer = GetKeyStringBytes(keyPrefix, keyBuffer);
        return SearchValues(keyBuffer);
    }

    /// <summary>
    /// Gets the values the have been stored with a key that starts with the supplied value
    /// </summary>
    /// <param name="keyPrefix">The value that matching keys will start with</param>
    /// <returns></returns>
    public ValueEnumerator SearchValues(ReadOnlySpan<byte> keyPrefix)
    {
        Node<T> matchingNode = keyPrefix.Length == 0 ? root : root.FindPrefixMatch(keyPrefix);
        return new ValueEnumerator(matchingNode);
    }

    /// <summary>
    /// Returns the key value pairs that have a key that starts with the
    /// supplied key prefix value.
    /// </summary>
    /// <param name="keyPrefix">The value to use as a 'StartsWith' search of keys</param>
    /// <returns>An enumerable of the key value pairs matching the prefix</returns>
    public Enumerator Search(string keyPrefix)
    {
        Span<byte> keyBuffer = stackalloc byte[keyPrefix.Length * 4];
        keyBuffer = GetKeyStringBytes(keyPrefix, keyBuffer);
        return Search(keyBuffer);
    }

    /// <summary>
    /// Returns the key value pairs that have a key that starts with the
    /// supplied key prefix value.
    /// </summary>
    /// <param name="keyPrefix">The value to use as a 'StartsWith' search of keys</param>
    /// <returns>An enumerable of the key value pairs matching the prefix</returns>
    public Enumerator Search(ReadOnlySpan<byte> keyPrefix)
    {
        Node<T> matchingNode = keyPrefix.Length == 0 ? root : root.FindPrefixMatch(keyPrefix);
        return new Enumerator(matchingNode);
    }

    /// <summary>
    /// Gets an enumerator of all key value paires contained in the lookup
    /// </summary>
    /// <returns></returns>
    IEnumerator<KeyValue<T>> IEnumerable<KeyValue<T>>.GetEnumerator()
    {
        return Search(string.Empty).GetEnumerator();
    }

    /// <summary>
    /// Gets the values the have been stored with a key that starts with the supplied value
    /// </summary>
    /// <param name="keyPrefix">The value that matching keys will start with</param>
    /// <returns></returns>
    IEnumerable<KeyValue<T>> IPrefixLookup<T>.Search(string keyPrefix)
    {
        return Search(keyPrefix);
    }

    IEnumerable<T> IPrefixLookup<T>.SearchValues(string keyPrefix)
    {
        return SearchValues(keyPrefix);
    }


    #region Enumerators

    /// <summary>
    /// A PrefixLookup enumerator for enumerating key value pairs retreived
    /// from searches on the PrefixLookup.
    /// </summary>
    public struct Enumerator : IEnumerable<KeyValue<T>>, IEnumerator<KeyValue<T>>
    {

        private Node<T> searchNode;
        private Stack<(Node<T>[] Siblings, int Index)> stack;
        private KeyValue<T> current;

        /// <summary>
        /// The current KeyValue pair value of the enemerator
        /// </summary>
        public KeyValue<T> Current => current;

        object IEnumerator.Current => Current;

        /// <summary>
        /// Gets the enumerator
        /// </summary>
        /// <returns></returns>
        public Enumerator GetEnumerator() => this;

        internal Enumerator(Node<T> collectNode)
        {
            searchNode = collectNode;
        }

        private static readonly ConcurrentQueue<Stack<(Node<T>[] Siblings, int Index)>> stackPool = new();

        private Stack<(Node<T>[] Siblings, int Index)> RentStack()
        {
            if (stackPool.TryDequeue(out var stack))
            {
                return stack;
            }
            return new Stack<(Node<T>[] Siblings, int Index)>();
        }

        private void ReturnStack(Stack<(Node<T>[] Siblings, int Index)> stack)
        {
            stack.Clear();
            stackPool.Enqueue(stack);
        }

        /// <summary>
        /// Advances the enumerator to the next value
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (searchNode is null) return false;
            if (stack == null)
            {
                stack = RentStack();
                if (searchNode!.Value is not null)
                {
                    current = searchNode.AsKeyValue();
                    return true;
                }
            }

            // Go until we find enough values to fill the buffer or traverse the tree
            while (true)
            {

                // DFS: Go until we find a value or bottom out on a leaf node
                while (searchNode!.childrenBuffer.Length > 0)
                {
                    stack.Push((searchNode.childrenBuffer, 0));
                    searchNode = searchNode.childrenBuffer[0];
                    if (searchNode.Value is not null)
                    {
                        current = searchNode.AsKeyValue();
                        return true;
                    }
                }

                // We made it to a leaf. Backtrack and find next sibling
                // of our search node

                while (true)
                {
                    if (stack.Count == 0)
                    {
                        searchNode = null;
                        var stackTmp = stack;
                        stack = null;
                        ReturnStack(stackTmp);
                        return false;
                    }

                    var parentStack = stack.Pop();
                    var siblings = parentStack.Siblings;
                    var nextSiblingIndex = parentStack.Index + 1;

                    if (nextSiblingIndex < siblings.Length)
                    {
                        stack.Push((parentStack.Siblings, nextSiblingIndex));
                        searchNode = siblings[nextSiblingIndex];

                        if (searchNode.Value is not null)
                        {
                            current = searchNode.AsKeyValue();
                            return true;
                        }
                        break;
                    }
                }
            }
        }


        IEnumerator<KeyValue<T>> IEnumerable<KeyValue<T>>.GetEnumerator()
        {
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        /// <summary>
        /// Resets the enumerator. Not implemente on this type
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Reset()
        {
            throw new NotImplementedException();
        }

        void IDisposable.Dispose()
        {
            if (stack is not null)
            {
                ReturnStack(stack);
                stack = null;
            }
        }
    }

    /// <summary>
    /// A PrefixLookup enumerator for enumerating values retreived
    /// from searches on the PrefixLookup.
    /// </summary>
    public struct ValueEnumerator : IEnumerable<T>, IEnumerator<T>
    {

        private Node<T> searchNode;
        private Stack<(Node<T>[] Siblings, int Index)> stack;
        private T current = default!;

        /// <summary>
        /// Gets the current enumerator value
        /// </summary>
        public T Current => current;

        object IEnumerator.Current => Current!;

        /// <summary>
        /// Gets the enumerator
        /// </summary>
        /// <returns></returns>

        public ValueEnumerator GetEnumerator() => this;

        internal ValueEnumerator(Node<T> collectNode)
        {
            searchNode = collectNode;
        }

        private static readonly ConcurrentQueue<Stack<(Node<T>[] Siblings, int Index)>> stackPool = new();

        private Stack<(Node<T>[] Siblings, int Index)> RentStack()
        {
            if (stackPool.TryDequeue(out var stack))
            {
                return stack;
            }
            return new Stack<(Node<T>[] Siblings, int Index)>();
        }

        private void ReturnStack(Stack<(Node<T>[] Siblings, int Index)> stack)
        {
            stack.Clear();
            stackPool.Enqueue(stack);
        }

        /// <summary>
        /// Advances the enumerator to the next value, if available
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (searchNode is null) return false;
            if (stack == null)
            {
                stack = RentStack();
                if (searchNode!.Value is not null)
                {
                    current = searchNode.Value;
                    return true;
                }
            }

            // Go until we find enough values to fill the buffer or traverse the tree
            while (true)
            {

                // DFS: Go until we find a value or bottom out on a leaf node
                while (searchNode!.childrenBuffer.Length > 0)
                {
                    stack.Push((searchNode.childrenBuffer, 0));
                    searchNode = searchNode.childrenBuffer[0];
                    if (searchNode.Value is not null)
                    {
                        current = searchNode.Value;
                        return true;
                    }
                }

                // We made it to a leaf. Backtrack and find next sibling
                // of our search node

                while (true)
                {
                    if (stack.Count == 0)
                    {
                        searchNode = null;
                        var stackTmp = stack;
                        stack = null;
                        ReturnStack(stackTmp);
                        return false;
                    }

                    var parentStack = stack.Pop();
                    var siblings = parentStack.Siblings;
                    var nextSiblingIndex = parentStack.Index + 1;

                    if (nextSiblingIndex < siblings.Length)
                    {
                        stack.Push((parentStack.Siblings, nextSiblingIndex));
                        searchNode = siblings[nextSiblingIndex];

                        if (searchNode.Value is not null)
                        {
                            current = searchNode.Value;
                            return true;
                        }
                        break;
                    }
                }
            }
        }


        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return this;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this;
        }

        /// <summary>
        /// Resets the enumerator. Not implemented.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Reset()
        {
            throw new NotImplementedException();
        }

        void IDisposable.Dispose()
        {
            if (stack is not null)
            {
                ReturnStack(stack);
                stack = null;
            }
        }
    }

    #endregion

}
