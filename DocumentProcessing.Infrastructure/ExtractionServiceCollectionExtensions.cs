using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Ai;
using DocumentProcessing.Infrastructure.Security;
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
        services.AddScoped<IDocumentTextExtractor, PdfPigTextExtractor>();
        services.AddAiProvider(configuration);
        services.AddMalwareScanning(configuration);
        services.AddScoped<IInvoiceValidationRule>(_ => new MissingRequiredFieldsValidationRule());
        services.AddScoped<IInvoiceValidationRule, InvalidDateValidationRule>();
        services.AddScoped<IInvoiceValidationRule, InvalidNumericValuesValidationRule>();
        services.AddScoped<IInvoiceValidationRule>(_ => new InconsistentInvoiceAmountsValidationRule());
        services.AddScoped<IInvoiceValidationRule, InconsistentInvoiceLinesValidationRule>();
        services.AddScoped<ValidationService>();
        services.AddScoped<IInvoiceExtractor, InvoiceExtractor>();
        return services;
    }
}
