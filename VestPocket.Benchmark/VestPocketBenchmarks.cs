using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VestPocket.Benchmark
{



    [MemoryDiagnoser]
    public class VestPocketBenchmarks
    {

        public int N = 999_999;
        private VestPocketStore<Entity> store;
        private string testKey = "123456";
        private Entity testDocument;

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

            store = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, VestPocketOptions.Default);
            store.OpenAsync(CancellationToken.None).Wait();

            Task[] setResults = new Task[N];
            for (int i = 0; i < N; i++)
            {
                setResults[i] = store.Save(new Entity(i.ToString(), 0, false, $"Test Body {i}"));
            }
            Task.WaitAll(setResults);
            testDocument = store.Get<Entity>(testKey);
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
}
