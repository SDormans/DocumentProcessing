using System.Collections.Concurrent;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Queue;
using DocumentProcessing.Worker;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Worker;

public class DocumentProcessingWorkerTests
{
    [Fact]
    public async Task SlowCommand_DoesNotBlockClaimingMoreWork()
    {
        var slow = new QueueCommand(Guid.NewGuid());
        var fast = new QueueCommand(Guid.NewGuid());
        var next = new QueueCommand(Guid.NewGuid());
        var claims = new Queue<IReadOnlyList<QueueCommand>>([[slow, fast], [next]]);
        var repository = new Mock<IQueueCommandRepository>();
        repository.Setup(r => r.ClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { lock (claims) return claims.Count > 0 ? claims.Dequeue() : []; });

        var processor = new BlockingProcessor(slow.Id);
        var services = new ServiceCollection()
            .AddSingleton(repository.Object)
            .AddSingleton<IQueueCommandProcessor>(processor)
            .BuildServiceProvider();
        var options = new QueueProcessingOptions { BatchSize = 2, PollInterval = TimeSpan.FromMilliseconds(20) };
        var worker = new DocumentProcessingWorker(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), NullLogger<DocumentProcessingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await processor.WaitForAsync(next.Id, TimeSpan.FromSeconds(5));
            processor.Processed.Should().Contain([fast.Id, next.Id]).And.NotContain(slow.Id);
            repository.Verify(r => r.ClaimAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), 1, It.IsAny<CancellationToken>()), Times.AtLeastOnce,
                "only the freed slot is claimed while the slow command is still running");
        }
        finally
        {
            processor.Release();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class BlockingProcessor(Guid blockedId) : IQueueCommandProcessor
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _done = new();
        public ConcurrentQueue<Guid> Processed { get; } = new();

        public async Task<QueueCommandOutcome> ProcessAsync(Guid commandId, string leaseOwner, CancellationToken cancellationToken)
        {
            if (commandId == blockedId)
                await _release.Task.WaitAsync(cancellationToken);
            Processed.Enqueue(commandId);
            Signal(commandId).TrySetResult();
            return QueueCommandOutcome.Completed;
        }

        public void Release() => _release.TrySetResult();

        public Task WaitForAsync(Guid commandId, TimeSpan timeout) => Signal(commandId).Task.WaitAsync(timeout);

        private TaskCompletionSource Signal(Guid id) => _done.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
