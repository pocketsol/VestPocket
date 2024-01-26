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
    public class ConcurrentDictionaryBenchmarks
    {

        public int N = 999_999;
        private string testKey = "123456";
        private Entity testDocument;

        private ConcurrentDictionary<string, Entity> dictionary = new();

        public ConcurrentDictionaryBenchmarks()
        {
            SetupDictionary();
        }

        private void SetupDictionary()
        {
            for (int i = 0; i < N; i++)
            {
                this.dictionary.TryAdd(i.ToString(), new Entity( $"Test Body {i}"));
            }
            testDocument = this.dictionary[testKey];
        }

        [Benchmark]
        public void GetByKey()
        {
            dictionary.TryGetValue(testKey, out var entity);
        }

        [Benchmark]
        public Task SetKey()
        {
            var newEntity = testDocument with { };
            dictionary.TryAdd(testKey, newEntity);
            return Task.CompletedTask;
        }

        [Benchmark]
        public void GetKeyPrefix()
        {
            var results = new List<Entity>();
            foreach(var key in dictionary.Keys)
            {
                if (key.StartsWith("12345"))
                {
                    if (dictionary.TryGetValue(key, out var entity))
                    {
                        results.Add(entity);
                    }
                }
            }
        }

    }
}
