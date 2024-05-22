using System.Buffers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace VestPocket
{
    /// <summary>
    /// A serializer for serialization and deserialization of VestPocket records into JSON for writing
    /// to a file or stream. This uses Utf8JsonWriter together with source generated 
    /// System.Text.Json.JsonSerializer (for the user defined object serialization).
    /// </summary>
    public class RecordSerializer
    {

        private static readonly JsonWriterOptions jsonWriterOptions = new JsonWriterOptions()
        {
            Indented = false,
            SkipValidation = true
        };

        public RecordSerializer(VestPocketOptions vestPocketOptions)
        {
            this.outputBuffer = new ArrayBufferWriter<byte>(8);
            this.entityBuffer = new ArrayBufferWriter<byte>(8);
            entityWriter = new(entityBuffer, jsonWriterOptions);
            this.options = vestPocketOptions;
        }

        private readonly ArrayBufferWriter<byte> outputBuffer;
        private readonly ArrayBufferWriter<byte> entityBuffer;
        private readonly Utf8JsonWriter entityWriter;
        private VestPocketOptions options;
        private JsonReaderOptions jsonReaderOptions = new();
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

        public ReadOnlySpan<byte> WrittenSpan => outputBuffer.WrittenSpan;

        /// <summary>
        /// Clears the buffered JSON that has been written so far
        /// </summary>
        public void Reset(VestPocketOptions options)
        {
            Reset();
            this.options = options;
        }

        public void Reset()
        {
            outputBuffer.ResetWrittenCount();
            entityBuffer.ResetWrittenCount();
            entityWriter.Reset(entityBuffer);
            written = 0;
        }

        ///// <summary>
        ///// Flushes the written records and returns the UTF8 bytes in a pooled array wrapped in an ArraySegment.
        ///// </summary>
        //public ArraySegment<byte> RentWrittenBuffer()
        //{
        //    var writtenSpan = outputBuffer.WrittenSpan;
        //    int writtenLength = writtenSpan.Length;
        //    var pooledArray = ArrayPool<byte>.Shared.Rent(writtenLength);
        //    writtenSpan.CopyTo(pooledArray);
        //    return new ArraySegment<byte>(pooledArray, 0, writtenLength);
        //}

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
                        if (!options.DeserializationTypes.TryGetValue(storageTypeName, out storageType))
                        {
                            throw new Exception("Unknown deserialization type");
                        }
                    }
                    else if (reader.ValueTextEquals(ValPropertyName))
                    {
                        reader.Read();
                        if (storageType is not null)
                        {
                            if (storageType.JsonTypeInfo is null)
                            {
                                entity = JsonSerializer.Deserialize(ref reader, storageType.Type, options.JsonSerializerContext);
                            }
                            else
                            {
                                entity = JsonSerializer.Deserialize(ref reader, storageType.JsonTypeInfo);
                            }
                        }
                        else
                        {
                            // Implicit types
                            entity = reader.TokenType switch { 
                                JsonTokenType.Null => null,
                                JsonTokenType.String => reader.GetString(),
                                JsonTokenType.Number => reader.GetDouble(),
                                JsonTokenType.True => true,
                                JsonTokenType.False => false,
                                _ => throw new Exception(
                                    $"A $type property was not found and the Json TokenType is not one that VestPocket understands implicitly: {reader.TokenType}"), 
                            };
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
                options.SerializationTypes.TryGetValue(entity.GetType(), out serializationType);
            }

            if (serializationType is null)
            {
                if (entity is null)
                {
                    entityWriter.WriteNullValue();
                }
                else if (entity is string entityString)
                {
                    entityWriter.WriteStringValue(entityString.ToString());
                }
                else if (entity is double entityDouble)
                {
                    entityWriter.WriteNumberValue(entityDouble);
                } 
                else if (entity is bool entityBool)
                {
                    entityWriter.WriteBooleanValue(entityBool);
                }
                else
                {
                    // This isn't a compile time known type 
                    throw new Exception("Unknown serialization type");
                }
            }
            else
            {
                JsonSerializer.Serialize(entityWriter, entity, serializationType.JsonTypeInfo);
            }

            var entityLength = entityBuffer.WrittenCount;

            Span<byte> keyBytes = stackalloc byte[key.Length * 4];
            keyBytes = keyBytes.Slice(0, Encoding.UTF8.GetBytes(key, keyBytes));

            int typeNameLength = serializationType is null ? 0 : serializationType.Utf8TypeName.Length;

            var recordLength = FixedOverheadLength + keyBytes.Length + typeNameLength + entityLength;

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

            if (serializationType is not null)
            {
                recordSpan[index++] = Comma;

                TypeProperty.CopyTo(recordSpan.Slice(index, TypeProperty.Length));
                index += TypeProperty.Length;

                recordSpan[index++] = DoubleQuote;
                serializationType.Utf8TypeName.CopyTo(recordSpan.Slice(index, serializationType.Utf8TypeName.Length));
                index += serializationType.Utf8TypeName.Length;
                recordSpan[index++] = DoubleQuote;
            }

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
