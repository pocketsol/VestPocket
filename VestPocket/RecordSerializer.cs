using System.Buffers;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using System.IO;

namespace VestPocket
{
    public ref struct RecordSerializer<T> where T : class, IEntity
    {

        ArrayBufferWriter<byte> outputBuffer;
        ArrayBufferWriter<byte> entityBuffer;
        Utf8JsonWriter entityWriter;
        private JsonReaderOptions jsonReaderOptions = new();
        private readonly JsonTypeInfo<T> jsonTypeInfo;
        private const byte LF = 10;
        private const byte OpenObject = 123;
        private const byte CloseObject = 125;
        private const byte DoubleQuote = 34;
        private const byte Comma = 44;
        private static readonly byte[] KeyProperty = "\"key\":"u8.ToArray();
        private static readonly byte[] KeyPropertyName = "key"u8.ToArray();
        private static readonly byte[] ValProperty = "\"val\":"u8.ToArray();
        private static readonly byte[] ValPropertyName = "val"u8.ToArray();
        private static readonly int FixedOverheadLength;

        private int written = 0;
        public int Written => written;

        public void WriteToStream(Stream stream, bool resetBuffer = true)
        {
            var writtenSpan = outputBuffer.WrittenSpan;
            stream.Write(writtenSpan);
            if (resetBuffer) Reset();
        }

        public void Reset()
        {
            outputBuffer.ResetWrittenCount();
            entityBuffer.ResetWrittenCount();
            entityWriter.Reset(entityBuffer);
            written = 0;
        }

        /// <summary>
        /// Flushes the written records and returns the UTF8 bytes in a pooled array wrapped in an ArraySegment.
        /// </summary>
        public ArraySegment<byte> RentWrittenBuffer()
        {
            var writtenSpan = outputBuffer.WrittenSpan;
            int writtenLength = writtenSpan.Length;
            var pooledArray = ArrayPool<byte>.Shared.Rent(writtenLength);
            writtenSpan.CopyTo(pooledArray);
            return new ArraySegment<byte>(pooledArray, 0, writtenLength);
        }

        /// <summary>
        /// Returns the array backing the ArraySegment back to the shared array pool
        /// </summary>
        public void ReturnWrittenBuffer(ArraySegment<byte> buffer)
        {
            if (buffer.Array.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(buffer.Array);
            }
        }

        static RecordSerializer()
        {
            FixedOverheadLength =
                1 + // OpenObject
                KeyProperty.Length +
                1 + // Comma
                ValProperty.Length +
                2 + // Key Double Quotes
                1 + // CloseObject
                1; // LF
        }

        public RecordSerializer(
            ArrayBufferWriter<byte> outputBuffer, 
            ArrayBufferWriter<byte> entityBuffer, 
            Utf8JsonWriter entityWriter,
            JsonTypeInfo<T> jsonTypeInfo)
        {
            this.outputBuffer = outputBuffer;
            this.entityBuffer = entityBuffer;
            this.entityWriter = entityWriter;
            this.jsonTypeInfo = jsonTypeInfo;
        }

        public Record<T> Deserialize(ReadOnlySequence<byte> utf8Bytes)
        {
            var reader = new Utf8JsonReader(utf8Bytes, jsonReaderOptions);
            string key = null;
            T entity = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals(KeyPropertyName))
                    {
                        reader.Read();
                        key = reader.GetString();
                    }
                    else if (reader.ValueTextEquals(ValPropertyName))
                    {
                        reader.Read();
                        entity = JsonSerializer.Deserialize<T>(ref reader, jsonTypeInfo);
                    }
                }
            }
            return new Record<T>(key, entity);
            
        }


        /// <summary>
        /// Serializes a VestPocket entity as a key value pair record to a buffer maintained by RecordSerializer. Call RentWrittenBuffer
        /// to get access to written bytes, and ensure ReturnWrittenBuffer is also called when the buffer is no longer needed
        /// to avoid unecessary excessive garbage generation.
        /// </summary>
        /// <typeparam name="T">The type of the entity</typeparam>
        /// <param name="key">The key of the entity to write to the record</param>
        /// <param name="entity">The entity to use as the value (val property) in the key value pair of the record</param>
        public void Serialize(string key, T entity)
        {
            // Write the entity to a separate (entity) buffer
            entityBuffer.ResetWrittenCount();
            entityWriter.Reset(entityBuffer);

            JsonSerializer.Serialize(entityWriter, entity, jsonTypeInfo);
            var entityLength = entityBuffer.WrittenCount;

            Span<byte> keyBytes = stackalloc byte[key.Length * 4];
            keyBytes = keyBytes.Slice(0, Encoding.UTF8.GetBytes(key, keyBytes));

            var recordLength = FixedOverheadLength + keyBytes.Length + entityLength;

            // Make room in the record buffer to copy the serialized entity JSON and a linefeed
            var recordSpan = outputBuffer.GetMemory(recordLength).Span;

            int index = 0;

            recordSpan[index++] = OpenObject;

            KeyProperty.CopyTo(recordSpan.Slice(index, KeyProperty.Length));
            index += KeyProperty.Length;

            recordSpan[index++] = DoubleQuote;
            keyBytes.CopyTo(recordSpan.Slice(index, keyBytes.Length));
            index += keyBytes.Length;
            recordSpan[index++] = DoubleQuote;

            recordSpan[index++] = Comma;

            ValProperty.CopyTo(recordSpan.Slice(index, ValProperty.Length));
            index += ValProperty.Length;

            entityBuffer.WrittenSpan.CopyTo(recordSpan.Slice(index, entityLength));
            index += entityLength;

            recordSpan[index++] = CloseObject;
            recordSpan[index++] = LF;

            outputBuffer.Advance(recordLength);
            written += recordLength;
        }


    }
}
