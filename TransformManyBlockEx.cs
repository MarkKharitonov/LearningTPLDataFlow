using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace TPLDataFlow
{
    // https://stackoverflow.com/a/69149830/80002
    public class TransformManyBlockEx<TInput, TOutput> : IPropagatorBlock<TInput, TOutput>, IReceivableSourceBlock<TOutput>
    {
        private readonly TransformBlock<TInput, (long, TInput)> m_input;
        private readonly ActionBlock<(long, TInput)> m_transformer;
        private readonly BufferBlock<TOutput> m_output;
        private readonly Dictionary<long, (Queue<TOutput> Queue, bool Completed)> m_byIndex;
        private readonly CancellationToken m_cancellationToken;
        private long m_currentIndex = 0L;
        private long m_minIndex = 0L;

        public TransformManyBlockEx(Func<TInput, IAsyncEnumerable<TOutput>> transform, ExecutionDataflowBlockOptions dataflowBlockOptions = null)
        {
            // Arguments validation omitted
            dataflowBlockOptions ??= new();
            m_cancellationToken = dataflowBlockOptions.CancellationToken;
            if (dataflowBlockOptions.EnsureOrdered)
            {
                m_byIndex = new Dictionary<long, (Queue<TOutput>, bool)>();
            }

            m_input = new TransformBlock<TInput, (long, TInput)>(item => (m_currentIndex++, item), new()
            {
                BoundedCapacity = dataflowBlockOptions.BoundedCapacity,
                CancellationToken = m_cancellationToken
            });

            m_transformer = new ActionBlock<(long, TInput)>(async entry =>
            {
                var (index, item) = entry;
                Queue<TOutput> queue = null;
                if (m_byIndex != null)
                {
                    // EnsureOrdered is enabled
                    queue = new Queue<TOutput>();
                    lock (m_byIndex)
                    {
                        m_byIndex.Add(index, (queue, false));
                    }
                }
                var resultSequence = transform(item);
                await foreach (var result in resultSequence.WithCancellation(m_cancellationToken))
                {
                    if (m_byIndex != null)
                    {
                        lock (queue)
                        {
                            queue.Enqueue(result);
                        }
                        if (!await SendPendingResultsAsync())
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (!await m_output.SendAsync(result, m_cancellationToken))
                        {
                            return;
                        }
                    }
                }
                if (m_byIndex != null)
                {
                    lock (m_byIndex)
                    {
                        m_byIndex[index] = (queue, true); // Mark as completed
                    }
                    await SendPendingResultsAsync();
                }
            }, dataflowBlockOptions);

            m_input.LinkTo(m_transformer, new() { PropagateCompletion = true });

            m_output = new BufferBlock<TOutput>(dataflowBlockOptions);

            Task transformerPostCompletion = m_transformer.Completion.ContinueWith(t =>
            {
                if (m_byIndex != null)
                {
                    int pendingCount;
                    lock (m_byIndex)
                    {
                        pendingCount = m_byIndex.Count;
                        m_byIndex.Clear(); // Cleanup
                    }
                    if (t.IsCompletedSuccessfully && pendingCount > 0)
                    {
                        throw new InvalidOperationException("The transformer completed before emitting all queued results.");
                    }
                }
            }, TaskScheduler.Default);

            // The Task.WhenAll aggregates nicely the exceptions of the two tasks
            PropagateCompletion(Task.WhenAll(m_transformer.Completion, transformerPostCompletion), m_output);
        }

        private static async void PropagateCompletion(Task sourceCompletion, IDataflowBlock target)
        {
            try
            {
                await sourceCompletion.ConfigureAwait(false);
            }
            catch { }
            var ex = sourceCompletion.IsFaulted ? sourceCompletion.Exception : null;
            if (ex != null)
            {
                target.Fault(ex);
            }
            else
            {
                target.Complete();
            }
        }

        private async Task<bool> SendPendingResultsAsync()
        {
            // Returns false in case the BufferBlock rejected a result
            // This may happen in case of cancellation
            while (TrySendNextPendingResult(out var sendTask))
            {
                if (!await sendTask)
                {
                    return false;
                }
            }
            return true;
        }

        private bool TrySendNextPendingResult(out Task<bool> sendTask)
        {
            // Returns false in case currently there is no pending result
            sendTask = null;
            lock (m_byIndex)
            {
                while (true)
                {
                    if (!m_byIndex.TryGetValue(m_minIndex, out var entry))
                    {
                        return false; // The next queue in not in the dictionary yet
                    }
                    var (queue, completed) = entry; // We found the next queue

                    lock (queue)
                    {
                        if (queue.TryDequeue(out var result))
                        {
                            // We found the next result
                            // Send the result while holding the lock on _byIndex
                            // The BufferBlock respects the order of submited items
                            sendTask = m_output.SendAsync(result, m_cancellationToken);
                            return true;
                        }
                    }

                    // Currently the queue is empty
                    // If it's not completed yet, return. It may have more items later.
                    if (!completed)
                    {
                        return false;
                    }

                    // OK, the queue is now both empty and completed
                    m_byIndex.Remove(m_minIndex); // Remove it
                    m_minIndex++; // Continue with the next queue in order
                }
            }
        }

        public TransformManyBlockEx(Func<TInput, Task<IEnumerable<TOutput>>> transform, ExecutionDataflowBlockOptions dataflowBlockOptions = null)
            : this(ToAsyncEnumerable(transform), dataflowBlockOptions) { }

        public TransformManyBlockEx(Func<TInput, IEnumerable<TOutput>> transform, ExecutionDataflowBlockOptions dataflowBlockOptions = null)
            : this(ToAsyncEnumerable(transform), dataflowBlockOptions) { }

        public Task Completion => m_output.Completion;
        public void Complete() => m_input.Complete();
        void IDataflowBlock.Fault(Exception exception) => ((IDataflowBlock)m_input).Fault(exception);

        public int InputCount => m_input.InputCount + m_input.OutputCount + m_transformer.InputCount;

        public int OutputCount
        {
            get
            {
                int count = m_output.Count;
                if (m_byIndex == null)
                {
                    return count;
                }
                lock (m_byIndex)
                {
                    return count + m_byIndex.Values.Select(e =>
                    {
                        lock (e.Queue)
                        {
                            return e.Queue.Count;
                        }
                    }).Sum();
                }
            }
        }

        public IDisposable LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions) => m_output.LinkTo(target, linkOptions);

        public bool TryReceive(Predicate<TOutput> filter, out TOutput item) => ((IReceivableSourceBlock<TOutput>)m_output).TryReceive(filter, out item);

        public bool TryReceiveAll(out IList<TOutput> items) => ((IReceivableSourceBlock<TOutput>)m_output).TryReceiveAll(out items);

        DataflowMessageStatus ITargetBlock<TInput>.OfferMessage(DataflowMessageHeader messageHeader, TInput messageValue, ISourceBlock<TInput> source, bool consumeToAccept) =>
            ((ITargetBlock<TInput>)m_input).OfferMessage(messageHeader, messageValue, source, consumeToAccept);

        TOutput ISourceBlock<TOutput>.ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target, out bool messageConsumed) =>
            ((ISourceBlock<TOutput>)m_output).ConsumeMessage(messageHeader, target, out messageConsumed);

        bool ISourceBlock<TOutput>.ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target) =>
            ((ISourceBlock<TOutput>)m_output).ReserveMessage(messageHeader, target);

        void ISourceBlock<TOutput>.ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target) =>
            ((ISourceBlock<TOutput>)m_output).ReleaseReservation(messageHeader, target);

        private static Func<TInput, IAsyncEnumerable<TOutput>> ToAsyncEnumerable(Func<TInput, Task<IEnumerable<TOutput>>> transform)
        {
            async IAsyncEnumerable<TOutput> Iterator(TInput item)
            {
                foreach (var result in await transform(item))
                {
                    yield return result;
                }
            }
            return Iterator;
        }

        private static Func<TInput, IAsyncEnumerable<TOutput>> ToAsyncEnumerable(Func<TInput, IEnumerable<TOutput>> transform)
        {
            async IAsyncEnumerable<TOutput> Iterator(TInput item)
            {
                foreach (var result in transform(item))
                {
                    yield return result;
                }
                await Task.CompletedTask; // Suppress CS1998
            }
            return Iterator;
        }
    }
}
