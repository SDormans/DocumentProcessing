using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.Invoice;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Infrastructure.Data.Repositories;

public class InvoiceResultRepository : IInvoiceResultRepository
{
    private readonly AppDbContext _dbContext;

    public InvoiceResultRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<InvoiceExtractionResultEntity?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.InvoiceExtractionResults
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.ExtractionVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
