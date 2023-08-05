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
    /// <param name="saveVersion">The version of the entity that failed to save</param>
    /// <param name="actualVersion">The current version of the entity in the VestPocketStore</param>
    public ConcurrencyException(string key, int saveVersion, int actualVersion) :
        base($"Could not save {key} with {saveVersion} as the current version is {actualVersion}")
    {
    }
}
