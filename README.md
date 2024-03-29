
[![Version](https://img.shields.io/nuget/vpre/VestPocket.svg)](https://www.nuget.org/packages/VestPocket)

![VestPocket](https://raw.githubusercontent.com/keithwill/VestPocket/main/Assets/packageIcon.png)

# What is Vest Pocket

Vest Pocket is a single-file persisted lookup contained in a pure .NET 8.0+ library. All records persisted are kept in-memory for fast retrieval.
This project is not a replacement for a networked database or a distributed cache. It is meant to be a sidecar to each instance of your application.

> NOTE: Vest Pocket is currently an alpha library. The API may change drastically before the first release.

# Use Cases

- Version and deploy data that should evovle with your application which may also need to be updated at runtime
- Caching data locally to a single application instance, especially for handling autocomplete / prefix searches
- As a light database for proof-of-concept and learning projects
- In any place you would use a single file database, but want to trade features and RAM usage for better performance

# Setup

After installing the nuget package, initialize a VestPocketOptions instance.

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


Then add a JsonDerivedType attribute to the base entity for the new entity type. This is necessary for System.Text.Json to create source generated serialization and deserialization logic.

# Usage

## Opening a Store

```csharp

    var vestPocketOptions = new VestPocketOptions
    {
        CompressOnRewrite = true,
        Durability = VestPocketDurability.FlushOnDelay,
        FilePath = "store.db",
        JsonSerializerContext = SourceGenerationContext.Default,
        ReadOnly = false,
        Name = null,
        RewriteRatio = 1.0,
        RewriteMinimum = 10_000
    };
    var store = new VestPocketStore(vestPocketOptions);
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
 
    var results = await store.GetByPrefix<Entity>("test");
    foreach(var entity in results)
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
The file format is simple and meant to be easy to read for developers familiar with JSON.

## Header
Each file starts with a header row of JSON which contains some metadata about the file, such as when it was created, the last time it was rewritten, and any entities that were compressed on the last file rewrite.

```json
{"Creation":"2022-12-14T17:38:24.1766817-05:00","LastRewrite":"2022-12-14T17:38:39.1187185-05:00","CompressedEntities":[...]}
```

## Entities

After a transaction is accepted, each entity in the transaction is serialized using System.Text.Json to a single line of text. The file store is an append only file, and old versions of entities are left in the file.

 ```json
{"$type":"Entity","Key":"3-0","Version":1,"Deleted":false,"Body":"Just some body text 3-0"}
{"$type":"Entity","Key":"13-0","Version":1,"Deleted":false,"Body":"Just some body text 13-0"}
{"$type":"Entity","Key":"6-0","Version":1,"Deleted":false,"Body":"Just some body text 6-0"}
{"$type":"Entity","Key":"14-0","Version":1,"Deleted":false,"Body":"Just some body text 14-0"}
```

JSON is not a particularly compact format. It was chosen for this project for two reasons, it is relatively easy to read (and merge or diagnose) and because using System.Text.Json means that AOT friendly source generation can be utilized.

If the entities stored in the file do not need to be easy to read for what you plan to use the store for, then it is recommended to enable the option to compress entities on rewrite.

# Rewriting
Because entities are stored in a single append-only file, periodically it needs maintenance to avoid the file growing too large. When certain criteria are met (typically when more dead entities than alive ones are in the file), Vest Pocket will undertake a file rewrite.

This involves writing all of the live records to a new temporary file. When new transactions come in during a file rewrite, they are both applied to the single file and buffered in memory. Once the temporary file creation is complete, new transactions are paused for a moment while the in memory transaction buffer is applied to the temporary file, and an atomic File.Move is used to swap the temporary file as the new data file.

# Forcing Maintenance

 ```csharp
    await store.ForceMaintenance();
```

A rewrite can be forced by calling the method ForceMaintenance. If a rewrite is already ongoing, then this method will wait for the current rewrite to complete and will not start a new rewrite operation.

# Backup
 ```csharp
    await store.CreateBackup("test.backup");
```

A Vest Pocket store can be backed up by calling the CreateBackup method. While a Vest Pocket store could be simply copied to another file using normal file conventions (System.IO, using scripts, or manually by the dev), using the CreateBackup method from code offers several advantages. The resulting file will be generated in a similar way to Rewriting: old versions of entities will be pruned, the CompressOnRewrite option will be honored, the Vest Pocket header row will contain updated meta data, and the resulting file will not contain partially written entities at the very end of the file.

# Performance


## *Sample Output from Runnning VestPocket.Benchmark*
```console
// * Summary *

BenchmarkDotNet v0.13.8, Windows 10 (10.0.19045.3803/22H2/2022Update)
AMD Ryzen 7 5700G with Radeon Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.100
  [Host]     : .NET 8.0.0 (8.0.23.53103), X64 RyuJIT AVX2
  Job-NWVYYZ : .NET 8.0.0 (8.0.23.53103), X64 RyuJIT AVX2

MaxRelativeError=0.05

| Method      | Mean        | Error     | StdDev   | Allocated |
|------------ |------------:|----------:|---------:|----------:|
| GetByKey    |    30.29 ns |  0.221 ns | 0.196 ns |         - |
| Save        | 2,730.84 ns | 11.552 ns | 9.647 ns |         - |
| SaveBatch   |   226.34 ns |  7.655 ns | 7.160 ns |         - |
| GetByPrefix |   673.93 ns |  7.239 ns | 6.772 ns |         - |

```
Before running the benchmark above, 999,999 entities are stored by key.

* GetByKey - Retreives a single entity by key
* Save - Updates an entity by key
* SaveBatch - Updates a larger batch of entities by passing them as an array to save (time is given per entitiy)
* GetByPrefix - Performs a prefix search to retreive a small number of elements

## *Sample output from running VestPocket.ConsoleTest*
```console
---------Running VestPocket---------

--Create Entities (threads:100, iterations:1000), ops/iteration:1000--
Throughput 154932085/s
Latency Median: 0.092400 Max:0.126751

--Save Entities (threads:100, iterations:1000), ops/iteration:1--
Throughput 1456673/s
Latency Median: 0.092500 Max:0.068584

--Read Entities (threads:100, iterations:1000), ops/iteration:1000--
Throughput 102212157/s
Latency Median: 0.143900 Max:0.223523

--Save Entities Batched (threads:100, iterations:1), ops/iteration:1000--
Throughput 3548641/s
Latency Median: 26.710000 Max:27.858400

--Prefix Search (threads:100, iterations:1000), ops/iteration:1--
Throughput 5700019/s
Latency Median: 0.002400 Max:0.003881

--Read and Write Mix (threads:100, iterations:1000), ops/iteration:10--
Throughput 5904275/s
Latency Median: 0.165800 Max:0.169268

-----Transaction Metrics-------------
Transaction Count: 401000
Flush Count: 8
Validation Time: 72056.4us
Serialized Bytes: 38034093
Queue Length: 50125
```

BenchmarkDotNet tests are great for testing the timing and overhead of individual operations, but are less useful for showing the impact of a library or system when under load from many asynchronous requests at a time. VestPocket.ConsoleTest contains a rudementary test that attempts to measure the requests per second of various VestPocket methods. The 'Read and Write Mix' performs two save operations and gets four values by key.

This test was performed with AOT against the same machine as the above Benchmark.NET test (Against a file stored on an NVmE drive).

# VestPocketOptions

## ReadOnly

```csharp
    var options = new VestPocketOptions { FilePath = "test.db", ReadOnly = true };
    var store = new VestPocketStore<Entity>(VestPocketJsonContext.Default.Entity, options);
    await store.OpenAsync(CancellationToken.None);
```

If you open a store with the ReadOnly option, then write operations will throw exceptions.
