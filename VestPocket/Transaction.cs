using System.Buffers;
using TrieHard.PrefixLookup;

namespace VestPocket;

/// <summary>
/// Represents an action to save one or more entities to the <seealso cref="EntityStore{T}"/>.
/// Much like a traditional database transaction, either every change in a VestPocket transaction
/// is applied at the same time, or none of the changes are. A transaction can fail to be saved
/// if one or more entities have an out of date version compared to what is already stored in
/// the VestPocket.
/// </summary>
/// <typeparam name="T"></typeparam>
internal abstract class Transaction<T> where T : IEntity
{
    private TaskCompletionSource taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool FailedConcurrency { get; set; }
    public abstract int Count { get; }
    public abstract T this[int index] { get; set; }
    public bool ThrowOnError { get; }

    public ArraySegment<byte> Payload;
    public bool IsComplete => taskCompletionSource.Task.IsCompleted;

    public Transaction(bool throwOnError)
    {
        ThrowOnError = throwOnError;
    }

    public virtual bool Validate(T existingEntity) => true;

    public void Complete()
    {
        this.taskCompletionSource.SetResult();
    }

    public void SetError(Exception ex)
    {
        this.taskCompletionSource.SetException(ex);
    }

    public Task Task => taskCompletionSource.Task;

    public void ClearPayload()
    {
        if (Payload.Array is not null)
        {
            ArrayPool<byte>.Shared.Return(Payload.Array);
        }
    }

}
