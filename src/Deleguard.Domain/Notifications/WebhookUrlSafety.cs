using System.Net;
using System.Net.Sockets;

namespace Deleguard.Domain.Notifications;

public sealed record WebhookUrlInspection(Uri Uri, IPAddress? LiteralAddress, bool IsHttps);

public static class WebhookUrlSafety
{
    public static WebhookUrlInspection Inspect(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048)
        {
            throw new ArgumentException("Webhook URL is required and must be under 2048 characters.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Webhook URL is not a valid absolute URI.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("Webhook URL must not include credentials.");
        }

        if (uri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Webhook URL must use https.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Webhook URL host is required.");
        }

        IPAddress? literal = null;
        if (IPAddress.TryParse(uri.Host.Trim('[').Trim(']'), out var parsed))
        {
            literal = parsed;
        }

        return new WebhookUrlInspection(uri, literal, uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool IsLocalhostHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "localhost.localdomain", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "metadata.internal", StringComparison.OrdinalIgnoreCase);

    public static bool IsBlockedAddress(IPAddress address)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None))
        {
            return true;
        }

        if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast)
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc || bytes[0] == 0xff;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        var v4 = ip.GetAddressBytes();
        return v4[0] == 0
               || v4[0] == 10
               || v4[0] == 127
               || (v4[0] == 169 && v4[1] == 254)
               || (v4[0] == 172 && v4[1] >= 16 && v4[1] <= 31)
               || (v4[0] == 192 && v4[1] == 168)
               || (v4[0] == 100 && v4[1] >= 64 && v4[1] <= 127)
               || v4[0] >= 224;
    }

    public static bool IsLoopbackAddress(IPAddress address)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(ip);
    }
}
