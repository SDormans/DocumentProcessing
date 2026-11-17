using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.TextExtraction;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Identity;
using DocumentProcessing.Infrastructure.Ocr;
using DocumentProcessing.Infrastructure.TextExtraction;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;

namespace DocumentProcessing.Api.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddUserStore(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddTokenProvider<AuthenticatorTokenProvider<ApplicationUser>>(TokenOptions.DefaultAuthenticatorProvider);
        services.AddScoped<IUserService, IdentityUserService>();
        return services;
    }

    public static IServiceCollection AddDocumentProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentServiceOptions>(configuration.GetSection("DocumentService"));
        services.Configure<TextExtractionOptions>(configuration.GetSection("TextExtraction"));

        services.Configure<DocumentAccessOptions>(configuration.GetSection(DocumentAccessOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentReviewService, DocumentReviewService>();

        services.AddScoped<ITextExtractionDetector, TextExtractionDetector>();
        services.AddScoped<IOcrProvider, TesseractOcrProvider>();
        services.AddScoped<IDocumentTextExtractionService, DocumentTextExtractionService>();

        services.AddInvoiceExtraction(configuration);
        return services;
    }

    public static IServiceCollection AddUploadLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var maxFileSizeBytes = configuration.GetValue<long>("DocumentService:MaxFileSizeBytes", 10 * 1024 * 1024);
        if (maxFileSizeBytes <= 0)
            throw new InvalidOperationException("DocumentService:MaxFileSizeBytes must be greater than zero.");

        services.Configure<FormOptions>(options =>
            options.MultipartBodyLengthLimit = checked(maxFileSizeBytes + 128 * 1024));
        return services;
    }
}
