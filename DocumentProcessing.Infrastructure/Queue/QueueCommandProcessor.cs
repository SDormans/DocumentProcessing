using System.Diagnostics;
using System.Text.Json;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Queue;

public enum QueueCommandOutcome
{
    Completed,
    AlreadyProcessed,
    Failed,
    RetryScheduled,
    LeaseLost
}

/// <summary>
/// Processes one leased <see cref="QueueCommand"/>: text extraction, LLM invoice extraction and
/// validation (via <see cref="IInvoiceExtractor"/>), then persists the result, the document status
/// and the command completion in a single transaction.
/// </summary>
public class QueueCommandProcessor
{
    private const int MaxErrorLength = 2000;
    private const int MaxErrorCategoryLength = 100;

    private readonly AppDbContext _dbContext;
    private readonly IInvoiceExtractor _invoiceExtractor;
    private readonly QueueProcessingOptions _options;
    private readonly ILogger<QueueCommandProcessor> _logger;

    public QueueCommandProcessor(
        AppDbContext dbContext,
        IInvoiceExtractor invoiceExtractor,
        IOptions<QueueProcessingOptions> options,
        ILogger<QueueCommandProcessor> logger)
    {
        _dbContext = dbContext;
        _invoiceExtractor = invoiceExtractor;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Loads the command by id and processes it if it is still leased by <paramref name="leaseOwner"/>.
    /// </summary>
    public async Task<QueueCommandOutcome> ProcessAsync(Guid commandId, string leaseOwner, CancellationToken cancellationToken)
    {
        var command = await _dbContext.QueueCommands.FirstOrDefaultAsync(c => c.Id == commandId, cancellationToken);
        if (command is null || command.LeaseOwner != leaseOwner)
            return Record(QueueCommandOutcome.LeaseLost, Stopwatch.GetTimestamp());

        return await ProcessAsync(command, cancellationToken);
    }

    public async Task<QueueCommandOutcome> ProcessAsync(QueueCommand command, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = PipelineTelemetry.ActivitySource.StartActivity("ProcessQueueCommand");
        activity?.SetTag("queue.command_id", command.Id);
        activity?.SetTag("document.id", command.DocumentId);
        activity?.SetTag("queue.attempt", command.AttemptCount);

        using var _ = _logger.BeginScope(new Dictionary<string, object>
        {
            ["QueueCommandId"] = command.Id,
            ["DocumentId"] = command.DocumentId,
            ["Attempt"] = command.AttemptCount
        });

        try
        {
            var outcome = await ProcessCoreAsync(command, cancellationToken);
            activity?.SetTag("queue.outcome", outcome.ToString());
            return Record(outcome, started);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Lease on queue command {QueueCommandId} was lost to another worker; discarding this attempt.", command.Id);
            return Record(QueueCommandOutcome.LeaseLost, started);
        }
    }

    private async Task<QueueCommandOutcome> ProcessCoreAsync(QueueCommand command, CancellationToken cancellationToken)
    {
        var document = await _dbContext.Documents.FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken);
        if (document is null)
        {
            command.MarkFailed("DocumentNotFound");
            await _dbContext.SaveChangesAsync(cancellationToken);
            return QueueCommandOutcome.Failed;
        }

        // Idempotency: a redelivered command for an already-extracted document is a no-op.
        if (document.Status is DocumentStatus.NeedsReview or DocumentStatus.Completed)
        {
            _logger.LogInformation("Document {DocumentId} already processed ({Status}); completing command.", document.Id, document.Status);
            command.MarkCompleted();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return QueueCommandOutcome.AlreadyProcessed;
        }

        if (document.Status == DocumentStatus.Failed)
        {
            command.MarkFailed("DocumentFailed");
            await _dbContext.SaveChangesAsync(cancellationToken);
            return QueueCommandOutcome.Failed;
        }

        // Reclaimed after an expired lease (e.g. the previous worker crashed) with no attempts left.
        if (command.AttemptCount > _options.MaxAttempts)
        {
            await FailAsync(command, document, "MaxAttemptsExceeded", cancellationToken);
            return QueueCommandOutcome.Failed;
        }

        // Renewing the lease puts the command (and its LeaseOwner concurrency token) in this UPDATE,
        // so a worker whose lease was taken over cannot overwrite the document status.
        command.RenewLease(_options.LeaseDuration);
        document.UpdateStatus(DocumentStatus.Processing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        InvoiceExtractionResult result;
        try
        {
            result = await _invoiceExtractor.ImproveAsync(document.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: hand the command back immediately instead of waiting for the lease to expire.
            command.RecordRetry("WorkerShutdown", TimeSpan.Zero);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            return await HandleTransientFailureAsync(command, document, ex, cancellationToken);
        }

        await AddResultAsync(document.Id, result, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Extraction failed for document {DocumentId}: {Warnings}", document.Id, string.Join("; ", result.Warnings));
            await FailAsync(command, document, "ExtractionFailed", cancellationToken);
            return QueueCommandOutcome.Failed;
        }

        document.UpdateStatus(DocumentStatus.NeedsReview);
        command.MarkCompleted();
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Document {DocumentId} extracted and ready for review.", document.Id);
        return QueueCommandOutcome.Completed;
    }

    private async Task<QueueCommandOutcome> HandleTransientFailureAsync(
        QueueCommand command, Document document, Exception ex, CancellationToken cancellationToken)
    {
        var category = Truncate($"Transient:{ex.GetType().Name}", MaxErrorCategoryLength);

        if (command.AttemptCount >= _options.MaxAttempts)
        {
            _logger.LogError(ex, "Processing document {DocumentId} failed on final attempt {Attempt}.", document.Id, command.AttemptCount);
            await FailAsync(command, document, category, cancellationToken);
            return QueueCommandOutcome.Failed;
        }

        var delay = _options.BaseRetryDelay * Math.Pow(2, Math.Max(0, command.AttemptCount - 1));
        _logger.LogWarning(ex, "Processing document {DocumentId} failed on attempt {Attempt}; retrying in {Delay}.", document.Id, command.AttemptCount, delay);
        command.RecordRetry(category, delay);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return QueueCommandOutcome.RetryScheduled;
    }

    private async Task FailAsync(QueueCommand command, Document document, string category, CancellationToken cancellationToken)
    {
        document.UpdateStatus(DocumentStatus.Failed);
        command.MarkFailed(category);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AddResultAsync(Guid documentId, InvoiceExtractionResult result, CancellationToken cancellationToken)
    {
        var latestVersion = await _dbContext.InvoiceExtractionResults
            .Where(r => r.DocumentId == documentId)
            .MaxAsync(r => (int?)r.ExtractionVersion, cancellationToken) ?? 0;

        var warnings = result.ExtractionVersion > 1
            ? result.Warnings.Append("Result produced by a validation-driven correction pass.").ToList()
            : result.Warnings;

        _dbContext.InvoiceExtractionResults.Add(new InvoiceExtractionResultEntity
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            ExtractionVersion = latestVersion + 1,
            ExtractionMethod = Truncate(result.ExtractionMethod, 50),
            PageCount = result.PageCount,
            TotalCharacterCount = result.TotalCharacterCount,
            RawAiResponse = string.IsNullOrEmpty(result.RawResponse) ? null : result.RawResponse,
            ErrorMessage = result.Success ? null : Truncate(string.Join("; ", result.Warnings), MaxErrorLength),
            SupplierName = result.SupplierName,
            SupplierNameHasValue = result.SupplierNameHasValue,
            InvoiceNumber = result.InvoiceNumber,
            InvoiceNumberHasValue = result.InvoiceNumberHasValue,
            InvoiceDate = AsUtc(result.InvoiceDate),
            InvoiceDateHasValue = result.InvoiceDateHasValue,
            DueDate = AsUtc(result.DueDate),
            DueDateHasValue = result.DueDateHasValue,
            Currency = result.Currency,
            CurrencyHasValue = result.CurrencyHasValue,
            Subtotal = result.Subtotal,
            SubtotalHasValue = result.SubtotalHasValue,
            TaxAmount = result.TaxAmount,
            TaxAmountHasValue = result.TaxAmountHasValue,
            TotalAmount = result.TotalAmount,
            TotalAmountHasValue = result.TotalAmountHasValue,
            Lines = result.Lines is { Count: > 0 } ? string.Join("\n", result.Lines) : null,
            LinesHasValue = result.LinesHasValue,
            LineItemsJson = result.Lines is { Count: > 0 } ? JsonSerializer.Serialize(result.Lines) : null,
            WarningsJson = JsonSerializer.Serialize(warnings),
            ExtractedAtUtc = DateTime.UtcNow
        });
    }

    private static QueueCommandOutcome Record(QueueCommandOutcome outcome, long started)
    {
        PipelineTelemetry.CommandsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        PipelineTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));
        return outcome;
    }

    // Npgsql maps DateTime to timestamptz and rejects Kind=Unspecified.
    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
