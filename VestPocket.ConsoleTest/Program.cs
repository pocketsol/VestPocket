using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VestPocket.ConsoleTest;


class Program
{
    static VestPocketStore<Entity> connection;


    static async Task Main(string[] args)
    {

        int threads = 15;
        int iterations = 10000;

        Console.WriteLine("---------Running VestPocket---------");

        var options = new VestPocketOptions();
        options.FilePath = null;
        options.RewriteRatio = 1;

        RemoveDatabaseFile(options);
        connection = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, options);
        await connection.OpenAsync(CancellationToken.None);

        await TimeIterations("Create Entities", (thread, i) =>
        {
            for (int j = 0; j < 1000; j++)
            {
                var entity = new Entity($"{thread}-{i}", 0, false, $"""Just some body text {thread}-{i}""");
                if (entity == null)
                {
                    throw new Exception();
                }
            }

            return Task.CompletedTask;
        }, threads, iterations, 1000);

        await TimeIterations("Save Entities", async (thread, i) =>
        {
            await connection.Save(new Entity($"{thread}-{i}", 0, false, $"""Just some body text {thread}-{i}"""));
        }, threads, iterations);

        await connection.ForceMaintenance();

        await TimeIterations("Read Entities", (thread, i) =>
        {
            for (int j = 0; j < 1000; j++)
            {
                connection.Get($"{thread}-{i}");
            }
            return Task.CompletedTask;
        }, threads, iterations, 1000);

        await TimeIterations($"Save Entities Batched", async (thread, i) =>
        {
            var entities = new Entity[100];
            int iterationKey = 0;
            while (iterationKey < iterations)
            {
                for (int j = 0; j < entities.Length; j++)
                {
                    iterationKey++;
                    entities[j] = new Entity($"{thread}-{iterationKey}", 1, false, $"""Just some body text {thread}-{i}""");
                    if (iterationKey == iterations)
                    {
                        break;
                    }
                }
                await connection.Save(entities);
            }

        }, threads, 1, iterations);

        await connection.ForceMaintenance();

        await TimeIterations("Prefix Search", (thread, i) =>
        {
            var prefixSearch = connection.GetByPrefix<Entity>(thread.ToString() + "-99");
            foreach(var result in prefixSearch)
            {
                if (result == null)
                {
                    throw new Exception("Failed to get prefix result");
                }
            }
            return Task.CompletedTask;
        }, threads, iterations);

        await TimeIterations("Read and Write Mix", async (thread, i) =>
        {
            var entity = connection.Get<Entity>($"{thread}-{i}");

            await connection.Save(entity);
            entity = connection.Get<Entity>($"{thread}-{i}");
            entity = connection.Get<Entity>($"{thread}-{i}");

            entity = await connection.Save(entity);
            entity = connection.Get<Entity>($"{thread}-{i}");
            entity = connection.Get<Entity>($"{thread}-{i}");

            entity = await connection.Save(entity);
            entity = connection.Get<Entity>($"{thread}-{i}");
            entity = connection.Get<Entity>($"{thread}-{i}");

        }, threads, iterations, 10);

        await connection.ForceMaintenance();

        Console.WriteLine();
        Console.WriteLine("-----Transaction Metrics-------------");
        Console.WriteLine($"Transaction Count: {connection.TransactionMetrics.TransactionCount}");
        Console.WriteLine($"Flush Count: {connection.TransactionMetrics.FlushCount}");
        Console.WriteLine($"Validation Time: {connection.TransactionMetrics.AverageValidationTime.TotalMicroseconds}us");
        Console.WriteLine($"Serialized Bytes: {connection.TransactionMetrics.BytesSerialized}");

        Console.WriteLine($"Queue Length: {connection.TransactionMetrics.AverageQueueLength}");

        await connection.Close(CancellationToken.None);
        connection.Dispose();
        Console.WriteLine();
    }

    private static void RemoveDatabaseFile(VestPocketOptions options)
    {
        var fileName = options.FilePath;
        if (fileName is not null && System.IO.File.Exists(fileName))
        {
            System.IO.File.Delete(fileName);
        }
    }

    private static async Task TimeIterations(string activityName, Func<int, int, Task> toDo, int threads, int iterations, double scale = 1)
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
                    var threadCountI = threadCount;
                    var iterationI = iterationCount;
                    await toDo(threadCountI, iterationI);
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

        var totalIterations = threads * iterations * scale;
        var throughput = totalIterations / elapsed.TotalSeconds;

        var threadMedianSorted = threadLatencyMedian.AsEnumerable().OrderBy(x => x).ToList();
        var overallMedian = threadMedianSorted[threadMedianSorted.Count / 2];
        Console.WriteLine();
        Console.WriteLine($"--{activityName} (threads:{threads}, iterations:{iterations}), ops/iteration:{scale}--");
        Console.WriteLine($"Throughput {throughput.ToString("F0")}/s");
        Console.WriteLine($"Latency Median: {overallMedian.ToString("N6")} Max:{threadLatencyAverages.Max().ToString("N6")}");
    }

}
