using VestPocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VestPocket.ConsoleTest
{


    class Program
    {
        static Connection<Entity> engine;
       

        static async Task Main(string[] args)
        {

            if (System.IO.File.Exists("test.db"))
            {
                engine = Connection<Entity>.Create("test.db", SourceGenerationContext.Default.Entity);
                await engine.OpenAsync(CancellationToken.None);

                await TimeIterations("Read Entities", (thread, i) => {
                    engine.Get<Entity>($"{thread}-{i}");
                    return Task.CompletedTask;
                }, 100, 1000);
                await engine.ForceMaintenance();

                await engine.Close(CancellationToken.None);

                System.IO.File.Delete("test.db");
            }

            engine = Connection<Entity>.Create("test.db", SourceGenerationContext.Default.Entity);
            await engine.OpenAsync(CancellationToken.None);

            await TimeIterations("Save Entities", async (thread, i) =>
            {
                await engine.Save(new Entity($"{thread}-{i}", 0, false, $"""Body Longer Text to take up more space"""));
            }, 100, 1000);

            await TimeIterations("Prefix Search", (thread, i) =>
            {
                var results = engine.GetByPrefix<Entity>(thread.ToString() + "-123", false);
                foreach (var result in results)
                {
                    if (result == null)
                    {
                        throw new Exception("Failed");
                    }
                }
                return Task.CompletedTask;
            }, 100, 1000);

            await engine.Close(CancellationToken.None);
        }


        private static async Task TimeIterations(string activityName, Func<int, int, Task> toDo, int threads, int iterations)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var tasks = new Task[threads];
            var threadLatencyAverages = new double[threads];
            var threadLatencyMaxes = new double[threads];
            var threadLatencyMedian = new double[threads];

            for(int threadI = 0; threadI < threads; threadI++)
            {
                var threadCount = threadI;
                tasks[threadI] = Task.Run(async () =>
                {
                    var taskLatencies = new double[iterations];
                    Stopwatch sw = new Stopwatch();
                    for (int iterationCount = 0; iterationCount < iterations; iterationCount++)
                    {
                        sw.Start();
                        await toDo(threadCount, iterationCount);
                        sw.Stop();
                        taskLatencies[iterationCount] = sw.Elapsed.TotalMilliseconds;
                        sw.Reset();
                    }
                    threadLatencyAverages[threadCount] = taskLatencies.Average();
                    threadLatencyMaxes[threadCount] = taskLatencies.Max();
                    var halfIndex = iterations / 2;
                    threadLatencyMedian[threadCount] = taskLatencies[halfIndex];
                });
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed;

            var totalIterations = threads * iterations;
            var throughput = totalIterations / elapsed.TotalSeconds;

            var threadMedianSorted = threadLatencyMedian.AsEnumerable().OrderBy(x => x).ToList();

            var overallMedian = threadMedianSorted[threadMedianSorted.Count / 2];

            Console.WriteLine($"{activityName} Total Elapsed: {elapsed} - Throughput:{throughput}/s");
            Console.WriteLine($"{activityName} Average Latency by Thread - Avg:{threadLatencyAverages.Average()} Median:{overallMedian} Max:{threadLatencyAverages.Max()}");
        }

    }
}
