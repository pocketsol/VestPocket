using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VestPocket
{

    /// <summary>
    /// A UTF8 serializer of VestPocket records, which are key value pairs where the value is an entity and where records are delimited by
    /// line feed characters.
    /// </summary>
    /// <remarks>
    /// This serializer utilizes thread local caching of write buffers. You must call Reset on this serializer before using it, and must call Reset
    /// before continuing to use it if the executing thread may have changed (e.g. after awaiting an async method).
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public class RecordSerializerFactory<T> where T : class, IEntity
    {

        ThreadLocal<ArrayBufferWriter<byte>> outputBufferLocal = new ThreadLocal<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(4096));
        ThreadLocal<ArrayBufferWriter<byte>> entityBufferLocal = new ThreadLocal<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(4096));
        ThreadLocal<Utf8JsonWriter> entityWriterLocal = new ThreadLocal<Utf8JsonWriter>();
        private readonly JsonWriterOptions jsonWriterOptions;
        private readonly JsonTypeInfo<T> jsonTypeInfo;

        public RecordSerializer<T> Create()
        {
            var outputBuffer = outputBufferLocal.Value;
            var entityBuffer = entityBufferLocal.Value;
            outputBuffer.ResetWrittenCount();
            entityBuffer.ResetWrittenCount();
            if (!entityWriterLocal.IsValueCreated)
            {
                entityWriterLocal.Value = new(entityBufferLocal.Value, jsonWriterOptions);
            }
            else
            {
                entityWriterLocal.Value.Reset(entityBufferLocal.Value);
            }
            return new RecordSerializer<T>(
                outputBuffer,
                entityBuffer,
                entityWriterLocal.Value,
                jsonTypeInfo
            );
        }

        public RecordSerializerFactory(JsonTypeInfo<T> jsonTypeInfo)
        {

            this.jsonWriterOptions = new JsonWriterOptions()
            {
                Indented = false,
                SkipValidation = true
            };
            this.jsonTypeInfo = jsonTypeInfo;
        }


    }
}
