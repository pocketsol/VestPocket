using System.Buffers;
using System.Threading.Tasks.Sources;

namespace VestPocket;

/// <summary>
/// Represents an action to save one or more entities to the <seealso cref="EntityStore"/>.
/// Much like a traditional database transaction, either every change in a VestPocket transaction
/// is applied at the same time, or none of the changes are. A transaction can fail to be saved
/// if one or more entities have an out of date version compared to what is already stored in
/// the VestPocket.
/// </summary>
internal abstract class Transaction : IValueTaskSource
{
    protected ManualResetValueTaskSourceCore<object> valueCompletionSource = new();

    public bool FailedConcurrency { get; set; }
    public abstract int Count { get; }
    public abstract Kvp this[int index] { get; set; }
    public bool ThrowOnError => throwOnError;
    protected bool throwOnError;
    public ReadOnlySpan<byte> Utf8JsonPayload => serializer.WrittenSpan;
    protected bool valueTaskGenerated = false;


    protected RecordSerializer serializer;
    protected VestPocketOptions options;

    public Transaction()
    {
        valueCompletionSource.RunContinuationsAsynchronously = true;
    }

    public virtual bool Validate(object existingEntity) => true;

    public void Complete()
    {
        this.valueCompletionSource.SetResult(null);
    }

    public void SetError(Exception ex)
    {
        this.valueCompletionSource.SetException(ex);
    }

    public void GetResult(short token)
    {
        valueCompletionSource.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return valueCompletionSource.GetStatus(token);
    }

    public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        valueCompletionSource.OnCompleted(continuation, state, token, flags);
    }

    public virtual void Reset(VestPocketOptions options, bool throwOnError)
    {
        this.valueCompletionSource.Reset();
        if (serializer is null)
        {
            this.serializer = new(options);
        }
        else
        {
            this.serializer.Reset(options);
        }
        this.valueTaskGenerated = false;
        this.FailedConcurrency = false;
        this.options = options;
        this.throwOnError = throwOnError;
    }

    //private static ArraySegment<byte> Empty = new([]);
    private static readonly byte[] EmptyBytes = [];


    public ValueTask Task {
        get {
            CheckValueTaskGenerated();
            return new ValueTask(this, valueCompletionSource.Version);
        }
    }

    private void CheckValueTaskGenerated()
    {
        if (valueTaskGenerated)
        {
            throw new InvalidOperationException("Cannot await a transaction result multiple times");
        }
        valueTaskGenerated = true;
    }

}
