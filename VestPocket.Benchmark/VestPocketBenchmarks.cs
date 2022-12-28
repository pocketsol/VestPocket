using BenchmarkDotNet.Attributes;

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
        if (System.IO.File.Exists(dbFile))
        {
            System.IO.File.Delete(dbFile);
        }

        var options = new VestPocketOptions();
        options.RewriteRatio = 100;
        store = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, options);
        store.OpenAsync(CancellationToken.None).Wait();

        Task[] setResults = new Task[N];
        for (int i = 0; i < N; i++)
        {
            setResults[i] = store.Save(new Entity(i.ToString(), 0, false, $"Test Body {i}"));
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
        var document = store.Get<Entity>(testKey);
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
    public void GetKeyPrefix()
    {
        using var prefixSerach = store.GetByPrefix<Entity>("1234");
        foreach (var result in prefixSerach.Results)
        {
            if (result.Key == null)
            {
                throw new Exception();
            }
        }
    }

}
