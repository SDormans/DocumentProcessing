using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace DocumentProcessing.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = new ErrorResponse
        {
            TraceId = context.TraceIdentifier,
            Message = "Er is een fout opgetreden bij het verwerken van de verzoek."
        };

        HttpStatusCode statusCode;

        switch (exception)
        {
            case ValidationException validationEx:
            {
                statusCode = HttpStatusCode.BadRequest;
                response.Message = "Validatiefout.";
                response.Errors = validationEx.Errors;

                var errorList = validationEx.Errors.ToList();
                if (errorList.Any(e => e.Contains("te groot", StringComparison.OrdinalIgnoreCase)))
                {
                    statusCode = HttpStatusCode.RequestEntityTooLarge; // 413
                    response.Message = "Bestand is te groot.";
                }
                else if (errorList.Any(e => e.Contains("PDF", StringComparison.OrdinalIgnoreCase)))
                {
                    statusCode = HttpStatusCode.UnsupportedMediaType; // 415
                    response.Message = "Niet-ondersteund bestandstype.";
                }
                break;
            }

            case NotFoundException notFoundEx:
                statusCode = HttpStatusCode.NotFound;
                response.Message = notFoundEx.Message;
                break;

            case StorageException:
                statusCode = HttpStatusCode.InternalServerError;
                response.Message = "Document storage failed.";
                break;

            case DocumentProcessingException domainEx:
                statusCode = HttpStatusCode.BadRequest;
                response.Message = domainEx.Message;
                break;

            default:
                statusCode = HttpStatusCode.InternalServerError;
                break;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}