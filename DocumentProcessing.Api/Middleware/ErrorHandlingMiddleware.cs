using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Exceptions;
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
            Message = "An error occurred while processing the request."
        };

        HttpStatusCode statusCode;

        switch (exception)
        {
            case ValidationException validationEx:
            {
                response.Errors = validationEx.Errors;
                (statusCode, response.Message) = validationEx.Kind switch
                {
                    ValidationFailureKind.TooLarge => (HttpStatusCode.RequestEntityTooLarge, "File is too large."),
                    ValidationFailureKind.UnsupportedMediaType => (HttpStatusCode.UnsupportedMediaType, "Unsupported file type."),
                    _ => (HttpStatusCode.BadRequest, "Validation failed.")
                };
                break;
            }

            case ConcurrencyConflictException:
                statusCode = HttpStatusCode.Conflict;
                response.Message = "The resource was changed by another request. Reload and try again.";
                break;

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