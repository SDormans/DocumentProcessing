using DocumentProcessing.Infrastructure.Queue;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Worker;

/// <summary>
/// Polls the database-backed queue, leases due commands and processes up to
/// <see cref="QueueProcessingOptions.BatchSize"/> of them at a time, each in its own DI scope. See docs/adr/0001-database-backed-queue.md.
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

        // Each command holds a slot until it finishes, so one slow document never blocks the others.
        var inFlight = new HashSet<Task>();
        var nextPurgeUtc = DateTime.UtcNow;
        var nextRetentionPurgeUtc = DateTime.UtcNow;
        var nextQueueDepthUtc = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow >= nextQueueDepthUtc)
            {
                nextQueueDepthUtc = DateTime.UtcNow + QueueDepthInterval;
                await MeasureQueueDepthAsync(stoppingToken);
            }

            if (DateTime.UtcNow >= nextPurgeUtc)
            {
                nextPurgeUtc = DateTime.UtcNow + _options.PurgeInterval;
                await PurgeCompletedCommandsAsync(stoppingToken);
            }

            if (DateTime.UtcNow >= nextRetentionPurgeUtc)
                nextRetentionPurgeUtc = DateTime.UtcNow + await PurgeExpiredDocumentsAsync(stoppingToken);

            var freeSlots = _options.BatchSize - inFlight.Count;
            if (freeSlots > 0)
            {
                try
                {
                    foreach (var (commandId, leaseOwner) in await ClaimAsync(freeSlots, stoppingToken))
                        inFlight.Add(Task.Run(() => ProcessCommandAsync(commandId, leaseOwner, stoppingToken), CancellationToken.None));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while polling the processing queue.");
                }
            }

            // All slots busy: wait for one to free up. Otherwise the queue is drained: wait for the
            // poll interval, or a finished command, whichever comes first.
            var wakeUp = inFlight.Count >= _options.BatchSize
                ? Task.WhenAny(inFlight)
                : Task.WhenAny(inFlight.Append(Task.Delay(_options.PollInterval, stoppingToken)));
            try
            {
                await (await wakeUp);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            inFlight.RemoveWhere(t => t.IsCompleted);
        }

        await Task.WhenAll(inFlight);
        _logger.LogInformation("Document processing worker {WorkerId} stopped.", _workerId);
    }

    /// <returns>The time until the next purge (<c>Retention:PurgeInterval</c>).</returns>
    internal async Task<TimeSpan> PurgeExpiredDocumentsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var interval = (scope.ServiceProvider.GetService<IOptions<DocumentProcessing.Application.Services.RetentionOptions>>()?.Value
            ?? new DocumentProcessing.Application.Services.RetentionOptions()).PurgeInterval;
        try
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<DocumentProcessing.Application.Services.IDocumentDeletion>()
                .PurgeExpiredAsync(DateTime.UtcNow, stoppingToken);
            if (deleted > 0)
                _logger.LogInformation("Deleted {Count} document(s) past their retention period.", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Purging documents past their retention period failed.");
        }
        return interval;
    }

    private static readonly TimeSpan QueueDepthInterval = TimeSpan.FromSeconds(30);

    internal async Task MeasureQueueDepthAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IQueueCommandRepository>();
            PipelineTelemetry.RecordQueueDepth(await repository.CountActiveAsync(stoppingToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Measuring the queue depth failed.");
        }
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

    private async Task<IReadOnlyList<(Guid Id, string LeaseOwner)>> ClaimAsync(int count, CancellationToken stoppingToken)
    {
        // A lease owner per claim, not per worker: if this worker reclaims its own expired command while the
        // first attempt is still running, the stale attempt must fail the lease and concurrency checks.
        var leaseOwner = $"{_workerId}:{Guid.NewGuid():N}";
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IQueueCommandRepository>();
        var claimed = await repository.ClaimAsync(leaseOwner, _options.LeaseDuration, count, stoppingToken);
        if (claimed.Count > 0)
            _logger.LogInformation("Claimed {Count} queue command(s).", claimed.Count);
        return claimed.Select(c => (c.Id, leaseOwner)).ToList();
    }

    private async Task ProcessCommandAsync(Guid commandId, string leaseOwner, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IQueueCommandProcessor>();
            await processor.ProcessAsync(commandId, leaseOwner, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the lease expires and another worker reclaims the command.
        }
        catch (Exception ex)
        {
            // The lease expires and the command is reclaimed, which counts as another attempt.
            _logger.LogError(ex, "Unexpected error processing queue command {QueueCommandId}.", commandId);
        }
    }
}
