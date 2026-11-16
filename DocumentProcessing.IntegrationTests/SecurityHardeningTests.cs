using DocumentProcessing.Api.Controllers;
using DocumentProcessing.Api.Extensions;
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

    [Theory]
    [InlineData(nameof(AuthController.Login))]
    [InlineData(nameof(AuthController.Logout))]
    public void Only_login_and_logout_skip_antiforgery(string action)
    {
        typeof(AuthController).GetMethod(action)!
            .GetCustomAttributes(typeof(IgnoreAntiforgeryTokenAttribute), inherit: true)
            .Should().ContainSingle();
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
