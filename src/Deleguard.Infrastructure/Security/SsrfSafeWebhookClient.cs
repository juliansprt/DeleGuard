using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Security;

public sealed class SsrfSafeWebhookClient : IWebhookHttpClient
{
    private readonly HttpClient _http;
    private readonly IWebhookUrlValidator _validator;

    public SsrfSafeWebhookClient(HttpClient http, IWebhookUrlValidator validator)
    {
        _http = http;
        _validator = validator;
    }

    public static SocketsHttpHandler CreateHandler(WebhookOptions options) =>
        new()
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(2, options.RequestTimeoutSeconds)),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var chosen = addresses.FirstOrDefault(address => IsAllowed(address, options));
                if (chosen is null)
                {
                    throw new HttpRequestException("Webhook destination resolved to a blocked address.");
                }

                var socket = new Socket(chosen.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(chosen, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

    public async Task<WebhookHttpResult> PostAsync(
        string url,
        string jsonBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateForDeliveryAsync(url, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("User-Agent", "Deleguard-Webhook/1.0");
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var timer = Stopwatch.StartNew();
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[2048];
            var n = 0;
            while (n < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(n, buffer.Length - n), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                n += read;
            }

            var body = n == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, n);
            return new WebhookHttpResult((int)response.StatusCode, body, null, (int)timer.ElapsedMilliseconds, false);
        }
        catch (TaskCanceledException)
        {
            return new WebhookHttpResult(null, null, "timeout", (int)timer.ElapsedMilliseconds, true);
        }
        catch (HttpRequestException ex)
        {
            return new WebhookHttpResult(null, null, Truncate(ex.Message), (int)timer.ElapsedMilliseconds, false);
        }
    }

    private static bool IsAllowed(IPAddress address, WebhookOptions options)
    {
        if (WebhookUrlSafety.IsLoopbackAddress(address))
        {
            return options.AllowLoopback;
        }

        return !WebhookUrlSafety.IsBlockedAddress(address);
    }

    private static string Truncate(string value) => value.Length <= 512 ? value : value[..512];
}
