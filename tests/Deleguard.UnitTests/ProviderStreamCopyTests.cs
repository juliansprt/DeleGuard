using System.Text;
using Deleguard.AiGateway;
namespace Deleguard.UnitTests;
public sealed class ProviderStreamCopyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Copies_exact_bytes_and_requires_terminal_marker(bool terminal)
    {
        var text = "data: {\"choices\":[]}\r\n\r\ndata: {\"usage\":{\"prompt_tokens\":13,\"completion_tokens\":7}}\n\n" + (terminal ? "data: [DONE]\n\n" : "");
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(text)); await using var output = new MemoryStream();
        var result = await ProviderStreamCopy.CopyAsync(input, output, default);
        Assert.Equal(text, Encoding.UTF8.GetString(output.ToArray())); Assert.Equal(terminal, result.Complete);
        Assert.Equal(13, result.InputTokens); Assert.Equal(7, result.OutputTokens);
    }
    [Fact]
    public async Task Completion_without_usage_does_not_invent_zero_cost()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
        var receipt = await ProviderStreamCopy.CopyAsync(input, Stream.Null, default);
        Assert.True(receipt.Complete); Assert.Null(receipt.InputTokens); Assert.Null(receipt.OutputTokens);
    }
}
