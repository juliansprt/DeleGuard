using Deleguard.Application.Authorization;

namespace Deleguard.UnitTests;

public sealed class GuardConfigHandlerTests
{
    [Fact]
    public void Compose_and_parse_guard_tool_names()
    {
        var name = GuardToolNames.Compose("invoice-demo", "cancel_invoice");
        Assert.Equal("invoice-demo__cancel_invoice", name);
        Assert.True(GuardToolNames.TryParse(name, out var server, out var tool));
        Assert.Equal("invoice-demo", server);
        Assert.Equal("cancel_invoice", tool);
        Assert.Contains("approve", GuardToolNames.Reserved);
        Assert.Contains("deny", GuardToolNames.Reserved);
    }
}
