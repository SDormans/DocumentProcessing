using DocumentProcessing.Infrastructure.Queue;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Worker;

/// <summary>
/// Polls the database-backed queue, leases due commands and processes them in parallel, each in
/// its own DI scope. See docs/adr/0001-database-backed-queue.md.
/// </summary>
public class DocumentProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QueueProcessingOptions _options;
    private readonly ILogger<DocumentProcessingWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public DocumentProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<QueueProcessingOptions> options,
        ILogger<DocumentProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document processing worker {WorkerId} started.", _workerId);

        var nextPurgeUtc = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow >= nextPurgeUtc)
            {
                nextPurgeUtc = DateTime.UtcNow + _options.PurgeInterval;
                await PurgeCompletedCommandsAsync(stoppingToken);
            }

            var claimed = 0;
            try
            {
                claimed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while polling the processing queue.");
            }

            // A full batch suggests more work is waiting; poll again immediately.
            if (claimed < _options.BatchSize)
            {
                try
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Document processing worker {WorkerId} stopped.", _workerId);
    }

    internal async Task PurgeCompletedCommandsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IQueueCommandRepository>();
            var purged = await repository.PurgeCompletedAsync(DateTime.UtcNow - _options.CompletedRetention, stoppingToken);
            if (purged > 0)
            {
                _logger.LogInformation("Purged {Count} completed queue commands older than {Retention}.", purged, _options.CompletedRetention);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Purging completed queue commands failed.");
        }
    }

    internal async Task<int> ProcessBatchAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<Guid> commandIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IQueueCommandRepository>();
            var claimed = await repository.ClaimAsync(_workerId, _options.LeaseDuration, _options.BatchSize, stoppingToken);
            commandIds = claimed.Select(c => c.Id).ToList();
        }

        if (commandIds.Count == 0)
            return 0;

        _logger.LogInformation("Claimed {Count} queue command(s).", commandIds.Count);

        await Parallel.ForEachAsync(
            commandIds,
            new ParallelOptions { MaxDegreeOfParallelism = _options.BatchSize, CancellationToken = stoppingToken },
            async (commandId, ct) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<QueueCommandProcessor>();
                try
                {
                    await processor.ProcessAsync(commandId, _workerId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The lease expires and the command is reclaimed, which counts as another attempt.
                    _logger.LogError(ex, "Unexpected error processing queue command {QueueCommandId}.", commandId);
                }
            });

        return commandIds.Count;
    }
}
