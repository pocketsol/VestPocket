namespace VestPocket;

public class TransactionMetrics
{
    internal long count;
    internal TimeSpan validationTime;
    internal TimeSpan serializationTime;
    internal long queueWaits;
    internal long bytesSerialized;

    public long Count => count;

    public TimeSpan AverageValidationTime => count == 0 ? TimeSpan.Zero : validationTime / count;

    public TimeSpan AverageSerializationTime => count == 0 ? TimeSpan.Zero : serializationTime / count;

    public long BytesSerialized => bytesSerialized;

    public double AverageQueueLength => queueWaits == 0 ? 0 : count / queueWaits;
}
