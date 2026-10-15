using System.Net;
using System.Net.Sockets;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Security;

public sealed class WebhookUrlValidator : IWebhookUrlValidator
{
    private readonly WebhookOptions _options;

    public WebhookUrlValidator(IOptions<WebhookOptions> options)
    {
        _options = options.Value;
    }

    public void ValidateForConfiguration(string url)
    {
        var inspection = InspectOrThrow(url);
        EnsureSchemeAndHost(inspection);
        if (inspection.LiteralAddress is not null)
        {
            EnsureAddressAllowed(inspection.LiteralAddress);
        }
    }

    public async Task ValidateForDeliveryAsync(string url, CancellationToken cancellationToken)
    {
        var inspection = InspectOrThrow(url);
        EnsureSchemeAndHost(inspection);
        if (inspection.LiteralAddress is not null)
        {
            EnsureAddressAllowed(inspection.LiteralAddress);
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(inspection.Uri.IdnHost, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new RequestValidationException("Webhook URL host could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            throw new RequestValidationException("Webhook URL host could not be resolved.");
        }

        foreach (var address in addresses)
        {
            EnsureAddressAllowed(address);
        }
    }

    private WebhookUrlInspection InspectOrThrow(string url)
    {
        try
        {
            return WebhookUrlSafety.Inspect(url);
        }
        catch (ArgumentException ex)
        {
            throw new RequestValidationException(ex.Message);
        }
    }

    private void EnsureSchemeAndHost(WebhookUrlInspection inspection)
    {
        if (!inspection.IsHttps && !_options.AllowInsecureHttp)
        {
            throw new RequestValidationException("Webhook URL must use https.");
        }

        if (WebhookUrlSafety.IsLocalhostHost(inspection.Uri.Host) && !_options.AllowLoopback)
        {
            throw new RequestValidationException("Webhook URL host is not allowed.");
        }
    }

    private void EnsureAddressAllowed(IPAddress address)
    {
        if (WebhookUrlSafety.IsLoopbackAddress(address))
        {
            if (!_options.AllowLoopback)
            {
                throw new RequestValidationException("Webhook URL must not target loopback addresses.");
            }

            return;
        }

        if (WebhookUrlSafety.IsBlockedAddress(address))
        {
            throw new RequestValidationException("Webhook URL must not target private or link-local addresses.");
        }
    }
}
