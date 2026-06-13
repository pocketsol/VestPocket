
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

After installing the nuget package, define the type(s) you want to store and configure them for serialization.

```csharp
    public record class Entity(string Body);

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(Entity))]
    internal partial class VestPocketJsonContext : JsonSerializerContext { }
```

Any type can be stored in Vest Pocket. A stored type is a plain object or record; it does not need to implement an interface, inherit from a base type, or carry `Key`, `Version`, or `Deleted` members. Records are stored as key/value pairs, where the key is supplied separately from the value (see [Saving a Record](#saving-a-record)).

Vest Pocket takes advantage of System.Text.Json source generated serialization. The lines of code above configure the `Entity` type for source generated serialization. This is standard System.Text.Json configuration. More information about this topic can be found in the [System.Text.Json .NET 7.0 announcement](https://devblogs.microsoft.com/dotnet/system-text-json-in-dotnet-7/#polymorphism-using-contract-customization).

Add a `JsonSerializable` attribute to the `JsonSerializerContext` for every type you intend to store. Each of those types must also be registered with the store's options through `AddType<T>()` (see below).

# Usage

## Opening a Store

```csharp

    var vestPocketOptions = new VestPocketOptions
    {
        CompressOnRewrite = true,
        Durability = VestPocketDurability.FlushOnDelay,
        FilePath = "store.db",
        JsonSerializerContext = VestPocketJsonContext.Default,
        ReadOnly = false,
        Name = null,
        RewriteRatio = 1.0,
        RewriteMinimum = 10_000
    };
    vestPocketOptions.AddType<Entity>();

    var store = new VestPocketStore(vestPocketOptions);
    await store.OpenAsync(CancellationToken.None);
```

Every type that will be stored must be registered with `AddType<T>()` before the store is opened. Attempting to save a value of an unregistered type will throw an exception. `AddType<T>()` accepts an optional `jsonTypeName` argument that overrides the value written to the record's `$type` property (it defaults to the type's name).

The store will maintain exclusive read write access on the opened file. The methods on a VestPocketStore are thread safe; it is safe to pass a single instance of a store between methods on different threads (for example, you could register a store object as a singleton or single instance lifecycle for use in DI/IoC).

 ## Saving a Record

A record is a `Kvp` (key/value pair) of a string key and a value of any registered type.

 ```csharp
    var testEntity = new Entity("some body text");
    await store.Save(new Kvp("testKey", testEntity));
```

A key/value overload is also provided as a convenience:

 ```csharp
    await store.Save("testKey", testEntity);
```

`Save` writes the value unconditionally, overwriting any existing value for the key. To save several records together as a single transaction, pass an array of `Kvp` (see [Saving a Transaction](#saving-a-transaction)). To save only when the currently stored value matches an expected value, use `Update` or `Swap` (see [Optimistic Concurrency](#optimistic-concurrency)).

In addition to registered types, string, boolean, and numeric values are handled implicitly and can be saved without registering a type:

 ```csharp
    await store.Save(new Kvp("testKey", "some string value"));
```

## Getting a Record by Key
 ```csharp
    var entity = store.Get<Entity>("testKey");
```

`Get` is synchronous and returns the stored value cast to the requested type, or null if no value is stored for the key. A non-generic overload returns the value as a `Kvp`:

 ```csharp
    Kvp record = store.Get("testKey");
```

## Getting Records by a Key Prefix
 ```csharp

    foreach(var record in store.GetByPrefix("test"))
    {
        var entity = (Entity)record.Value;
        //...
    }
```
`GetByPrefix` is synchronous and enumerates the key/value pairs whose keys start with the supplied (case sensitive) prefix.


## Deleting a Record

A record is deleted by saving a null value for its key.

 ```csharp
    await store.Save(new Kvp("testKey", null));
```

 ## Saving a Transaction

 ```csharp

    var entities = new Kvp[]
    {
        new Kvp("entity1", new Entity("body1")),
        new Kvp("entity2", new Entity("body2")),
        new Kvp("testKey", null), // a delete can be part of a transaction
    };

    await store.Save(entities);

```
Changes to multiple records can be saved at the same time by passing an array of `Kvp` to `Save`. The array is applied as a single transaction.

## Optimistic Concurrency

`Update` and `Swap` apply a change only when the value currently stored for the key still matches a `basedOn` value. This is a compare-and-swap operation; pass `null` as `basedOn` when you expect no value to be stored for the key yet.

 ```csharp
    var current = store.Get<Entity>("testKey");
    var updated = current with { Body = "updated body" };

    // Throws ConcurrencyException if the stored value no longer matches `current`
    await store.Update(new Kvp("testKey", updated), current);

    // Swap does not throw; it returns the value that is currently stored
    var stored = await store.Swap(new Kvp("testKey", updated), current);
```

If the stored value no longer matches `basedOn`, `Update` throws a `ConcurrencyException`, while `Swap` leaves the store unchanged and returns the value that is currently stored.

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

After a transaction is accepted, each record in the transaction is serialized using System.Text.Json to a single line of text. The file store is an append only file, and old versions of records are left in the file.

Each record is a JSON object with a `key` property, a `$type` property naming the registered type of the value, and a `val` property holding the serialized value.

 ```json
{"key":"3-0","$type":"Entity","val":{"Body":"Just some body text 3-0"}}
{"key":"13-0","$type":"Entity","val":{"Body":"Just some body text 13-0"}}
{"key":"6-0","$type":"Entity","val":{"Body":"Just some body text 6-0"}}
{"key":"14-0","$type":"Entity","val":{"Body":"Just some body text 14-0"}}
```

Values of implicitly handled types (strings, booleans, and numbers) are written without a `$type` property, and a deleted record is written with a null `val`:

 ```json
{"key":"6-0","val":"some string value"}
{"key":"3-0","val":null}
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
    var options = new VestPocketOptions
    {
        FilePath = "test.db",
        JsonSerializerContext = VestPocketJsonContext.Default,
        ReadOnly = true
    };
    options.AddType<Entity>();

    var store = new VestPocketStore(options);
    await store.OpenAsync(CancellationToken.None);
```

If you open a store with the ReadOnly option, then write operations will throw exceptions.
