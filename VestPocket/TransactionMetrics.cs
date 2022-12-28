namespace VestPocket;


/// <summary>
/// Some basic stats from the VestPocketStore since it has been opened.
/// </summary>
public class TransactionMetrics
{
    internal long transactionCount;
    internal TimeSpan validationTime;
    internal TimeSpan serializationTime;
    internal long queueWaits;
    internal long bytesSerialized;
    internal long flushCount;

    /// <summary>
    /// The number of transactions that this VestPocketStore has applied.
    /// </summary>
    public long TransactionCount => transactionCount;

    /// <summary>
    /// The number of times that this VestPocketStore has asked the OS to flush writes to the output (if the output is a file).
    /// </summary>
    public long FlushCount => flushCount;

    /// <summary>
    /// The average amount of time this VestPocketStore has spent validating transactions (checking if entities already
    /// exist and if the incoming version matches the version stored in the VestPcketStore).
    /// </summary>
    public TimeSpan AverageValidationTime => transactionCount == 0 ? TimeSpan.Zero : validationTime / transactionCount;

    /// <summary>
    /// The average amount of time this VestPocketStore has spent serializing transactions to the output file/stream.
    /// </summary>
    public TimeSpan AverageSerializationTime => transactionCount == 0 ? TimeSpan.Zero : serializationTime / transactionCount;

    /// <summary>
    /// The total number of bytes serialized as JSON to the output file/stream.
    /// </summary>
    public long BytesSerialized => bytesSerialized;

    /// <summary>
    /// The average number of transactions processed each time the queue is drained. For a real system (and not a synthetic
    /// benchmark) this number might be quite low.
    /// </summary>
    public double AverageQueueLength => queueWaits == 0 ? 0 : transactionCount / queueWaits;
}
