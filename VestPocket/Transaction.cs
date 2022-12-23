namespace VestPocket;

/// <summary>
/// Represents an action to save one or more entities to the <seealso cref="EntityStore{T}"/>.
/// Much like a traditional database transaction, either every change in a VestPocket transaction
/// is applied at the same time, or none of the changes are. A transaction can fail to be saved
/// if one or more entities have an out of date version compared to what is already stored in
/// the VestPocket.
/// </summary>
/// <typeparam name="T"></typeparam>
internal class Transaction<T> where T : IEntity
{
    private TaskCompletionSource taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private T entity;
    private T[] entities;

    public T Entity { get => entity; internal set => entity = value; }
    public T[] Entities { get => entities; internal set => entities = value; }


    public Transaction(T entity)
    {
        Entity = entity;
    }

    public Transaction(T[] entities)
    {
        Entities = entities;
    }

    public bool IsSingleChange => this.Entity != null;

    public void Complete()
    {
        this.taskCompletionSource.SetResult();
    }

    public void SetError(Exception ex)
    {
        this.taskCompletionSource.SetException(ex);
    }

    public Task Task => taskCompletionSource.Task;

}
