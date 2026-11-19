using System.Net;
using DocumentProcessing.Api.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

public class ForwardedHeadersSetupTests
{
    private static ForwardedHeadersOptions Configure(Dictionary<string, string?> values)
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSetup.Configure(options, new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return options;
    }

    [Fact]
    public void Defaults_TrustOnlyLoopback()
    {
        var options = Configure([]);

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.Should().Be(1);
        options.KnownProxies.Should().OnlyContain(ip => IPAddress.IsLoopback(ip));
    }

    [Fact]
    public void ConfiguredProxiesAndNetworks_AreTrusted()
    {
        var options = Configure(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
        });

        options.KnownProxies.Should().Contain(IPAddress.Parse("10.1.2.3"));
        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    public void InvalidEntries_FailFast(string key, string value)
    {
        var act = () => Configure(new() { [key] = value });

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{value}*");
    }
}
