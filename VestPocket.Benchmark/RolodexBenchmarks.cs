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
        private Connection<Entity> dataEngine;
        private string testKey = "123456";
        private Entity testDocument;

        public VestPocketBenchmarks()
        {
            SetupDataEngine();
        }

        private void SetupDataEngine()
        {
            dataEngine = Connection<Entity>.CreateTransient(SourceGenerationContext.Default.Entity);
            dataEngine.OpenAsync(CancellationToken.None).Wait();

            Task[] setResults = new Task[N];
            for (int i = 0; i < N; i++)
            {
                setResults[i] = dataEngine.Save(new Entity(i.ToString(), 0, false, $"Test Body {i}"));
            }
            Task.WaitAll(setResults);
            testDocument = dataEngine.Get<Entity>(testKey);
        }

        [Benchmark]
        public void GetByKey()
        {
            var document = dataEngine.Get<Entity>(testKey);
        }

        [Benchmark]
        public async Task SetKey()
        {
            testDocument = await dataEngine.Save(testDocument);
        }

        [Benchmark]
        public void GetKeyPrefix()
        {
            var results = dataEngine.GetByPrefix<Entity>("12345", false);
            foreach (var result in results)
            {
                if (result.Key == null)
                {
                    throw new Exception();
                }
            }
        }

    }
}
