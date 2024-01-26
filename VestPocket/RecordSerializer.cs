using System.Buffers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Text.Json.Serialization;
using System.Collections.Frozen;

namespace VestPocket
{
    /// <summary>
    /// A serializer for serialization and deserialization of VestPocket records into JSON for writing
    /// to a file or stream. This uses Utf8JsonWriter together with source generated 
    /// System.Text.Json.JsonSerializer (for the user defined object serialization0.
    /// </summary>
    public ref struct RecordSerializer
    {

        ArrayBufferWriter<byte> outputBuffer;
        ArrayBufferWriter<byte> entityBuffer;
        Utf8JsonWriter entityWriter;
        private JsonReaderOptions jsonReaderOptions = new();
        private readonly JsonSerializerContext jsonSerializerContext;
        private readonly FrozenDictionary<string, StorageType> deserializerionType;
        private FrozenDictionary<Type, StorageType> serializationTypes;
        private const byte LF = 10;
        private const byte OpenObject = 123;
        private const byte CloseObject = 125;
        private const byte DoubleQuote = 34;
        private const byte Comma = 44;
        private static readonly byte[] KeyProperty = "\"key\":"u8.ToArray();
        private static readonly byte[] KeyPropertyName = "key"u8.ToArray();
        private static readonly byte[] ValProperty = "\"val\":"u8.ToArray();
        private static readonly byte[] ValPropertyName = "val"u8.ToArray();
        private static readonly byte[] TypeProperty = "\"$type\":"u8.ToArray();
        private static readonly byte[] TypePropertyName = "$type"u8.ToArray();
        private static readonly int FixedOverheadLength;

        private int written = 0;

        /// <summary>
        /// The number of bytes written to the serializers buffer so far.
        /// </summary>
        public int Written => written;

        /// <summary>
        /// Writes the current buffered json to the supplied stream.
        /// </summary>
        /// <param name="stream">The stream to write the buffered json to</param>
        /// <param name="resetBuffer">If the buffer should be reset after writing to the stream</param>
        public void WriteToStream(Stream stream, bool resetBuffer = true)
        {
            var writtenSpan = outputBuffer.WrittenSpan;
            stream.Write(writtenSpan);
            if (resetBuffer) Reset();
        }

        /// <summary>
        /// Clears the buffered JSON that has been written so far
        /// </summary>
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
                TypeProperty.Length +
                1 + // Comma
                ValProperty.Length +
                2 + // Key Double Quotes
                2 + // Type Double Quotes
                1 + // CloseObject
                1; // LF
        }


        internal RecordSerializer(
            ArrayBufferWriter<byte> outputBuffer, 
            ArrayBufferWriter<byte> entityBuffer, 
            Utf8JsonWriter entityWriter,
            VestPocketOptions vestPocketOptions)
        {
            this.outputBuffer = outputBuffer;
            this.entityBuffer = entityBuffer;
            this.entityWriter = entityWriter;
            this.jsonSerializerContext = vestPocketOptions.JsonSerializerContext;
            this.deserializerionType = vestPocketOptions.DeserializationTypes;
            this.serializationTypes = vestPocketOptions.SerializationTypes;
        }

        /// <summary>
        /// Deserializes a key value pair of a VestPocket record from the supplied utf8 json data.
        /// </summary>
        /// <param name="utf8Bytes">The utf8 json data of a single row of text from a VestPocket store file</param>
        /// <returns>A key value pair of the string key and untyped object of the stored record</returns>
        public Kvp Deserialize(ReadOnlySequence<byte> utf8Bytes)
        {
            var reader = new Utf8JsonReader(utf8Bytes, jsonReaderOptions);
            string key = null;
            object entity = null;
            StorageType storageType = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals(KeyPropertyName))
                    {
                        reader.Read();
                        key = reader.GetString();
                    }
                    if (reader.ValueTextEquals(TypePropertyName))
                    {
                        reader.Read();
                        string storageTypeName = reader.GetString();
                        deserializerionType.TryGetValue(storageTypeName, out storageType);
                    }
                    else if (reader.ValueTextEquals(ValPropertyName))
                    {
                        reader.Read();
                        if (storageType is not null)
                        {
                            entity = JsonSerializer.Deserialize(ref reader, storageType.JsonTypeInfo);
                        }
                        else
                        {
                            entity = reader.GetString();
                        }
                    }
                }
            }

            return new Kvp(key, entity);
            
        }


        /// <summary>
        /// Serializes a VestPocket entity as a key value pair record to a buffer maintained by RecordSerializer. Call RentWrittenBuffer
        /// to get access to written bytes, and ensure ReturnWrittenBuffer is also called when the buffer is no longer needed
        /// to avoid unecessary excessive garbage generation.
        /// </summary>
        /// <param name="key">The key of the entity to write to the record</param>
        /// <param name="entity">The entity to use as the value (val property) in the key value pair of the record</param>
        public void Serialize(string key, object entity)
        {
            // Write the entity to a separate (entity) buffer
            entityBuffer.ResetWrittenCount();
            entityWriter.Reset(entityBuffer);

            StorageType serializationType = null;
            if (entity is not null)
            {
                this.serializationTypes.TryGetValue(entity.GetType(), out serializationType);
            }
            byte[] utf8TypeName;

            if (serializationType is null)
            {
                utf8TypeName = VestPocketOptions.StringNameUtf8;
                if (entity is null)
                {
                    entityWriter.WriteNullValue();
                }
                else if (entity is string entityString)
                {
                    entityWriter.WriteStringValue(entityString.ToString());
                }
                else
                {
                    entityWriter.WriteStringValue(entity.ToString());
                }
            }
            else
            {
                utf8TypeName = serializationType.Utf8TypeName;
                JsonSerializer.Serialize(entityWriter, entity, serializationType.JsonTypeInfo);
            }

            var entityLength = entityBuffer.WrittenCount;

            Span<byte> keyBytes = stackalloc byte[key.Length * 4];
            keyBytes = keyBytes.Slice(0, Encoding.UTF8.GetBytes(key, keyBytes));

            var recordLength = FixedOverheadLength + keyBytes.Length + utf8TypeName.Length + entityLength;

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

            TypeProperty.CopyTo(recordSpan.Slice(index, TypeProperty.Length));
            index += TypeProperty.Length;

            recordSpan[index++] = DoubleQuote;
            utf8TypeName.CopyTo(recordSpan.Slice(index, utf8TypeName.Length));
            index += utf8TypeName.Length;
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
