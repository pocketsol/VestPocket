namespace VestPocket;

/// <summary>
/// Represents the minimum interface that an entity must implement to be usable in VestPocket.
/// There are requirements that a type implementing IEntity must also satisfy before they can be
/// used properly in VestPocket:
/// They must be a Record type utilizing positional record type syntax
/// They must be configured for System.Text.Json source generator serialization and deserialization
/// </summary>
/// <example>
/// Example of a class that implements IEntity and a subclass, both configured for serialization
/// <code>
///     [JsonDerivedType(typeof(Entity), nameof(Entity))]
///     [JsonDerivedType(typeof(TestEntity), nameof(TestEntity))]
///     public record class Entity(string Key, int Version, bool Deleted, string Body) : IEntity
///     {
///         public IEntity WithVersion(int version) { return this with { Version = version }; }
///     }
///
///     public record TestEntity(string Key, int Version, bool Deleted, string Body, string Address) : Entity(Key, Version, Deleted, Body);
///     
///     [JsonSourceGenerationOptions(WriteIndented = false)]
///     [JsonSerializable(typeof(Entity))]
///     internal partial class SourceGenerationContext : JsonSerializerContext { }
/// </code>
///</example>
public interface IEntity
{
    /// <summary>
    /// The key to be used to store and locate the entity.
    /// Because VestPocket offers prefix searches, it may make sense to generate entity keys 
    /// in a heirarchical manner, such as you might see in REST endpoint routing. Keys
    /// are case sensitive and are matched using exact byte matching of the UTF8 representation
    /// of the string. 
    /// </summary>
    /// <example>
    /// /Customer/{Name}
    /// /Customer/{Name}/Locations/{Location}
    /// </example>
    public string Key { get; }

    /// <summary>
    /// If the entity should be deleted in VestPocket.
    /// </summary>
    /// <example>
    /// Using the Deleted property to delete an IEntity
    /// <code>
    /// var toDelete = entity with { Deleted = true };
    /// await connection.Save(toDelete);
    /// var entityRetreived = connection.Get&lt;Entity&gt;(entity.Key); // entityRetreived is null
    ///</code>
    /// </example>
    public bool Deleted { get; }

}