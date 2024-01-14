using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace VestPocket.Benchmark;

[MemoryDiagnoser]
public class VestPocketBenchmarks
{

    public int N = 999_999;
    private VestPocketStore<Entity> store;
    private string testKey = "123456";
    private Entity testDocument;

    private Entity[] testDocuments = new Entity[10];

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
        options.FilePath = null;
        options.RewriteRatio = 1;
        options.Durability = VestPocketDurability.FileSystemCache;
        
        store = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, options);
        store.OpenAsync(CancellationToken.None).Wait();

        Task[] setResults = new Task[N];
        for (int i = 0; i < N; i++)
        {
            setResults[i] = store.Save(new Entity(i.ToString(), false, $"Test Body {i}"));
        }
        Task.WaitAll(setResults);
        testDocument = store.Get<Entity>(testKey);

        for(int i = 0; i < testDocuments.Length; i++)
        {
            testDocuments[i] = store.Get<Entity>(i.ToString());
        }
    }

    [Benchmark]
    public void GetByKey()
    {
        var document = store.Get(testKey);
        if (document == null)
        {
            throw new Exception();
        }
    }

    [Benchmark]
    public async Task SetKey()
    {
        testDocument = await store.Save(testDocument);
    }

    [Benchmark]
    public async Task SetTenPerTransaction()
    {
        testDocuments = await store.Save(testDocuments);
    }

    
    [Benchmark]
    public void GetByPrefix()
    {
        foreach (var result in store.GetByPrefix<Entity>("1234"))
        {
            if (result.Key == null)
            {
                throw new Exception();
            }
        }
    }

    [Benchmark]
    public void GetByPrefix_BaseType()
    {
        foreach (var result in store.GetByPrefix("1234"))
        {
            if (result.Key == null)
            {
                throw new Exception();
            }
        }
    }

}
