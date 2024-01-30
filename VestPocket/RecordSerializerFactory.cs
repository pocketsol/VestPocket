//using System.Buffers;
//using System.Text;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using System.Text.Json.Serialization.Metadata;

//namespace VestPocket
//{

//    /// <summary>
//    /// A UTF8 serializer of VestPocket records, which are key value pairs where the value is an entity and where records are delimited by
//    /// line feed characters.
//    /// </summary>
//    /// <remarks>
//    /// This serializer utilizes thread local caching of write buffers. You must call Reset on this serializer before using it, and must call Reset
//    /// before continuing to use it if the executing thread may have changed (e.g. after awaiting an async method).
//    /// </remarks>
//    internal class RecordSerializerFactory
//    {
//        //ThreadLocal<ArrayBufferWriter<byte>> outputBufferLocal = new ThreadLocal<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(4096));
//        //ThreadLocal<ArrayBufferWriter<byte>> entityBufferLocal = new ThreadLocal<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(4096));
//        ThreadLocal<Utf8JsonWriter> entityWriterLocal = new ThreadLocal<Utf8JsonWriter>();
//        private readonly JsonWriterOptions jsonWriterOptions;
//        private readonly VestPocketOptions vestPocketOptions;

//        /// <summary>
//        /// Creates a serializer to use when reading and writing to a VestPocketStore file.
//        /// The RecordSerializer returned is a ref struct and can't be used in async methods.
//        /// </summary>
//        /// <returns></returns>
//        public RecordSerializer Create()
//        {
//            //var outputBuffer = outputBufferLocal.Value;
//            //var entityBuffer = entityBufferLocal.Value;
//            var outputBuffer = bufferPool.Get();
//            var entityBuffer = bufferPool.Get();
//            outputBuffer.ResetWrittenCount();
//            entityBuffer.ResetWrittenCount();
//            if (!entityWriterLocal.IsValueCreated)
//            {
//                entityWriterLocal.Value = new(entityBuffer, jsonWriterOptions);
//            }
//            else
//            {
//                entityWriterLocal.Value.Reset(entityBuffer);
//            }
//            return new RecordSerializer(
//                outputBuffer,
//                entityBuffer,
//                entityWriterLocal.Value,
//                vestPocketOptions
//            );
//        }

//        /// <summary>
//        /// Instantiates a RecordSerializerFactory
//        /// </summary>
//        /// <param name="vestPocketOptions">The VestPocketOptions that are used in the store that this 
//        /// factory will create serializers for.</param>
//        public RecordSerializerFactory(VestPocketOptions vestPocketOptions)
//        {
//            this.jsonWriterOptions = new JsonWriterOptions()
//            {
//                Indented = false,
//                SkipValidation = true
//            };
//            this.vestPocketOptions = vestPocketOptions;
//        }


//    }
//}
