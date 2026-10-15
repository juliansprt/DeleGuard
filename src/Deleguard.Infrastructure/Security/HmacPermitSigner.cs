using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Evidence;
using Microsoft.Extensions.Options;

namespace Deleguard.Infrastructure.Security;

public sealed class PermitSigningOptions
{
    public string SigningKey { get; set; } = "change-me-permit-signing-key-32b-min";
    public string Issuer { get; set; } = "deleguard";
}

public sealed class HmacPermitSigner : IPermitSigner
{
    private readonly PermitSigningOptions _options;

    public HmacPermitSigner(IOptions<PermitSigningOptions> options)
    {
        _options = options.Value;
    }

    public string Sign(Permit permit, AgentPrincipal agent, string? actorId)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["iss"] = _options.Issuer,
            ["jti"] = permit.Id,
            ["dec"] = permit.DecisionId,
            ["org"] = permit.OrganizationId,
            ["app"] = agent.ApplicationId,
            ["env"] = agent.EnvironmentId,
            ["agent_id"] = agent.AgentId,
            ["operation_id"] = permit.OperationId,
            ["operation_hash"] = permit.OperationHash,
            ["agt"] = agent.AgentExternalId,
            ["actor"] = actorId,
            ["actn"] = permit.Action,
            ["rtype"] = permit.ResourceType,
            ["rid"] = permit.ResourceId,
            ["rhash"] = permit.ResourceSnapshotHash,
            ["pol"] = permit.PolicyVersion?.ToString(),
            ["iat"] = permit.IssuedAt.ToUnixTimeSeconds().ToString(),
            ["exp"] = permit.ExpiresAt.ToUnixTimeSeconds().ToString()
        });
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var sig = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.SigningKey), Encoding.UTF8.GetBytes(payloadB64));
        return $"{payloadB64}.{Convert.ToBase64String(sig)}";
    }
}
