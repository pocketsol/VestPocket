using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace VestPocket;

/// <summary>
/// Options for configuring the behavior of a VestPocketStore
/// </summary>
public class VestPocketOptions
{

    /// <summary>
    /// A VestPocketOptions instance created with default options
    /// </summary>
    public static VestPocketOptions Default => new();
    
    /// <summary>
    /// A VestPocketOptions instance created with default options, except it is ReadOnly
    /// </summary>
    public static readonly VestPocketOptions DefaultReadOnly = new VestPocketOptions { ReadOnly = true };

    ///<summary>
    /// The file path to the file where entities are stored. A default value of "store.db" is
    /// supplied, but it is strongly recommended to supply a different file path. If assigned a
    /// null value, then records will be buffered into a MemoryStream instead of a FileStream,
    /// and is provided as an option for diagnostic and unit testing purposes.
    ///</summary>
    public string FilePath { get; set; } = "store.db";

    /// <summary>
    /// A name to give to the connection. This can be useful when differentiating multiple
    /// instances of VestPocket connections (such as when connections to different files are
    /// registered into DI/IoC). Default value is null
    /// </summary>
    public string Name { get; set; } = null;

    ///<summary>
    /// If changes are allowed to entities after being loaded from the file or not. Default value is false
    ///</summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// The ratio of current records to dead records stored 
    /// before a rewrite is started. A ratio of 1.0 means
    /// that a rewrite will occur whenever more dead records
    /// are stored than current records (if more than 
    /// RewriteMinimum records are stored). Default value
    /// is 1.0
    /// </summary>
    public double RewriteRatio { get; set; } = 1.0;

    /// <summary>
    /// The minimum number of records stored before a rewrite
    /// will be considered. Default value is 10,000
    /// </summary>
    public int RewriteMinimum { get; set; } = 10_000;

    /// <summary>
    /// If set, then the live records will be stored as a compressed
    /// block at the start of the rewritten file.
    /// </summary>
    public bool CompressOnRewrite { get; set; } = false;

    /// <summary>
    /// The strategy for file durability that will be used with this VestPocketStore.
    /// </summary>
    public VestPocketDurability Durability { get; set; } = VestPocketDurability.FlushOnDelay;

    /// <summary>
    /// The JsonSerializerContext which contains the source generated JsonTypeInfo objects
    /// thaat VestPocket will use to serialize and deserialize user defined types.
    /// </summary>
    public JsonSerializerContext JsonSerializerContext { get; set; }

    internal FrozenDictionary<string, StorageType> DeserializationTypes { get; set; }

    internal FrozenDictionary<Type, StorageType> SerializationTypes { get; set; }

    private List<StorageType> storageTypes = new();

    /// <summary>
    /// Adds a type that can be stored in the VestPocketStore.
    /// </summary>
    /// <typeparam name="T">The type to add</typeparam>
    /// <param name="jsonTypeName">When VestPocket serializes a complex object, it also stores the name of the type in a $type property (from the objects Type.Name). This parameter can override the $type property used.</param>
    public void AddType<T>(string jsonTypeName = null)
    {
        ThrowIfFrozen();
        Type typeToRegister = typeof(T);
        if (jsonTypeName is null)
        {
            jsonTypeName = typeToRegister.Name;
        }
        var jsonTypeInfo = JsonSerializerContext.GetTypeInfo(typeToRegister);
        var storageType = new StorageType(jsonTypeName, typeToRegister, jsonTypeInfo, System.Text.Encoding.UTF8.GetBytes(jsonTypeName));
        storageTypes.Add(storageType);
    }

    private void ThrowIfFrozen()
    {
        if (frozen)
        {
            throw new ArgumentException("Cannot make changes to a frozen VestPocketOptions object");
        }
    }

    static internal readonly byte[] StringNameUtf8 = "string"u8.ToArray();

    private bool frozen = false;
    internal void Freeze()
    {
        if (frozen) return;
        frozen = true;

        var typesByName = storageTypes.Select(x => new KeyValuePair<string, StorageType>(x.TypeName, x));
        var typesByType = storageTypes.Select(x => new KeyValuePair<Type, StorageType>(x.Type, x));
        DeserializationTypes = typesByName.ToFrozenDictionary();
        SerializationTypes = typesByType.ToFrozenDictionary();
    }

    /// <summary>
    /// Returns the first validation failure message, or null if the options appear valid
    /// </summary>
    public string Validate()
    {
        if (FilePath == null && ReadOnly)
        {
            return "If the FilePath is null, then ReadOnly cannot be true, as that would imply opening a store to an empty memory stream that cannot be written to.";
        }

        if (FilePath != null && Durability == VestPocketDurability.Unknown)
        {
            return "If a FilePath is supplied, than a Durability other than Unknown must be supplied";
        }

        if (RewriteRatio <= 0.0)
        {
            return "RewriteRatio must be greater than zero";
        }

        if (RewriteMinimum <= 1)
        {
            return "RewriteMinimum must be greater than one";
        }

        return null;
    }

}