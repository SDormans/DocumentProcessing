using DocumentProcessing.Api.Controllers;
using DocumentProcessing.Api.Extensions;
using DocumentProcessing.Api.Middleware;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

public class SecurityHardeningTests
{
    private readonly Mock<IAntiforgery> _antiforgery = new();

    [Fact]
    public void Upload_uses_the_upload_rate_limit()
    {
        typeof(DocumentsController).GetMethod(nameof(DocumentsController.UploadDocument))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .Should().ContainSingle(a => a.PolicyName == RateLimitingSetup.UploadPolicy);
    }

    [Fact]
    public void Only_login_skips_antiforgery()
    {
        typeof(AuthController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), inherit: true).Length > 0)
            .Select(m => m.Name)
            .Should().Equal(nameof(AuthController.Login));
    }

    [Theory]
    [InlineData(null, "Development", CookieSecurePolicy.None)]
    [InlineData(null, "Production", CookieSecurePolicy.Always)]
    [InlineData("SameAsRequest", "Production", CookieSecurePolicy.SameAsRequest)]
    [InlineData("always", "Development", CookieSecurePolicy.Always)]
    public void Cookie_secure_policy_defaults_by_environment_and_can_be_configured(string? configured, string environment, CookieSecurePolicy expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:CookieSecurePolicy"] = configured })
            .Build();
        var host = Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environment);

        AuthSetup.GetCookieSecurePolicy(configuration, host).Should().Be(expected);
    }

    [Fact]
    public void Invalid_cookie_secure_policy_fails_startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:CookieSecurePolicy"] = "Sometimes" })
            .Build();

        FluentActions.Invoking(() => AuthSetup.GetCookieSecurePolicy(configuration, Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production")))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ValidationFailureKind.TooLarge, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(ValidationFailureKind.UnsupportedMediaType, StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(ValidationFailureKind.Invalid, StatusCodes.Status400BadRequest)]
    public async Task Validation_failures_map_to_status_codes_by_kind(ValidationFailureKind kind, int expected)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new ValidationException("Upload validation failed.", ["File is too large. Maximum size is 10 MB."], kind),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Concurrency_conflicts_map_to_409()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new ConcurrencyConflictException("Changed.", new Exception()),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void Register_requires_antiforgery()
    {
        typeof(AuthController).GetMethod(nameof(AuthController.Register))!
            .GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), inherit: true)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Safe_methods_are_not_validated(string method)
    {
        var context = CreateContext(method);

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        _antiforgery.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Unsafe_methods_without_a_valid_token_get_400(string method)
    {
        _antiforgery.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new AntiforgeryValidationException("missing"));
        var context = CreateContext(method);

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Unsafe_methods_with_a_valid_token_pass()
    {
        _antiforgery.Setup(a => a.ValidateRequestAsync(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);
        var context = CreateContext("POST");

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        _antiforgery.Verify(a => a.ValidateRequestAsync(context.HttpContext), Times.Once);
    }

    [Fact]
    public async Task Actions_marked_IgnoreAntiforgeryToken_are_skipped()
    {
        var context = CreateContext("POST", new IgnoreAntiforgeryTokenAttribute());

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        _antiforgery.Verify(a => a.ValidateRequestAsync(It.IsAny<HttpContext>()), Times.Never);
    }

    private ValidateAntiforgeryOnUnsafeMethodsFilter CreateFilter() =>
        new(_antiforgery.Object, NullLogger<ValidateAntiforgeryOnUnsafeMethodsFilter>.Instance);

    private static AuthorizationFilterContext CreateContext(string method, params object[] endpointMetadata)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        var actionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() };
        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), actionDescriptor),
            new List<IFilterMetadata>());
    }
}
