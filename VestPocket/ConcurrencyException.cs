namespace VestPocket;

/// <summary>
/// An exception thrown when an attempt to save an Entity is performed on
/// an entity that is already stored with a different version. This represents
/// a violation of optimistic concurrency.
/// </summary>
public class ConcurrencyException : Exception
{

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class
    /// </summary>
    /// <param name="key">The key of entity that failed to save</param>
    public ConcurrencyException(string key) :
        base($"Could not save entity with key:{key}. The operation was performed on an entity that no longer matches what is stored in the VestPocket file")
    {
    }
}
