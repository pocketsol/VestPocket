using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace VestPocket.Benchmark;

[MemoryDiagnoser]
public class VestPocketBenchmarks
{

    public int N = 999_999;
    private VestPocketStore store;
    private string testKey = "123456";
    private Kvp testDocument;

    private Kvp[] testDocuments = new Kvp[10];

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
        options.FilePath = null;
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

        for(int i = 0; i < testDocuments.Length; i++)
        {
            testDocuments[i] = new Kvp(i.ToString(), store.Get<Entity>(i.ToString()));
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
    public async Task<Kvp> SetKey()
    {
        return await store.Save(testDocument);
    }

    [Benchmark]
    public async Task<Kvp[]> SetTenPerTransaction()
    {
        return await store.Save(testDocuments);
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
