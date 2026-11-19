using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Ai;
using DocumentProcessing.Infrastructure.Ocr;
using DocumentProcessing.Infrastructure.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Infrastructure;

public static class ExtractionServiceCollectionExtensions
{
    /// <summary>
    /// Registers PDF text extraction, the configured AI provider, validation rules and
    /// <see cref="IInvoiceExtractor"/>. Shared by the API and the worker.
    /// </summary>
    public static IServiceCollection AddInvoiceExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.AddSingleton<IOcrProvider, TesseractCliOcrProvider>();
        services.AddSingleton<IPdfPageRenderer, PdftoppmPageRenderer>();
        services.AddScoped<PdfPigTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, HybridPdfTextExtractor>();
        services.AddAiProvider(configuration);
        services.AddScoped<IInvoiceValidationRule>(_ => new MissingRequiredFieldsValidationRule());
        services.AddScoped<IInvoiceValidationRule, InvalidDateValidationRule>();
        services.AddScoped<IInvoiceValidationRule, InvalidNumericValuesValidationRule>();
        services.AddScoped<IInvoiceValidationRule>(_ => new InconsistentInvoiceAmountsValidationRule());
        services.AddScoped<IInvoiceValidationRule, InconsistentInvoiceLinesValidationRule>();
        services.AddScoped<ValidationService>();
        services.Configure<InvoiceExtractionOptions>(configuration.GetSection(InvoiceExtractionOptions.SectionName));
        services.AddScoped<IInvoiceExtractor, InvoiceExtractor>();
        return services;
    }
}
