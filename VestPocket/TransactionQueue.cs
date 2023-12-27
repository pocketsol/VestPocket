using System.Diagnostics;
using System.Threading.Channels;

namespace VestPocket;

/// <summary>
/// Takes requests from the data store and flattens them to a single
/// thread by forcing them to pass through a thread safe queue with a single consumer.
/// Responsible for processing each transaction and passing the results to the entity store
/// and to the transaction log.
/// </summary>
internal class TransactionQueue<TBaseType> where TBaseType : class, IEntity
{
    private readonly TransactionLog<TBaseType> transactionStore;
    private readonly EntityStore<TBaseType> memoryStore;
    private Task processQueuesTask;
    private CancellationTokenSource processQueuesCancellationTokenSource;
    private readonly Channel<Transaction<TBaseType>> queueItemChannel;

    public TransactionMetrics Metrics { get; init; } = new();


    public TransactionQueue(
        TransactionLog<TBaseType> transactionStore,
        EntityStore<TBaseType> memoryStore
    )
    {
        this.queueItemChannel = Channel.CreateUnbounded<Transaction<TBaseType>>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        this.transactionStore = transactionStore;
        this.memoryStore = memoryStore;
    }

    public Task Start()
    {
        if (this.processQueuesTask != null)
        {
            throw new InvalidOperationException("Already processing transactions");
        }
        this.processQueuesCancellationTokenSource = new CancellationTokenSource();
        this.processQueuesTask = Task.Run(ProcessQueue);
        return Task.CompletedTask;
    }

    public async Task Stop()
    {
        this.processQueuesCancellationTokenSource.Cancel();
        this.queueItemChannel.Writer.Complete();
        await processQueuesTask;
    }

    private async Task ProcessQueue()
    {

        var cancellationToken = processQueuesCancellationTokenSource.Token;

        Stopwatch sw = new();
        try
        {
            while (!cancellationToken.IsCancellationRequested )
            {
                Transaction<TBaseType> transaction = await queueItemChannel.Reader.ReadAsync(cancellationToken);

                if (transactionStore.RewriteReadyToComplete)
                {
                    transactionStore.CompleteRewrite();
                }

                this.Metrics.queueWaits++;
                sw.Restart();
                do
                {
                    if (transaction.Count == 0)
                    {
                        transaction.Complete();
                        continue;
                    }
                    var appliedInMemory = memoryStore.ProcessTransaction(transaction);
                    this.Metrics.validationTime += sw.Elapsed;

                    if (appliedInMemory)
                    {
                        this.Metrics.bytesSerialized += transactionStore.WriteTransaction(transaction);
                    }
                    this.Metrics.transactionCount++;
                } while (queueItemChannel.Reader.TryRead(out transaction));

                transactionStore.ProcessDelay();
            }
        }
        catch (OperationCanceledException)
        {
        }

    }

    public void Enqueue(Transaction<TBaseType> transaction)
    {
        if (!queueItemChannel.Writer.TryWrite(transaction))
        {
            if (processQueuesCancellationTokenSource.IsCancellationRequested)
            {
                throw new Exception("Get by prefix request can't complete because Transaction processor is shutting down");
            }
            else
            {
                // Should never happen. TryWrite is always supposed to succeed
                // when channel is open and was created unbound
                throw new Exception("Could not write to transaction processor get queue for an unknown reason");
            }
        }
    }
}
