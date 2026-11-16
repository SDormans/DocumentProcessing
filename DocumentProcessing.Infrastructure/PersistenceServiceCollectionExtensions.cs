using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Data.Repositories;
using DocumentProcessing.Infrastructure.Queue;
using DocumentProcessing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Infrastructure;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AppDbContext"/>, the repositories, the queue and file storage. Shared by the API and the worker.
    /// Repositories share the scoped context, so one <c>SaveChangesAsync</c> commits everything tracked in a request.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentReviewRepository, DocumentReviewRepository>();
        services.AddScoped<IInvoiceResultRepository, InvoiceResultRepository>();
        services.AddScoped<IQueueCommandRepository, QueueCommandRepository>();
        services.AddScoped<IProcessingQueue, DbProcessingQueue>();
        services.AddFileStorage(configuration);
        return services;
    }
}
