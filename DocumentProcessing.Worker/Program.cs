using DocumentProcessing.Application.Services;
using DocumentProcessing.Worker;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Queue;
using DocumentProcessing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Configure database
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Register repositories
        services.AddScoped<IDocumentRepository, AppDbContext>();
        services.AddScoped<IQueueCommandRepository, QueueCommandRepository>();

        // Register other services
        services.AddScoped<DocumentService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<ValidationService>();

        // Register the hosted service (the worker)
        services.AddHostedService<DocumentProcessingWorker>();
    })
    .Build();

builder.Run();