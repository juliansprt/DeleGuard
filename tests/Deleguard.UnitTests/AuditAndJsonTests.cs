using Deleguard.Domain.Common;

namespace Deleguard.UnitTests;

public sealed class AuditAndJsonTests
{
    [Fact]
    public void Hash_chain_is_deterministic_and_chained()
    {
        var h1 = AuditHasher.Compute(null, "payload-a");
        var h2 = AuditHasher.Compute(h1, "payload-b");
        Assert.Equal(h1, AuditHasher.Compute(AuditHasher.Genesis, "payload-a"));
        Assert.NotEqual(h1, h2);
        Assert.Equal(64, h1.Length);
    }

    [Fact]
    public void Canonical_json_sorts_keys()
    {
        var left = CanonicalJson.HashHex(System.Text.Json.Nodes.JsonNode.Parse("""{"b":1,"a":2}"""));
        var right = CanonicalJson.HashHex(System.Text.Json.Nodes.JsonNode.Parse("""{"a":2,"b":1}"""));
        Assert.Equal(left, right);
    }

    [Fact]
    public void Duration_parser_reads_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), DurationParser.Parse("15m"));
        Assert.Equal("15m", DurationParser.Format(TimeSpan.FromMinutes(15)));
    }
}
