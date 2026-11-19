using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Data.Repositories;
using DocumentProcessing.Infrastructure.Queue;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentProcessing.UnitTests.Repositories;

public class RepositoryTests : IDisposable
{
    private readonly AppDbContext _dbContext = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _dbContext.Dispose();

    private static Document NewDocument(DateTime createdAtUtc) =>
        new(Guid.NewGuid(), "invoice.pdf", "application/pdf", 10, "hash", "key", DocumentStatus.Uploaded, createdAtUtc);

    [Fact]
    public async Task DocumentRepository_GetPageAsync_ReturnsNewestFirst_AndPages()
    {
        var repository = new DocumentRepository(_dbContext);
        var older = NewDocument(DateTime.UtcNow.AddHours(-1));
        var newer = NewDocument(DateTime.UtcNow);
        await repository.AddAsync(older);
        await repository.AddAsync(newer);
        await repository.SaveChangesAsync();

        var all = await repository.GetPageAsync(null, 0, 50);
        var second = await repository.GetPageAsync(null, 1, 1);

        all.Items.Select(d => d.Id).Should().Equal(newer.Id, older.Id);
        all.TotalCount.Should().Be(2);
        second.Items.Select(d => d.Id).Should().Equal(older.Id);
        second.TotalCount.Should().Be(2);
        (await repository.GetPageAsync("someone-else", 0, 50)).TotalCount.Should().Be(0);
        (await repository.GetByIdAsync(older.Id)).Should().BeSameAs(older);
        (await repository.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task DocumentRepository_SaveChanges_CommitsQueueCommandFromSharedContext()
    {
        var repository = new DocumentRepository(_dbContext);
        var queue = new DbProcessingQueue(_dbContext);
        var document = NewDocument(DateTime.UtcNow);

        await repository.AddAsync(document);
        await queue.EnqueueAsync(document.Id);
        await repository.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Documents.CountAsync()).Should().Be(1);
        (await _dbContext.QueueCommands.SingleAsync()).DocumentId.Should().Be(document.Id);
    }

    [Fact]
    public async Task DocumentReviewRepository_FindsReviewByIdAndDocumentId()
    {
        var repository = new DocumentReviewRepository(_dbContext);
        var documentId = Guid.NewGuid();
        var review = new DocumentReview(Guid.NewGuid(), documentId, "{}");
        await repository.AddAsync(review);
        await repository.SaveChangesAsync();

        (await repository.GetByDocumentIdAsync(documentId)).Should().BeSameAs(review);
        (await repository.GetByIdAsync(review.Id)).Should().BeSameAs(review);
        (await repository.GetByDocumentIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task InvoiceResultRepository_ReturnsLatestExtractionVersion()
    {
        var documentId = Guid.NewGuid();
        _dbContext.InvoiceExtractionResults.AddRange(
            new InvoiceExtractionResultEntity { Id = Guid.NewGuid(), DocumentId = documentId, ExtractionVersion = 1 },
            new InvoiceExtractionResultEntity { Id = Guid.NewGuid(), DocumentId = documentId, ExtractionVersion = 3 },
            new InvoiceExtractionResultEntity { Id = Guid.NewGuid(), DocumentId = documentId, ExtractionVersion = 2 },
            new InvoiceExtractionResultEntity { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ExtractionVersion = 9 });
        await _dbContext.SaveChangesAsync();

        var result = await new InvoiceResultRepository(_dbContext).GetByDocumentIdAsync(documentId);

        result!.ExtractionVersion.Should().Be(3);
    }
}
