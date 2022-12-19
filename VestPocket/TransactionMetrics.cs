using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket
{
    public class TransactionMetrics
    {
        internal long count;
        internal TimeSpan validationTime;
        internal TimeSpan serializationTime;
        internal long queueWaits;
        internal TimeSpan acquiringWriteLockTime;
        internal long bytesSerialized;

        public long Count => count;

        public TimeSpan AverageValidationTime => count == 0 ? TimeSpan.Zero : validationTime / count;

        public TimeSpan AverageSerializationTime => count == 0 ? TimeSpan.Zero : serializationTime / count;

        public TimeSpan AverageAquiringWriteLockTime => count == 0 ? TimeSpan.Zero : acquiringWriteLockTime / count;

        public long BytesSerialized => bytesSerialized;

        public double AverageQueueLength => queueWaits == 0 ? 0 : count / queueWaits;
    }
}
