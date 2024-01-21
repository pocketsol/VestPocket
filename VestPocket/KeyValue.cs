namespace VestPocket
{

    /// <summary>
    /// A Key Value Pair for string or UTF8 byte keys
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public record struct KeyValue<T>
    {

        /// <summary>
        /// Instantiates a key value pair
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public KeyValue(string key, T value)
        {
            this.key = key;
            this.value = value;
            this.keyUtf8 = System.Text.Encoding.UTF8.GetBytes(key).AsMemory();
        }

        /// <summary>
        /// Instantiates a key value pair
        /// </summary>
        public KeyValue(string key, ReadOnlyMemory<byte> keyUtf8, T value)
        {
            this.key = key;
            this.value = value;
            this.keyUtf8 = keyUtf8;
        }

        /// <summary>
        /// Instantiates a key value pair
        /// </summary>
        public KeyValue(ReadOnlyMemory<byte> keyUtf8, T value)
        {
            this.keyUtf8 = keyUtf8;
            this.key = System.Text.Encoding.UTF8.GetString(keyUtf8.Span);
            this.value = value;
        }

        /// <summary>
        /// The key as a string
        /// </summary>
        public string Key => key;

        /// <summary>
        /// The UTF8 bytes of the key
        /// </summary>
        public ReadOnlyMemory<byte> KeyUtf8 => keyUtf8;

        /// <summary>
        /// The value in the Key value pair
        /// </summary>
        public T Value => value;
        
        private string key;
        private ReadOnlyMemory<byte> keyUtf8;
        private T value;
    }
}
