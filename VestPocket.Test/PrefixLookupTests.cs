using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace VestPocket.Test;

public class PrefixLookupTests
{

    private const int iterationSize = 1000;
    private readonly ITestOutputHelper output;

    public PrefixLookupTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    public record class TestEntity(string Key, int Version, bool Deleted, string Body) : IEntity
    {
        public IEntity WithVersion(int version)
        {
            return this with { Version = version };
        }
    }

    private Random rng = new(Environment.TickCount);

    /// <summary>
    /// A rather brutish way to confirm that the results returning from the PrefixLookup are stable
    /// with a single writer and many readers.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task ConcurrentWrite_PrefixLookupResultsAreStable()
    {
        Dictionary<string, TestEntity> expectedLookup = new();
        List<TestEntity> expectedValues = new();
        
        for(int i = 0; i < iterationSize; i++)
        {
            var testEntity = new TestEntity(i.ToString(), 0, false, i.ToString());
            expectedValues.Add(testEntity);
            expectedLookup.Add(i.ToString(), testEntity);
        }

        var iterationCount = 0;


        CancellationTokenSource timeoutCancellation = new(3000);
        var readers = new Task[5];
        while (!timeoutCancellation.IsCancellationRequested)
        {
            CancellationTokenSource iterationCancellation = new();
            PrefixLookup<TestEntity> lookup = new(false);

            var writeTask = Task.Run(() => WriteValues(lookup, expectedValues, iterationCancellation), timeoutCancellation.Token);

            for (var i = 0; i < readers.Length; i++)
            {
                readers[i] = Task.Run(() => ReadAndVerifyPrefixValues(lookup, expectedLookup, iterationCancellation.Token), timeoutCancellation.Token);
            }

            await writeTask;
            await Task.WhenAll(readers);

            ReadAndVerifyPrefixCounts(lookup, expectedValues);
            iterationCount++;

            output.WriteLine($"Completed: {iterationCount} prefix validations ({iterationSize * iterationCount} items)");
        }
    }

    private void ReadAndVerifyPrefixCounts(PrefixLookup<TestEntity> lookup, List<TestEntity> expectedValues)
    {
        for(int i = 0; i < iterationSize; i++)
        {
            var prefix = i.ToString();
            var expectedCount = expectedValues.Where(x => x.Key.StartsWith(prefix)).Count();
            var actualCount = lookup.GetPrefix<TestEntity>(prefix).Count();
            if (actualCount != expectedCount)
            {
                throw new Exception("Invalid prefix search results. Expected: " + expectedCount + " actual:" + actualCount);
            }
        }
    }

    private void ReadAndVerifyPrefixValues(PrefixLookup<TestEntity> lookup, Dictionary<string, TestEntity> expectedLookup, CancellationToken cancellationToken)
    {

        while (!cancellationToken.IsCancellationRequested)
        {
            var nextKey = rng.Next(0, iterationSize).ToString();
            var queryResult = lookup.GetPrefix<TestEntity>(nextKey);
            int resultCount = 0;
            foreach (var result in queryResult)
            {
                resultCount++;
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                if (result == null)
                {
                    throw new NullReferenceException("A PrefixLookup returned a null result");
                }

                if (expectedLookup.ContainsKey(result.Key))
                {
                    var expected = expectedLookup[result.Key];
                    if (expected != result)
                    {
                        throw new NullReferenceException("An invalid object reference was returned");
                    }
                }

                if (!result.Key.StartsWith(nextKey))
                {
                    throw new Exception(
                        "ReadAnVerifyPrefixes encountered a prefix result that did not start with the expected prefix");
                }
            }

        }
    }

    private void WriteValues(PrefixLookup<TestEntity> lookup, List<TestEntity> expectedValues, CancellationTokenSource cancellationTokenSource)
    {
        // Write keys at random
        for (int i = 0; i < iterationSize; i++)
        {
            var next = rng.Next(0, iterationSize);
            var nextValue = expectedValues[next];
            lookup.Set(next.ToString(), nextValue);
        }
        
        // Then go back and make sure all keys are written
        for (int i = 0; i < iterationSize; i++)
        {
            var nextValue = expectedValues[i];
            lookup.Set(i.ToString(), nextValue);
        }
        cancellationTokenSource.Cancel();
    }
    
    
    
}