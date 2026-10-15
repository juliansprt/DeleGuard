using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deleguard.Desktop.Core;

namespace Deleguard.Desktop.Tests;

public sealed class ProjectVaultTests
{
    [Fact] public void Portable_vault_roundtrip_does_not_contain_secret_or_password()
    {
        var vault=ProjectVault.Seal("dgk_private_key","project-a","long project password",false);
        Assert.DoesNotContain("dgk_private_key",vault);Assert.DoesNotContain("long project password",vault);
        Assert.Equal("dgk_private_key",Encoding.UTF8.GetString(ProjectVault.Open(vault,"project-a","long project password")));
    }
    [Theory][InlineData("project-b","long project password")][InlineData("project-a","wrong password")][InlineData("project-a",null)]
    public void Other_identity_wrong_password_and_locked_session_cannot_open(string identity,string? password)
    {
        var vault=ProjectVault.Seal("dgk_private_key","project-a","long project password",false);
        Assert.Equal("vault_locked",Assert.Throws<SetupException>(()=>ProjectVault.Open(vault,identity,password)).Code);
    }
    [Fact] public void Ciphertext_and_parameters_are_authenticated_or_rejected()
    {
        var vault=JsonSerializer.Deserialize<VaultEnvelope>(ProjectVault.Seal("dgk_private_key","project-a","long project password",false))!;
        var data=Convert.FromBase64String(vault.Ciphertext);data[0]^=1;
        Assert.Throws<SetupException>(()=>ProjectVault.Open(JsonSerializer.Serialize(vault with{Ciphertext=Convert.ToBase64String(data)}),"project-a","long project password"));
        Assert.Throws<SetupException>(()=>ProjectVault.Open(JsonSerializer.Serialize(vault with{Iterations=int.MaxValue}),"project-a","long project password"));
    }
    [Fact] public void Each_seal_has_independent_salt_and_nonce()
    {
        var a=JsonSerializer.Deserialize<VaultEnvelope>(ProjectVault.Seal("dgk_private_key","project-a","long project password",false))!;
        var b=JsonSerializer.Deserialize<VaultEnvelope>(ProjectVault.Seal("dgk_private_key","project-a","long project password",false))!;
        Assert.NotEqual(a.Salt,b.Salt);Assert.NotEqual(a.Nonce,b.Nonce);Assert.NotEqual(a.Ciphertext,b.Ciphertext);
    }
    [Fact] public void Windows_vault_is_local_and_bound_to_identity()
    {
        if(!OperatingSystem.IsWindows())return;
        var vault=ProjectVault.Seal("dgk_private_key","project-a",null);
        Assert.Equal("dgk_private_key",Encoding.UTF8.GetString(ProjectVault.Open(vault,"project-a",null)));
        Assert.Throws<SetupException>(()=>ProjectVault.Open(vault,"project-b",null));
    }
    [Theory][InlineData("https://example.com")][InlineData("http://localhost:5086")][InlineData("http://127.0.0.1:5086")]
    public void Valid_origins(string url)=>Assert.NotNull(ProjectConnection.ValidateUrl(url));
    [Theory][InlineData("http://example.com")][InlineData("https://user:pass@example.com")][InlineData("https://example.com/?key=secret")][InlineData("https://example.com/path")]
    public void Unsafe_origins_are_rejected(string url)=>Assert.Throws<SetupException>(()=>ProjectConnection.ValidateUrl(url));
    [Fact] public void Bridge_cannot_proxy_arbitrary_endpoints()
    {
        var scope=JsonSerializer.SerializeToElement(new{organization_id="org",application_id="app",agent_id="agent"});
        var c=new ProjectConnection(Path.GetFullPath("."),"cursor","https://example.com","project","agent","development","",scope);
        Assert.True(ProjectApi.RouteAllowed(c,new("/v1/ide/config/project")));
        Assert.False(ProjectApi.RouteAllowed(c,new("/v1/ide/config/another-project")));
        Assert.False(ProjectApi.RouteAllowed(c,new("https://evil.example")));
        Assert.False(ProjectApi.RouteAllowed(c,new("/v1/agents")));
        Assert.False(ProjectApi.RouteAllowed(c,new("/v1/decisions/../../agents")));
        Assert.False(ProjectApi.RouteAllowed(c,new("/v1/authorize")));
        Assert.True(ProjectApi.RouteAllowed(c,new("/v1/authorize",JsonSerializer.SerializeToElement(new{}))));
    }
    [Fact] public void Identity_separates_accounts_and_editors()
    {
        var c=new ProjectConnection(Path.GetFullPath("."),"cursor","https://example.com","project","agent","development","",
            JsonSerializer.SerializeToElement(new{organization_id="org",application_id="app",agent_id="agent"}));
        Assert.NotEqual(c.ComputeIdentity(),(c with{Adapter="vscode"}).ComputeIdentity());
        Assert.NotEqual(c.ComputeIdentity(),(c with{Scope=JsonSerializer.SerializeToElement(new{organization_id="other",application_id="app",agent_id="agent"})}).ComputeIdentity());
        Assert.NotEqual(c.ComputeIdentity(),(c with{Root=Path.Combine(c.Root,"another")}).ComputeIdentity());
    }
}
