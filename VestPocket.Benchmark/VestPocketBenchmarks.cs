using BenchmarkDotNet.Attributes;

namespace VestPocket.Benchmark;

[MemoryDiagnoser]
public class VestPocketBenchmarks
{

    /// <summary>
    /// Number of entities to put into the store before running tests
    /// </summary>
    public int EntityCount = 999_999;

    /// <summary>
    /// Number of iterations to run per method execution of methods that
    /// take advantage of ValueTaskSource poooling to amortize the cost
    /// (This shouldn't be necessary, but it seems it is)
    /// </summary>
    public const int N = 10_000;

    private VestPocketStore store;
    private string testKey = "123456";
    private Kvp testDocument;
    private const int BatchSize = 10_000;
    private Kvp[] batchEntities = new Kvp[BatchSize];

    public VestPocketBenchmarks()
    {
    }

    [GlobalSetup]
    public async Task SetupConnection()
    {
        var dbFile = VestPocketOptions.Default.FilePath;

        if (File.Exists(dbFile))
        {
            File.Delete(dbFile);
        }

        var options = new VestPocketOptions();
        options.JsonSerializerContext = SourceGenerationContext.Default;

        options.AddType<Entity>();
        options.RewriteRatio = 1;
        options.Durability = VestPocketDurability.FileSystemCache;
        
        store = new VestPocketStore(options);
        await store.OpenAsync(CancellationToken.None);

        var entities = new Kvp[EntityCount];
        for (int i = 0; i < EntityCount; i++)
        {
            entities[i] = new Kvp(i.ToString(), new Entity($"Test Body {i}"));
        }

        await store.Save(entities);
        testDocument = new Kvp(testKey, store.Get<Entity>(testKey));

        for (int i = 0; i < batchEntities.Length; i++)
        {
            batchEntities[i] = entities[i];
        }
    }

    [Benchmark]
    public Entity GetByKey()
    {
        return store.Get<Entity>(testKey);
    }

    [Benchmark(OperationsPerInvoke = N)]
    public async ValueTask Save()
    {
        for(int i = 0; i < N; i++)
        {
            await store.Save(testDocument);
        }
    }

    [Benchmark(OperationsPerInvoke = BatchSize)]
    public async Task SaveBatch()
    {
        await store.Save(batchEntities);
    }

    [Benchmark]
    public object GetByPrefix()
    {
        KeyValue<object> result = default;
        foreach (var kvp in store.GetByPrefix("1234"))
        {
            result = kvp;
        }
        return result.Value;

    }


}
