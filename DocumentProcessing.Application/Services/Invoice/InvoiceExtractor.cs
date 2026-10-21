using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Application.Services.Invoice;

public class InvoiceExtractor : IInvoiceExtractor
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly ITextExtractionDetector _textDetector;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<InvoiceExtractor> _logger;
    private readonly ValidationService _validationService;

    public InvoiceExtractor(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        IDocumentTextExtractor textExtractor,
        ITextExtractionDetector textDetector,
        IAiProvider aiProvider,
        ILogger<InvoiceExtractor> logger,
        ValidationService validationService)
    {
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _textExtractor = textExtractor;
        _textDetector = textDetector;
        _aiProvider = aiProvider;
        _logger = logger;
        _validationService = validationService;
    }

    public async Task<InvoiceExtractionResult> ExtractAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<InvoiceExtractionResult> ImproveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
