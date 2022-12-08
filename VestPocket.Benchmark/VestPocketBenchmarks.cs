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
        private Connection<Entity> connection;
        private string testKey = "123456";
        private Entity testDocument;

        public VestPocketBenchmarks()
        {
            SetupConnection();
        }

        private void SetupConnection()
        {
            Console.WriteLine("Setting up bench.db");
            if (System.IO.File.Exists("bench.db"))
            {
                System.IO.File.Delete("bench.db");
            }

            connection = Connection<Entity>.Create("bench.db", SourceGenerationContext.Default.Entity);
            connection.OpenAsync(CancellationToken.None).Wait();

            Task[] setResults = new Task[N];
            for (int i = 0; i < N; i++)
            {
                setResults[i] = connection.Save(new Entity(i.ToString(), 0, false, $"Test Body {i}"));
            }
            Task.WaitAll(setResults);
            testDocument = connection.Get<Entity>(testKey);
        }

        [Benchmark]
        public void GetByKey()
        {
            var document = connection.Get<Entity>(testKey);
        }

        [Benchmark]
        public async Task SetKey()
        {
            testDocument = await connection.Save(testDocument);
        }

        [Benchmark]
        public void GetKeyPrefix()
        {
            var results = connection.GetByPrefix<Entity>("12345", false);
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
