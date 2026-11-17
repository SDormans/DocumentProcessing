using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace DocumentProcessing.Api.Extensions;

public static class ForwardedHeadersSetup
{
    private const string BuiltInSwitch = "ASPNETCORE_FORWARDEDHEADERS_ENABLED";

    /// <summary>
    /// Honours <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> only from the proxies in
    /// <c>ForwardedHeaders:KnownProxies</c> (IPs) and <c>ForwardedHeaders:KnownNetworks</c> (CIDRs).
    /// With neither set, only loopback is trusted. This also applies when the built-in
    /// <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> switch is used, which would otherwise trust any sender.
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));

            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
            {
                options.KnownProxies.Add(IPAddress.Loopback);
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
            }
        });
        return services;
    }

    public static WebApplication UseTrustedForwardedHeaders(this WebApplication app)
    {
        // The built-in switch already adds the middleware; adding it twice would consume two hops.
        if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled") && !app.Configuration.GetValue<bool>(BuiltInSwitch))
            app.UseForwardedHeaders();
        return app;
    }
}
