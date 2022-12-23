using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace VestPocket.ConsoleTest;


class Program
{
    static VestPocketStore<Entity> connection;


    static async Task Main(string[] args)
    {
        RemoveDatabaseFiles();

        int threads = 100;
        int iterations = 1000;

        Console.WriteLine("---------Running VestPocket---------");

        var options = new VestPocketOptions();
        options.CompressOnRewrite = true;

        connection = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, options);
        await connection.OpenAsync(CancellationToken.None);

        await TimeIterations("Save Entities", async (thread, i) =>
        {
            await connection.Save(new Entity($"{thread}-{i}", 0, false, $"""Just some body text {thread}-{i}"""));
        }, threads, iterations);

        await TimeIterations("Read Entities", (thread, i) =>
        {
            connection.Get<Entity>($"{thread}-{i}");
            return Task.CompletedTask;
        }, threads, iterations);

        await TimeIterations("Prefix Search", (thread, i) =>
        {
            using var prefixSearch = connection.GetByPrefix<Entity>(thread.ToString() + "-99");
            foreach (var result in prefixSearch.Results)
            {
                if (result == null)
                {
                    throw new Exception("Failed");
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
        }, threads, iterations);

        await connection.ForceMaintenance();
        Console.WriteLine("-----Transaction Metrics-------------");
        Console.WriteLine($"Count: {connection.TransactionMetrics.Count}");
        Console.WriteLine($"Validation Time: {connection.TransactionMetrics.AverageValidationTime.TotalMicroseconds}us");
        Console.WriteLine($"Serialization Time: {connection.TransactionMetrics.AverageSerializationTime.TotalMicroseconds}us");
        Console.WriteLine($"Serialized Bytes: {connection.TransactionMetrics.BytesSerialized}");

        Console.WriteLine($"Queue Length: {connection.TransactionMetrics.AverageQueueLength}");

        await connection.Close(CancellationToken.None);
        connection.Dispose();
        Console.WriteLine();

        Console.WriteLine("-----Running VestPocket ReadOnly-----");

        var readOnlyConnection = new VestPocketStore<Entity>(SourceGenerationContext.Default.Entity, VestPocketOptions.DefaultReadOnly);
        await readOnlyConnection.OpenAsync(CancellationToken.None);

        await TimeIterations("Read Entities", (thread, i) =>
        {
            readOnlyConnection.Get<Entity>($"{thread}-{i}");
            return Task.CompletedTask;
        }, threads, iterations);

        await TimeIterations("Prefix Search", (thread, i) =>
        {
            using var prefixSearch = readOnlyConnection.GetByPrefix<Entity>(thread.ToString() + "-99");
            foreach (var result in prefixSearch.Results)
            {
                if (result == null)
                {
                    throw new Exception("Failed");
                }
            }
            return Task.CompletedTask;
        }, threads, iterations);
        readOnlyConnection.Close(CancellationToken.None).Wait();
        readOnlyConnection.Dispose();

        Console.WriteLine();

        Console.WriteLine("----Running ConcurrentDictionary----");

        ConcurrentDictionary<string, Entity> dictionary = new();
        await TimeIterations("ConcurrentDictionary Save Entities", (thread, i) =>
        {
            var entity = new Entity($"{thread}-{i}", 0, false, $"""Just some body text {thread}-{i}""");
            dictionary.AddOrUpdate(entity.Key, entity, (k, e) => e);
            return Task.CompletedTask;
        }, threads, iterations);

        await TimeIterations("ConcurrentDictionary Read Entities", (thread, i) =>
        {
            var key = $"{thread}-{i}";
            dictionary.TryGetValue(key, out var entity);
            return Task.CompletedTask;
        }, threads, iterations);

        await TimeIterations("ConcurrentDictionary Read and Write Mix", (thread, i) =>
        {
            string key = $"{thread}-{i}";
            if (dictionary.TryGetValue(key, out var entity))
            {
                dictionary.AddOrUpdate(entity.Key, entity, (k, e) => e);
                dictionary.TryGetValue(key, out entity);
                dictionary.TryGetValue(key, out entity);
                dictionary.AddOrUpdate(entity.Key, entity, (k, e) => e);
                dictionary.TryGetValue(key, out entity);
                dictionary.TryGetValue(key, out entity);
            }
            return Task.CompletedTask;
        }, threads, iterations);
        Console.WriteLine();

        RemoveDatabaseFiles();

    }

    private static void RemoveDatabaseFiles()
    {
        var fileName = VestPocketOptions.Default.FilePath;
        if (System.IO.File.Exists(fileName))
        {
            System.IO.File.Delete(fileName);
        }
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

        var totalIterations = threads * iterations;
        var throughput = totalIterations / elapsed.TotalSeconds;

        var threadMedianSorted = threadLatencyMedian.AsEnumerable().OrderBy(x => x).ToList();

        var overallMedian = threadMedianSorted[threadMedianSorted.Count / 2];
        Console.WriteLine();
        Console.WriteLine($"--{activityName} (threads:{threads}, iterations:{iterations})--");
        Console.WriteLine($"Throughput {throughput.ToString("F0")}/s");
        Console.WriteLine($"Latency Median: {overallMedian.ToString("N6")} Max:{threadLatencyAverages.Max().ToString("N6")}");
    }

}
