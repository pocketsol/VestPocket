
[![Version](https://img.shields.io/nuget/vpre/VestPocket.svg)](https://www.nuget.org/packages/VestPocket)

![VestPocket](https://raw.githubusercontent.com/keithwill/VestPocket/main/Assets/packageIcon.png)

# What is Vest Pocket

Vest Pocket is a single-file persisted lookup contained in a pure .NET 7.0+ library. All records persisted are kept in-memory for fast retrieval. This project is not a replacement for a networked database or a distributed cache. It is meant to be a sidecar to each instance of your application.

> NOTE: Vest Pocket is currently an alpha library. The API may change drastically before the first release.

# Use Cases

- Version and deploy data that should evovle with your application which may also need to be updated at runtime
- Caching data locally to a single application instance
- As a light database for proof-of-concept and learning projects
- In any place you would use a single file database, but want to trade features and RAM usage for better performance

# Setup

After installing the nuget package, some code needs to be added to the project before Vest Pocket can be used.

```csharp
    [JsonDerivedType(typeof(Entity), nameof(Entity))]
    public record class Entity(string Key, int Version, bool Deleted) : IEntity
    {
        public IEntity WithVersion(int version) { return this with { Version = version }; }
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(Entity))]
    internal partial class VestPocketJsonContext : JsonSerializerContext { }
```

Vest Pocket takes advantage of System.Text.Json source generated serialization. These lines of code setup a base entity type for storing in Vest Pocket as well as configuring it for source generated serialization. This is standard System.Text.Json configuration. More information about this topic can be found in the [System.Text.Json .NET 7.0 announcement](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-7/#polymorphism-using-contract-customization).

# Adding an Entity

```csharp
    public record Customer(string Key, int Version, bool Deleted, string Name) : Entity(Key, Version, Deleted);
```
First, add a record type that inherits from the base entity type.

```csharp
    [JsonDerivedType(typeof(Entity), nameof(Entity))]
    [JsonDerivedType(typeof(Customer), nameof(Customer))] // <<----------Add This Line
    public record class Entity(string Key, int Version, bool Deleted) : IEntity
    {
        public IEntity WithVersion(int version) { return this with { Version = version }; }
    }
```

Then add a JsonDerivedType attribute to the base entity for the new entity type. This is necessary for System.Text.Json to create source generated serialization and deserialization logic.

# Usage

## Opening a Store

```csharp
    var options = new VestPocketOptions { FilePath = "test.db" };
    var store = new VestPocketStore<Entity>(VestPocketJsonContext.Default.Entity, options);
    await store.OpenAsync(CancellationToken.None);
```
 
 The store will maintain exclusive read write access on the opened file. The methods on a VestPocketStore are thread safe; it is safe to pass a single instance of a store between methods on different threads (for example, you could register a store object as a singleton or single instance lifecycle for use in DI/IoC).

 ## Saving a Record

 ```csharp
    var testEntity = new Entity("testKey", 0, false);
    var updatedEntity = await store.Save(testEntity);
```
Entities are immutable. After a save, a new instance of the entity is returned with the increased version number. In the example above, testEntity will have a version of 0, and updatedEntity will have a version of 1.

If you attempt to save an entity with a version number that does not match the version of the entity already stored for that key, a ConcurrencyException will be thrown. All saves to Vest Pocket require optimistic concurrency.

## Getting a Record by Key
 ```csharp
    var entity = await store.Get<Entity>("testKey");
```

## Getting Records by a Key Prefix
 ```csharp
 
    var entities = await store.GetByPrefix<Entity>("test");
    foreach(var entity in entities)
    {
        //...
    }
```

## Deleting a Record
 ```csharp
    var entity = await store.Get<Entity>("testKey");
    var entityToDelete = entity with { Deleted = true };
    await store.Save(entity);
```

 ## Saving a Transaction

 ```csharp

    var testEntity = store.Get<Entity>("testKey");
    var deletedTestEntity = testEntity with { Deleted = true };
    var entity1 = new Entity("entity1", 0, false);
    var entity2 = new Entity("entity2", 0, false);

    var entities = new[] { entity1, entity2, deletedTestEntity };

    var updatedEntities = await store.Save(entities);

```
Changes to multiple entities can be saved at the same time. If any entity has an out of date Version, then no changes will be applied.

## Closing the Store
 ```csharp
    await store.Close(CancellationToken.None);
```
The store should be closed before the application shuts down whenever possible. This will allow any ongoing file rewrite to complete and a graceful shutdown of transactions that are currently being processed. The cancellation token can be passed to control if the close method should cancel any shutdown activities (such as waiting for a rewrite to finish). As of v0.1.0, this has not yet been implemented.  

# File Format
The file format is trivial. After a transaction is accepted, each entity in the transaction is serialized using System.Text.Json to a single line of text. The file store is an append only file, and old versions of entities are left in the file.

 ```json
{"$type":"Entity","Key":"3-0","Version":1,"Deleted":false,"Body":"Just some body text 3-0"}
{"$type":"Entity","Key":"13-0","Version":1,"Deleted":false,"Body":"Just some body text 13-0"}
{"$type":"Entity","Key":"6-0","Version":1,"Deleted":false,"Body":"Just some body text 6-0"}
{"$type":"Entity","Key":"14-0","Version":1,"Deleted":false,"Body":"Just some body text 14-0"}
```

JSON is not a particularly compact format. It was chosen for this project for two reasons, it is relatively easy to read (and merge or diagnose) and because using System.Text.Json means that AOT friendly source generation can be utilized.

# Rewriting
Because entities are stored in a single append-only file, periodically it needs maintenance to avoid the file growing too large. When certain criteria are met (typically when more dead entities than alive ones are in the file), Vest Pocket will undertake a file rewrite.

This involves writing all of the live records to a new temporary file. When new transactions come in during a file rewrite, they are both applied to the single file and buffered in memory. Once the temporary file creation is complete, new transactions are paused for a moment while the in memory transaction buffer is applied to the temporary file, and an atomic File.Move is used to swap the temporary file as the new data file.

# Performance

The VestPocket.ConsoleTest project contains some rudementary async performance tests comparing the performance of Vest Pocket to LiteDb and to ConcurrentDictionary. These tests are far from conclusive but imply that Vest Pocket is at least an order of magnitude faster than LiteDb and at least an order of magnitude slower than ConcurrentDictionary. Both of these comparisons are unfair, for various reasons.

If you don't need the ability to persist data, apply a set of changes as a transaction, or the ability to search keys by a prefix value...then the performance of ConcurrentDictionary is quite good. If you need to store a very large amount of data or have a limited amount of RAM available, or want to utilize advanced query and indexing features, then LiteDb might be a better choice.

## *Sample output from running VestPocket.ConsoleTest*
```console
---------Running VestPocket---------

--Save Entities (threads:1000, iterations:100)--
Throughput 295250/s
Latency Median: 3.276600 Max:3.355136

--Read Entities (threads:1000, iterations:100)--
Throughput 2226770/s
Latency Median: 0.001400 Max:0.257704

--Prefix Search (threads:1000, iterations:100)--
Throughput 3917451/s
Latency Median: 0.002700 Max:0.135011

--Read and Write Mix (threads:1000, iterations:100)--
Throughput 202602/s
Latency Median: 4.319100 Max:4.926419

----Running ConcurrentDictionary----

--ConcurrentDictionary Save Entities (threads:1000, iterations:100)--
Throughput 7357594/s
Latency Median: 0.000500 Max:0.054178

--ConcurrentDictionary Read Entities (threads:1000, iterations:100)--
Throughput 20653049/s
Latency Median: 0.000200 Max:0.004747

--ConcurrentDictionary Read and Write Mix (threads:1000, iterations:100)--
Throughput 7407353/s
Latency Median: 0.001900 Max:0.008407

----------Running LiteDb-------------

--LiteDb Save Entities (threads:1000, iterations:100)--
Throughput 12174/s
Latency Median: 1.134000 Max:25.306619

--LiteDb Read Entities (threads:1000, iterations:100)--
Throughput 172192/s
Latency Median: 0.081400 Max:0.723352

--LiteDb Get Entities (threads:1000, iterations:100)--
Throughput 187811/s
Latency Median: 0.074500 Max:0.487134

--LiteDb Read and Write Mix (threads:1000, iterations:100)--
Throughput 8573/s
Latency Median: 2.303100 Max:16.042014
```
## *Sample Output from Runnning VestPocket.Benchmark*
```console
// * Summary *

BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19044.2251 (21H2)
AMD Ryzen 7 5700G with Radeon Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK=7.0.100
  [Host]     : .NET 7.0.0 (7.0.22.51805), X64 RyuJIT
  Job-CMOYAO : .NET 7.0.0 (7.0.22.51805), X64 RyuJIT

MaxRelativeError=0.05

|       Method |        Mean |     Error |    StdDev |  Gen 0 | Allocated |
|------------- |------------:|----------:|----------:|-------:|----------:|
|     GetByKey |    95.52 ns |  0.967 ns |  0.808 ns | 0.0038 |      32 B |
|       SetKey | 2,016.63 ns | 27.825 ns | 21.724 ns | 0.0877 |     744 B |
| GetKeyPrefix | 1,141.74 ns |  7.934 ns |  6.194 ns | 0.1888 |   1,592 B |

```
Before running the benchmark above, 999,999 entities are stored by key.

* GetByKey - Retreives a single entity by key
* SetKey - Updates an entity by key
* GetKeyPrefix - Performs a prefix search to retreive a small number of elements