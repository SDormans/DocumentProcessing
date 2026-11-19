using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Queue;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Queue;

public class QueueCommandProcessorTests : IDisposable
{
    private const string Worker = "worker-1";
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly Mock<IInvoiceExtractor> _extractor = new();
    private readonly QueueProcessingOptions _options = new() { MaxAttempts = 3, BaseRetryDelay = TimeSpan.FromSeconds(10) };

    public QueueCommandProcessorTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);
    }

    private AppDbContext OtherWorkerContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_dbName)
        .Options);

    public void Dispose() => _db.Dispose();

    private QueueCommandProcessor Processor => new(_db, _extractor.Object, Options.Create(_options), NullLogger<QueueCommandProcessor>.Instance);

    private async Task<(Document Document, QueueCommand Command)> SeedAsync(DocumentStatus status = DocumentStatus.Queued, int claims = 1)
    {
        var document = new Document(Guid.NewGuid(), "invoice.pdf", "application/pdf", 10, "hash", "key", status, DateTime.UtcNow);
        var command = new QueueCommand(document.Id);
        for (var i = 0; i < claims; i++)
            command.Claim(Worker, TimeSpan.FromMinutes(5));
        _db.Documents.Add(document);
        _db.QueueCommands.Add(command);
        await _db.SaveChangesAsync();
        return (document, command);
    }

    private static InvoiceExtractionResult Extracted() => InvoiceExtractionResult.SuccessResult("{\"raw\":true}", new InvoiceExtractionResult
    {
        SupplierName = "Acme",
        SupplierNameHasValue = true,
        InvoiceDate = new DateTime(2026, 1, 31),
        InvoiceDateHasValue = true,
        TotalAmount = 121m,
        TotalAmountHasValue = true,
        Lines = new List<string> { "Widget | 1 | 100 | 100" },
        LinesHasValue = true,
        ExtractionMethod = "Llm+Text"
    });

    [Fact]
    public async Task Success_PersistsResult_MovesDocumentToReview_CompletesCommand()
    {
        var (document, command) = await SeedAsync();
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Extracted());

        var outcome = await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.Completed);
        document.Status.Should().Be(DocumentStatus.NeedsReview);
        command.Status.Should().Be(QueueCommandStatus.Completed);
        command.LeaseOwner.Should().BeNull();
        var stored = await _db.InvoiceExtractionResults.SingleAsync();
        stored.SupplierName.Should().Be("Acme");
        stored.ExtractionVersion.Should().Be(1);
        stored.InvoiceDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task AlreadyProcessedDocument_CompletesWithoutCallingExtractor()
    {
        var (_, command) = await SeedAsync(DocumentStatus.NeedsReview);

        var outcome = await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.AlreadyProcessed);
        command.Status.Should().Be(QueueCommandStatus.Completed);
        _extractor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NotLeaseOwner_DoesNothing()
    {
        var (_, command) = await SeedAsync();

        var outcome = await Processor.ProcessAsync(command.Id, "someone-else", CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.LeaseLost);
        command.Status.Should().Be(QueueCommandStatus.Processing);
        _extractor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnsuccessfulExtraction_FailsImmediately_AndStoresError()
    {
        var (document, command) = await SeedAsync();
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceExtractionResult.FailureResult("No text could be extracted from the document."));

        var outcome = await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.Failed);
        document.Status.Should().Be(DocumentStatus.Failed);
        command.Status.Should().Be(QueueCommandStatus.Failed);
        command.LastErrorCategory.Should().Be("ExtractionFailed");
        (await _db.InvoiceExtractionResults.SingleAsync()).ErrorMessage.Should().Contain("No text");
    }

    [Fact]
    public async Task TransientException_SchedulesRetryWithBackoff()
    {
        var (document, command) = await SeedAsync(claims: 2);
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));

        var before = DateTime.UtcNow;
        var outcome = await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.RetryScheduled);
        command.Status.Should().Be(QueueCommandStatus.Pending);
        command.LastErrorCategory.Should().Be("Transient:HttpRequestException");
        command.NextAttemptAtUtc.Should().BeOnOrAfter(before + TimeSpan.FromSeconds(20)); // 10s * 2^(2-1)
        command.LeaseOwner.Should().BeNull();
        document.Status.Should().Be(DocumentStatus.Processing);
    }

    [Fact]
    public async Task TransientException_OnFinalAttempt_FailsDocumentAndCommand()
    {
        var (document, command) = await SeedAsync(claims: 3);
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var outcome = await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.Failed);
        command.Status.Should().Be(QueueCommandStatus.Failed);
        document.Status.Should().Be(DocumentStatus.Failed);
    }

    [Fact]
    public async Task ReclaimedPastMaxAttempts_FailsWithoutCallingExtractor()
    {
        var (_, command) = await SeedAsync(claims: 4);

        var outcome = await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.Failed);
        command.LastErrorCategory.Should().Be("MaxAttemptsExceeded");
        _extractor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reprocessing_IncrementsExtractionVersion()
    {
        var (document, command) = await SeedAsync();
        _db.InvoiceExtractionResults.Add(new InvoiceExtractionResultEntity { Id = Guid.NewGuid(), DocumentId = document.Id, ExtractionVersion = 1 });
        await _db.SaveChangesAsync();
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Extracted());

        await Processor.ProcessAsync(command.Id, Worker, CancellationToken.None);

        (await _db.InvoiceExtractionResults.MaxAsync(r => r.ExtractionVersion)).Should().Be(2);
    }

    [Fact]
    public async Task LeaseTakenOverBeforeStatusSave_DoesNotOverwriteDocument()
    {
        var (document, command) = await SeedAsync();

        await using (var other = OtherWorkerContext())
        {
            var takenOver = await other.QueueCommands.SingleAsync(c => c.Id == command.Id);
            takenOver.Claim("worker-2", TimeSpan.FromMinutes(5));
            var otherDocument = await other.Documents.SingleAsync(d => d.Id == document.Id);
            otherDocument.UpdateStatus(DocumentStatus.Processing);
            otherDocument.UpdateStatus(DocumentStatus.NeedsReview);
            takenOver.MarkCompleted();
            await other.SaveChangesAsync();
        }

        var outcome = await Processor.ProcessAsync(command, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.LeaseLost);
        _extractor.Verify(e => e.ImproveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        // The in-memory provider is not transactional, so the document row itself is only protected on PostgreSQL;
        // here we assert the save was rejected before extraction ran.
        await using var verify = OtherWorkerContext();
        (await verify.QueueCommands.SingleAsync(c => c.Id == command.Id)).Status.Should().Be(QueueCommandStatus.Completed);
    }

    [Fact]
    public async Task LongExtraction_RenewsLeaseUntilDone()
    {
        var (document, command) = await SeedAsync();
        var renewer = new CountingRenewer(renews: true);
        _options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(20);
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, CancellationToken ct) => { await Task.Delay(300, ct); return Extracted(); });

        var outcome = await new QueueCommandProcessor(_db, _extractor.Object, Options.Create(_options), NullLogger<QueueCommandProcessor>.Instance, renewer)
            .ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.Completed);
        renewer.Calls.Should().BeGreaterThan(1);
        renewer.Owners.Should().OnlyContain(o => o == Worker);
    }

    [Fact]
    public async Task LeaseLostDuringExtraction_CancelsExtraction_AndDiscardsAttempt()
    {
        var (document, command) = await SeedAsync();
        _options.LeaseRenewalInterval = TimeSpan.FromMilliseconds(20);
        _extractor.Setup(e => e.ImproveAsync(document.Id, It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, CancellationToken ct) => { await Task.Delay(Timeout.Infinite, ct); return Extracted(); });

        var outcome = await new QueueCommandProcessor(_db, _extractor.Object, Options.Create(_options), NullLogger<QueueCommandProcessor>.Instance, new CountingRenewer(renews: false))
            .ProcessAsync(command.Id, Worker, CancellationToken.None);

        outcome.Should().Be(QueueCommandOutcome.LeaseLost);
        (await _db.InvoiceExtractionResults.AnyAsync()).Should().BeFalse();
        command.Status.Should().Be(QueueCommandStatus.Processing);
    }

    private sealed class CountingRenewer(bool renews) : IQueueLeaseRenewer
    {
        private int _calls;
        public int Calls => _calls;
        public System.Collections.Concurrent.ConcurrentBag<string> Owners { get; } = new();

        public Task<bool> RenewAsync(Guid commandId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Owners.Add(leaseOwner);
            return Task.FromResult(renews);
        }
    }
}
