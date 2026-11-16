using DocumentProcessing.Application.Services.Invoice;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// Toegang tot opgeslagen factuur-extractieresultaten.
/// Geïmplementeerd door InvoiceResultRepository (Infrastructure).
/// </summary>
public interface IInvoiceResultRepository
{
    Task<InvoiceExtractionResultEntity?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
