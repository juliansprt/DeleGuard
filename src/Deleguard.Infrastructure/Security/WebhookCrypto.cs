using System.Security.Cryptography;
using System.Text;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Security;

public sealed class WebhookOptions
{
    public string ProtectionKey { get; set; } = "deleguard-dev-webhook-protect-key-32";
    public bool AllowLoopback { get; set; }
    public bool AllowInsecureHttp { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 15;
}

public sealed class StandardWebhookSigner : IWebhookSigner
{
    public string Sign(string messageId, long unixTimestampSeconds, string payload, IReadOnlyList<string> plaintextSecrets) =>
        StandardWebhookSignature.Sign(messageId, unixTimestampSeconds, payload, plaintextSecrets);
}

public sealed class AesWebhookSecretProtector : IWebhookSecretProtector
{
    private readonly byte[] _key;

    public AesWebhookSecretProtector(IOptions<WebhookOptions> options)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ProtectionKey));
    }

    public string CreatePlaintextSecret() => StandardWebhookSignature.CreateSecret();

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, bytes, ciphertext, tag);
        var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, nonce.Length);
        ciphertext.CopyTo(packed, nonce.Length + tag.Length);
        return "v1." + Convert.ToBase64String(packed);
    }

    public string Unprotect(string ciphertext)
    {
        if (!ciphertext.StartsWith("v1.", StringComparison.Ordinal))
        {
            throw new CryptographicException("Unsupported webhook secret payload.");
        }

        var packed = Convert.FromBase64String(ciphertext[3..]);
        if (packed.Length < 28)
        {
            throw new CryptographicException("Webhook secret payload is truncated.");
        }

        var nonce = packed[..12];
        var tag = packed[12..28];
        var encrypted = packed[28..];
        var plaintext = new byte[encrypted.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, encrypted, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
