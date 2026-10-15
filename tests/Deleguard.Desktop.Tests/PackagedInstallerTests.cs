using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deleguard.Desktop.Core;

namespace Deleguard.Desktop.Tests;

public sealed class PackageFactAttribute : FactAttribute
{
    public PackageFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DELEGUARD_PORTABLE_RUNTIME")))Skip="Requires a built package on its native OS.";}
}

public sealed class PackagedInstallerTests
{
    [PackageFact]
    public async Task Native_package_connects_two_accounts_reinstalls_and_disconnects_independently()
    {
        var runtime=Environment.GetEnvironmentVariable("DELEGUARD_PORTABLE_RUNTIME")!;
        var work=Path.Combine(Environment.GetEnvironmentVariable("DELEGUARD_UI_ARTIFACTS")??Path.GetTempPath(),"package-smoke-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);var sentinel=Path.Combine(work,"unused-global");Directory.CreateDirectory(sentinel);
        await using var server=new ScopeServer();
        var connections=new List<ProjectConnection>();
        var sessions=new List<Process>();
        try
        {
            foreach(var account in new[]{"first","second"})
            {
                var root=Path.Combine(work,account,"same project name");Directory.CreateDirectory(root);
                var key="dgk_smoke_"+account;
                var profile=new{schema_version=1,server_url=server.Url,organization_id="org-"+account,application_id="app-"+account,
                    project_id="same-project",agent_id="agent-"+account,agent_external_id="assistant",environment="development",adapter="vscode"};
                var scope=new{id=profile.project_id,organization_id=profile.organization_id,application_id=profile.application_id,agent_id=profile.agent_id,agent_external_id=profile.agent_external_id,environment=profile.environment,case_sensitive=CaseSensitive(root)};
                server.Keys[key]=JsonSerializer.Serialize(scope);
                object Request(string operation,string? vault=null)=>new{operation,root,runtime,profile,adapter="vscode",key,vault};
                var preview=await Worker(Request("preview"));
                Assert.False(preview.GetProperty("changes_applied").GetBoolean());Assert.Empty(Directory.GetFileSystemEntries(root));
                var connection=ProjectFiles.ParseConnection(preview.GetProperty("config"));connections.Add(connection);
                var vault=ProjectVault.Seal(key,connection.CredentialId,"a separate project password");
                var installed=await Worker(Request("install",vault));Assert.Equal("installed",installed.GetProperty("status").GetString());
                Assert.DoesNotContain(key,File.ReadAllText(connection.ConfigPath));Assert.DoesNotContain(key,File.ReadAllText(connection.VaultPath));
                if(!OperatingSystem.IsWindows())
                {
                    var child=Start(Path.Combine(runtime,"Deleguard.Desktop"),["--session",connection.ConfigPath]);
                    await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize("a separate project password"));await child.StandardInput.FlushAsync();
                    Assert.Equal("ready",await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));sessions.Add(child);
                }
                var reply=await Bridge(connection);Assert.Equal(200,reply.GetProperty("Status").GetInt32());
                await Worker(Request("install",vault));
                using var hooks=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,".github","hooks","deleguard.json")));
                var before=hooks.RootElement.GetProperty("hooks").GetProperty("PreToolUse");Assert.Equal(1,before.GetArrayLength());
                var command=before[0].GetProperty("command").GetString()!;
                if(OperatingSystem.IsWindows())command=Encoding.Unicode.GetString(Convert.FromBase64String(command.Split(' ')[^1]));
                Assert.Contains("deleguard-adapter",command);
            }
            var legacy=await Run(Path.Combine(runtime,"worker",OperatingSystem.IsWindows()?"deleguard-adapter.exe":"deleguard-adapter"),["doctor","--profile","legacy"],new{});
            Assert.Equal("project_scope_required",legacy.GetProperty("error").GetString());
            var simultaneous=await Task.WhenAll(connections.Select(Bridge));
            Assert.All(simultaneous,reply=>Assert.Equal(200,reply.GetProperty("Status").GetInt32()));
            var validScope=server.Keys["dgk_smoke_second"];
            var wrongScope=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(validScope)!;
            wrongScope["environment"]=JsonSerializer.SerializeToElement("other-environment");
            server.Keys["dgk_smoke_second"]=JsonSerializer.Serialize(wrongScope);
            Assert.Equal("account_mismatch",(await Bridge(connections[1])).GetProperty("Error").GetString());
            server.Keys["dgk_smoke_second"]=validScope;
            Assert.NotEqual(connections[0].CredentialId,connections[1].CredentialId);
            var untouched=Snapshot(connections[1].Root);
            await Worker(new{operation="disconnect",config_path=connections[0].ConfigPath,runtime});
            Assert.False(File.Exists(connections[0].VaultPath));Assert.Equal(untouched,Snapshot(connections[1].Root));
            server.Keys.TryRemove("dgk_smoke_second",out _);
            Assert.Equal(401,(await Bridge(connections[1])).GetProperty("Status").GetInt32());
            Assert.Empty(Directory.GetFileSystemEntries(sentinel,"*",SearchOption.AllDirectories));
        }
        finally
        {
            foreach(var process in sessions){process.StandardInput.Close();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));process.Dispose();}
        }

        Process Start(string executable,string[] args)
        {
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,WorkingDirectory=work};
            foreach(var arg in args)info.ArgumentList.Add(arg);
            foreach(var variable in new[]{"LOCALAPPDATA","APPDATA","HOME","TMP","TEMP","TMPDIR","XDG_CONFIG_HOME","XDG_DATA_HOME","XDG_CACHE_HOME"})info.Environment[variable]=sentinel;
            return Process.Start(info)!;
        }
        async Task<JsonElement> Run(string executable,string[] args,object request)
        {
            using var process=Start(executable,args);var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request));process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
            var raw=await output;Assert.True(string.IsNullOrWhiteSpace(await errors),"Unexpected runtime stderr");
            using var json=JsonDocument.Parse(raw);return json.RootElement.Clone();
        }
        Task<JsonElement> Worker(object request)=>Run(Path.Combine(runtime,"worker",OperatingSystem.IsWindows()?"deleguard-adapter.exe":"deleguard-adapter"),["portable"],request);
        Task<JsonElement> Bridge(ProjectConnection c)=>Run(Path.Combine(c.Base,"runtime",OperatingSystem.IsWindows()?"Deleguard.Desktop.exe":"Deleguard.Desktop"),["--bridge",c.ConfigPath],new ApiRequest("/v1/ide/config/"+c.Project));
    }
    private static string[] Snapshot(string root)=>Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order().Select(path=>Path.GetRelativePath(root,path)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static bool CaseSensitive(string root)
    {var probe=Path.Combine(root,"CaseProbe");File.WriteAllText(probe,"");try{return !File.Exists(Path.Combine(root,"caseprobe"));}finally{File.Delete(probe);}}
    private sealed class ScopeServer : IAsyncDisposable
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string,string> Keys {get;}=new();
        private readonly TcpListener listener=new(IPAddress.Loopback,0);private readonly CancellationTokenSource stop=new();private readonly Task loop;
        public string Url {get;}
        public ScopeServer(){listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;loop=Listen();}
        private async Task Listen()
        {
            try{while(!stop.IsCancellationRequested)
            {
                using var client=await listener.AcceptTcpClientAsync(stop.Token);var stream=client.GetStream();using var reader=new StreamReader(stream,leaveOpen:true);
                var request=await reader.ReadLineAsync(stop.Token);var key="";string? line;
                while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync(stop.Token)))if(line.StartsWith("Authorization: Bearer ",StringComparison.OrdinalIgnoreCase))key=line[22..];
                var valid=Keys.TryGetValue(key,out var body)&&request?.StartsWith("GET /v1/ide/config/same-project ")==true;
                body=valid?body:"{}";var data=Encoding.UTF8.GetBytes(body!);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid?"200 OK":"401 Unauthorized")}\r\nContent-Type: application/json\r\nContent-Length: {data.Length}\r\nConnection: close\r\n\r\n"),stop.Token);
                await stream.WriteAsync(data,stop.Token);
            }}catch(OperationCanceledException){}catch(SocketException)when(stop.IsCancellationRequested){}
        }
        public async ValueTask DisposeAsync(){stop.Cancel();listener.Stop();await loop;stop.Dispose();}
    }
}
