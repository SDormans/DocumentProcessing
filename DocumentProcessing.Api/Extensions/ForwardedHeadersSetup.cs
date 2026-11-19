using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace DocumentProcessing.Api.Extensions;

/// <summary>
/// Honours X-Forwarded-For/-Proto from trusted reverse proxies only (the web container's Nginx, a cloud ingress),
/// so rate limiting sees the client IP and HTTPS detection works behind TLS termination.
/// Trust is configured with <c>ForwardedHeaders:KnownProxies</c> (IP addresses) and
/// <c>ForwardedHeaders:KnownNetworks</c> (CIDR ranges); without either, only loopback proxies are trusted.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string SectionName = "ForwardedHeaders";

    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options => Configure(options, configuration));
        return services;
    }

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = configuration.GetValue<int?>($"{SectionName}:ForwardLimit") ?? 1;

        foreach (var proxy in configuration.GetSection($"{SectionName}:KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new InvalidOperationException($"{SectionName}:KnownProxies entry '{proxy}' is not an IP address.");
            options.KnownProxies.Add(address);
        }

        foreach (var network in configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
                throw new InvalidOperationException($"{SectionName}:KnownNetworks entry '{network}' is not a CIDR range.");
            options.KnownIPNetworks.Add(parsed);
        }
    }
}
