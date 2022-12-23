namespace VestPocket;

/// <summary>
/// An exception thrown when an attempt to save an Entity is performed on
/// an entity that is already stored with a different version. This represents
/// a violation of optimistic concurrency.
/// </summary>
public class ConcurrencyException : Exception
{
    public ConcurrencyException(string key, int saveVersion, int actualVersion) :
        base($"Could not save {key} with {saveVersion} as the current version is {actualVersion}")
    {
    }
}
