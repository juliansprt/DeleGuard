using Deleguard.Desktop.Core;
using System.Security.Cryptography;

namespace Deleguard.Desktop.Tests;

public class SessionAuthenticationTests
{
    [Fact]
    public void PacketAuthenticationBindsProjectNonceDirectionAndFullResponse()
    {
        var capability=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        const string body="{\"Status\":200,\"Body\":{\"decision\":\"DENY\"}}";
        var signature=SessionAuthentication.Sign(capability,"project-a",nonce,body,"response");
        Assert.True(SessionAuthentication.Verify(capability,"project-a",nonce,body,"response",signature));
        Assert.False(SessionAuthentication.Verify(capability,"project-b",nonce,body,"response",signature));
        Assert.False(SessionAuthentication.Verify(capability,"project-a",new string('A',64),body,"response",signature));
        Assert.False(SessionAuthentication.Verify(capability,"project-a",nonce,body,"request",signature));
        Assert.False(SessionAuthentication.Verify(capability,"project-a",nonce,body.Replace("DENY","ALLOW"),"response",signature));
        Assert.False(SessionAuthentication.Verify(new string('0',64),"project-a",nonce,body,"response",signature));
    }
    [Theory]
    [InlineData("")][InlineData("invalid")][InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void MalformedSignatureIsRejected(string value)=>Assert.False(SessionAuthentication.Verify(new string('0',64),"project",new string('A',64),"{}","response",value));
}
