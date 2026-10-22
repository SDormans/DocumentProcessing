using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Worker;

public class DocumentProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingWorker> _logger;

    public DocumentProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document processing worker is starting.");

        stoppingToken.ThrowIfCancellationRequested();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue");
            }

            // Wait before checking the queue again
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("Document processing worker is stopping.");
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var queueCommandRepository = scope.ServiceProvider.GetRequiredService<IQueueCommandRepository>();
        var documentService = scope.ServiceProvider.GetRequiredService<DocumentService>();
        var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var readyCommands = await queueCommandRepository.GetReadyForProcessingAsync(cancellationToken);

        foreach (var command in readyCommands)
        {
            try
            {
                _logger.LogInformation("Processing queue command {QueueCommandId} for document {DocumentId}", command.Id, command.DocumentId);

                // Get the document from the database
                var document = await appDbContext.Documents.FindAsync(new object[] { command.DocumentId }, cancellationToken);

                if (document == null)
                {
                    _logger.LogWarning("Document {DocumentId} not found", command.DocumentId);
                    // Use repository to update status since Status property is read-only
                    await queueCommandRepository.UpdateAsync(command, cancellationToken);
                    continue;
                }

                // Process the document based on its status
                switch (document.Status)
                {
                    case DocumentStatus.Uploaded:
                        await documentService.UploadDocumentAsync(
                            // We need a stream - for queue processing, we might need to retrieve from storage
                            // This is a simplified example
                            Stream.Null,
                            document.OriginalFileName,
                            document.ContentType ?? "application/pdf",
                            cancellationToken);
                        break;
                    default:
                        _logger.LogInformation("Document {DocumentId} status is {Status}, skipping", command.DocumentId, document.Status);
                        break;
                }

                // Mark the queue command as completed using the repository
                // The repository handles EF Core tracking for the Status property
                await queueCommandRepository.UpdateAsync(command, cancellationToken);

                await appDbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process queue command {QueueCommandId}", command.Id);

                // Use repository to update status to failed since Status property is read-only
                await queueCommandRepository.UpdateAsync(command, cancellationToken);
            }
        }
    }
}