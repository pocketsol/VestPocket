namespace VestPocket
{

    /// <summary>
    /// The interface that all of the TrieHard lookup implementations adhere to. Most lookups have methods
    /// that will perform better if this interface is not used directly; this interface exists to document
    /// the expected interface of each implementation and to simplify testing of lookups. A developer consuming
    /// the lookups from this project would not be expected to use this interface directly in a consuming project.
    /// </summary>
    /// <typeparam name="TValue">
    /// The type of the value to store in the lookup.
    /// </typeparam>
    public interface IPrefixLookup<TValue> : IEnumerable<KeyValue<TValue>>
    {

        /// <summary>
        /// Gets the value of the supplied key. Unlike a Dictionary,
        /// this will return a default or null value if the value does not
        /// exist in the lookup.
        /// </summary>
        /// <param name="key">The key to match on</param>
        /// <returns>The value associated with the key or default(TElement)</returns>
        TValue this[string key] { get; set; }

        /// <summary>
        /// Removes all values from the lookup. Implementations are free to decide if this
        /// clears any other internal storage or node connections.
        /// </summary>
        void Clear();

        /// <summary>
        /// The number of values stored in the lookup.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Returns the key value pairs that have a key that starts with the
        /// supplied key prefix value.
        /// </summary>
        /// <param name="keyPrefix">The value to use as a 'StartsWith' search of keys</param>
        /// <returns>An enumerable of the key value pairs matching the prefix</returns>
        IEnumerable<KeyValue<TValue>> Search(string keyPrefix);

        /// <summary>
        /// Returns the values that have are associated with keys that start with the
        /// supplied key prefix.
        /// </summary>
        /// <param name="keyPrefix">The value to use as a 'StartsWith' search of keys</param>
        /// <returns>The value stored with each matching key</returns>
        IEnumerable<TValue> SearchValues(string keyPrefix);
    }

}
