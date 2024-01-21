using BenchmarkDotNet.Attributes;

namespace VestPocket.Benchmark;

[MemoryDiagnoser]
public class VestPocketBenchmarks
{

    public int N = 999_999;
    private VestPocketStore store;
    private string testKey = "123456";
    private Kvp testDocument;

    private Kvp[] entities1000 = new Kvp[1000];

    public VestPocketBenchmarks()
    {
        SetupConnection();
    }

    private void SetupConnection()
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
        store.OpenAsync(CancellationToken.None).Wait();

        Task[] setResults = new Task[N];
        for (int i = 0; i < N; i++)
        {
            setResults[i] = store.Save(new Kvp(i.ToString(), new Entity($"Test Body {i}")));
        }
        Task.WaitAll(setResults);
        testDocument = new Kvp(testKey, store.Get<Entity>(testKey));

        for (int i = 0; i < entities1000.Length; i++)
        {
            string key = i.ToString();
            entities1000[i] = new Kvp(key, store.Get<Entity>(key));
        }
    }

    [Benchmark]
    public Entity GetByKey()
    {
        return store.Get<Entity>(testKey);
    }

    [Benchmark]
    public Kvp GetByKeyUntyped()
    {
        return store.Get(testKey);
    }

    [Benchmark]
    public async Task<Kvp> Save()
    {
        return await store.Save(testDocument);
    }

    [Benchmark]
    public async Task Save1000()
    {
        await store.Save(entities1000);
    }


    [Benchmark]
    public object GetByPrefix()
    {
        object entity = null;
        foreach (var result in store.GetByPrefix("1234"))
        {
            entity = result.Value;
        }
        return entity;
    }


}
