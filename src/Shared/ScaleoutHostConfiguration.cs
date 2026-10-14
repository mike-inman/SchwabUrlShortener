using System.Net;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace Shortener.Shared;

public static class ScaleoutHostConfiguration
{
    public static string AddConfiguredDataProtection(IServiceCollection services, IConfiguration configuration)
    {
        var mode = Required(configuration, "DataProtection:Mode");
        var applicationName = Required(configuration, "DataProtection:ApplicationName");
        var protection = services.AddDataProtection().SetApplicationName(applicationName);
        if (mode.Equals("Filesystem", StringComparison.OrdinalIgnoreCase))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspnet", "DataProtection-Keys")));
            return "Filesystem";
        }
        if (mode.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            var blob = HttpsUri(configuration, "DataProtection:BlobUri");
            var key = HttpsUri(configuration, "DataProtection:KeyVaultKeyId");
            var credential = new DefaultAzureCredential();
            protection.PersistKeysToAzureBlobStorage(blob, credential)
                .ProtectKeysWithAzureKeyVault(key, credential);
            return "AzureBlob";
        }
        throw new InvalidOperationException("DataProtection:Mode must be Filesystem or AzureBlob.");
    }

    public static string ConfigureTrustedProxy(IServiceCollection services, IConfiguration configuration)
    {
        var host = configuration["ForwardedHeaders:KnownProxyHost"];
        var networks = configuration["ForwardedHeaders:KnownNetworks"];
        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(networks))
            throw new InvalidOperationException("ForwardedHeaders:KnownProxyHost and ForwardedHeaders:KnownNetworks cannot both be set.");
        var limitText = configuration["ForwardedHeaders:ForwardLimit"];
        if (!int.TryParse(limitText ?? "1", out var limit) || limit < 1)
            throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be a positive integer.");
        if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(networks)) return "None";

        var addresses = Array.Empty<IPAddress>();
        var cidrs = new List<System.Net.IPNetwork>();
        if (!string.IsNullOrWhiteSpace(host))
        {
            addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0) throw new InvalidOperationException("ForwardedHeaders:KnownProxyHost could not be resolved.");
        }
        else
        {
            foreach (var text in networks!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!text.Contains('/') || !System.Net.IPNetwork.TryParse(text, out var network))
                    throw new InvalidOperationException("ForwardedHeaders:KnownNetworks must contain valid CIDRs.");
                cidrs.Add(network);
            }
            if (cidrs.Count == 0) throw new InvalidOperationException("ForwardedHeaders:KnownNetworks must contain valid CIDRs.");
        }
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = limit;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var address in addresses) options.KnownProxies.Add(address);
            foreach (var network in cidrs) options.KnownIPNetworks.Add(network);
            var publicUrl = configuration["PublicLinks:BaseUrl"];
            if (Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri))
            {
                options.AllowedHosts.Add(uri.Host);
                options.AllowedHosts.Add("www." + uri.Host);
            }
        });
        return addresses.Length > 0 ? "KnownProxyHost" : "KnownNetworks";
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! :
        throw new InvalidOperationException($"{key} must be configured.");

    private static Uri HttpsUri(IConfiguration configuration, string key)
    {
        if (!Uri.TryCreate(Required(configuration, key), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{key} must be an HTTPS URI without credentials or query parameters.");
        return uri;
    }
}
