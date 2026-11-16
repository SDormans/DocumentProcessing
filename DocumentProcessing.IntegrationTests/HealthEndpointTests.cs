using DocumentProcessing.Api.Extensions;
using DocumentProcessing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

public class HealthEndpointTests
{
    private static RouteEndpoint[] MapEndpoints(out WebApplication app)
    {
        var builder = WebApplication.CreateBuilder();
        // Never opened: the live probe must not touch the database.
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=unreachable.invalid"));
        app = builder.Build();
        app.MapHealthEndpoints();
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToArray();
    }

    [Fact]
    public void Maps_anonymous_live_and_ready_endpoints()
    {
        var endpoints = MapEndpoints(out var app);
        using var _ = app as IDisposable;

        endpoints.Select(e => e.RoutePattern.RawText).Should().BeEquivalentTo("/health/live", "/health/ready");
        endpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null);
    }

    [Fact]
    public async Task Live_returns_ok_without_resolving_the_database()
    {
        var endpoints = MapEndpoints(out var app);
        await using var __ = app;
        var live = endpoints.Single(e => e.RoutePattern.RawText == "/health/live");

        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Response.Body = new MemoryStream();

        await live.RequestDelegate!(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        (await new StreamReader(context.Response.Body).ReadToEndAsync()).Should().Contain("\"live\"");
    }
}
