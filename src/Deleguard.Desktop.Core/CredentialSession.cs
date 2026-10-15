using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deleguard.Desktop.Core;

public sealed record ApiRequest(string Route, JsonElement? Body = null, string? IdempotencyKey = null);
public sealed record ApiReply(int Status, JsonElement? Body = null, string? Error = null);
public sealed record SessionEndpoint(int Version, int Port, string Capability, string Identity);
internal sealed record SessionPacket(string Identity, string Nonce, string Payload, string Mac);

public static class SessionAuthentication
{
    public static string Sign(string capability,string identity,string nonce,string payload,string direction) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromHexString(capability),Encoding.UTF8.GetBytes(direction+"\n"+identity+"\n"+nonce+"\n"+payload)));
    public static bool Verify(string capability,string identity,string nonce,string payload,string direction,string mac)
    {
        if(nonce.Length!=64 || mac.Length!=64)return false;
        try{return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sign(capability,identity,nonce,payload,direction)),Convert.FromHexString(mac));}
        catch(FormatException){return false;}
    }
}

public static class ProjectApi
{
    public static bool RouteAllowed(ProjectConnection config, ApiRequest request) => request.Route == "/v1/ide/config/" + Uri.EscapeDataString(config.Project) && request.Body is null
        || request.Route is "/v1/authorize" or "/v1/executions" or "/v1/ide/sessions" or "/v1/ide/permits/consume" && request.Body is not null
        || request.Body is null && System.Text.RegularExpressions.Regex.IsMatch(request.Route, "^/v1/decisions/[a-zA-Z0-9_-]{1,128}$");
    public static async Task<ApiReply> Forward(ProjectConnection config, byte[] secret, ApiRequest request, CancellationToken ct)
    {
        config.Validate();
        if (!RouteAllowed(config, request)) throw new SetupException("request_not_allowed");
        if(request.Route=="/v1/authorize")
        {
            var payload=request.Body!.Value; var action=payload.GetProperty("action").GetString()??"";
            if(!(action is "filesystem.read" or "filesystem.list" or "filesystem.search" or "filesystem.create" or "filesystem.write" or "filesystem.delete" or "filesystem.move" or "process.execute" or "mcp.call")
                || payload.GetProperty("agent").GetProperty("id").GetString()!=config.Agent
                || payload.GetProperty("agent").GetProperty("environment").GetString()!=config.Environment
                || payload.GetProperty("resource").GetProperty("attributes").GetProperty("project_id").GetString()!=config.Project
                || payload.GetProperty("context").GetProperty("source").GetString()!="ide-hook"
                || payload.GetProperty("context").GetProperty("adapter").GetString()!=config.Adapter)throw new SetupException("request_not_allowed");
        }
        if(request.Route=="/v1/ide/sessions" && (request.Body!.Value.GetProperty("project_id").GetString()!=config.Project
            ||request.Body.Value.GetProperty("adapter").GetString()!=config.Adapter))throw new SetupException("request_not_allowed");
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) { Timeout=TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Encoding.UTF8.GetString(secret));
        var configRoute = "/v1/ide/config/" + Uri.EscapeDataString(config.Project);
        var remote = await Send(configRoute, null, null);
        if (remote.Status != 200) return remote;
        if (remote.Body is not { } body || new[]{"organization_id","application_id","agent_id"}.Any(k => !body.TryGetProperty(k,out var value) || value.GetString()!=config.Scope.GetProperty(k).GetString())
            || !body.TryGetProperty("id",out var project) || project.GetString()!=config.Project
            || !body.TryGetProperty("agent_external_id",out var external) || external.GetString()!=config.Agent
            || !body.TryGetProperty("environment",out var environment) || environment.GetString()!=config.Environment)
            throw new SetupException("account_mismatch");
        if(request.Route==configRoute)return remote;
        var decisionId=request.Route.StartsWith("/v1/decisions/",StringComparison.Ordinal)?request.Route[14..]:
            request.Route is "/v1/executions" or "/v1/ide/permits/consume"?request.Body!.Value.GetProperty("decision_id").GetString():null;
        if(decisionId is not null)
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(decisionId,"^[a-zA-Z0-9_-]{1,128}$"))throw new SetupException("request_not_allowed");
            var scoped=await Send("/v1/ide/projects/"+Uri.EscapeDataString(config.Project)+"/decisions/"+decisionId,null,null);
            if(scoped.Status!=200||request.Route.StartsWith("/v1/decisions/",StringComparison.Ordinal))return scoped;
        }
        return await Send(request.Route, request.Body, request.IdempotencyKey);

        async Task<ApiReply> Send(string route, JsonElement? data, string? idempotency)
        {
            using var message = new HttpRequestMessage(data is null ? HttpMethod.Get : HttpMethod.Post, config.Url.TrimEnd('/')+route);
            if (data is not null) message.Content=new StringContent(data.Value.GetRawText(),Encoding.UTF8,"application/json");
            if (idempotency is not null)
            {
                if (idempotency.Length>256 || idempotency.Any(char.IsControl)) throw new SetupException("request_not_allowed");
                message.Headers.Add("Idempotency-Key",idempotency);
            }
            using var response=await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);
            using var stream=await response.Content.ReadAsStreamAsync(ct);
            using var output=new MemoryStream(); var buffer=new byte[8192]; int read;
            while((read=await stream.ReadAsync(buffer,ct))>0){if(output.Length+read>1_048_576)throw new SetupException("invalid_response");output.Write(buffer,0,read);}
            if(output.Length==0)return new((int)response.StatusCode);
            using var document=JsonDocument.Parse(output.ToArray()); return new((int)response.StatusCode,document.RootElement.Clone());
        }
    }
}

/// <summary>One project, one unlocked vault, no global registry and no key-export operation.</summary>
public sealed class CredentialSession : IAsyncDisposable
{
    private readonly ProjectConnection config;
    private readonly byte[] secret;
    private readonly string vaultHash;
    private readonly TcpListener listener = new(IPAddress.Loopback,0);
    private readonly CancellationTokenSource stop = new();
    private readonly string capability=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private Task? loop;
    private readonly HashSet<string> seenNonces=new(StringComparer.Ordinal);
    private CredentialSession(ProjectConnection config, byte[] secret)
    { this.config=config;this.secret=secret;vaultHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config.VaultPath))); }
    public static async Task<CredentialSession> Unlock(ProjectConnection config,string? password,CancellationToken ct=default)
    {
        config.Validate();
        var secret=ProjectVault.Open(File.ReadAllText(config.VaultPath),config.CredentialId,password);
        try
        {
            var reply=await ProjectApi.Forward(config,secret,new("/v1/ide/config/"+Uri.EscapeDataString(config.Project)),ct);
            if(reply.Status!=200)throw new SetupException(reply.Status is 401 or 403 ? "invalid_key" : "connection_failed");
            var session=new CredentialSession(config,secret);
            try { session.listener.Start(8); session.loop=session.Listen(); ProjectFiles.WritePrivate(config.SessionPath,
                JsonSerializer.Serialize(new SessionEndpoint(1,((IPEndPoint)session.listener.LocalEndpoint).Port,session.capability,config.CredentialId))); return session; }
            catch { await session.DisposeAsync();throw; }
        }
        catch { CryptographicOperations.ZeroMemory(secret);throw; }
    }
    private async Task Listen()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                using var client=await listener.AcceptTcpClientAsync(stop.Token);
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);deadline.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    var packet=await Frame.Read<SessionPacket>(client.GetStream(),deadline.Token);
                    if(packet.Identity!=config.CredentialId || !SessionAuthentication.Verify(capability,packet.Identity,packet.Nonce,packet.Payload,"request",packet.Mac)
                        || !seenNonces.Add(packet.Nonce))
                        throw new SetupException("session_locked");
                    if(seenNonces.Count>100000)throw new SetupException("session_locked");
                    var current=ProjectFiles.ReadConnection(config.ConfigPath);
                    if(current!=config && current.CredentialId!=config.CredentialId)throw new SetupException("session_locked");
                    if(vaultHash!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(config.VaultPath))))throw new SetupException("session_locked");
                    ApiReply reply;
                    try{reply=await ProjectApi.Forward(config,secret,JsonSerializer.Deserialize<ApiRequest>(packet.Payload)??throw new SetupException("invalid_request"),deadline.Token);}
                    catch(Exception ex){reply=new(0,Error:ex is SetupException e?e.Code:"connection_failed");}
                    var payload=JsonSerializer.Serialize(reply);
                    await Frame.Write(client.GetStream(),new SessionPacket(config.CredentialId,packet.Nonce,payload,
                        SessionAuthentication.Sign(capability,config.CredentialId,packet.Nonce,payload,"response")),deadline.Token);
                }
                catch(Exception ex) when(ex is not OutOfMemoryException)
                { try{await Frame.Write(client.GetStream(),new ApiReply(0,Error:ex is SetupException e?e.Code:"connection_failed"),deadline.Token);}catch{} }
            }
        }
        catch(OperationCanceledException){} catch(SocketException) when(stop.IsCancellationRequested){}
    }
    public static async Task<ApiReply> Call(ProjectConnection config,ApiRequest request,CancellationToken ct=default)
    {
        config.Validate();
        if(OperatingSystem.IsWindows())
        {
            var key=ProjectVault.Open(File.ReadAllText(config.VaultPath),config.CredentialId,null);
            try{return await ProjectApi.Forward(config,key,request,ct);}finally{CryptographicOperations.ZeroMemory(key);}
        }
        if(!File.Exists(config.SessionPath))throw new SetupException("session_locked");
        var endpoint=JsonSerializer.Deserialize<SessionEndpoint>(File.ReadAllText(config.SessionPath))??throw new SetupException("session_locked");
        if(endpoint.Version!=1||endpoint.Identity!=config.CredentialId||endpoint.Port is <1 or >65535||endpoint.Capability.Length!=64)throw new SetupException("session_locked");
        using var client=new TcpClient(); using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await client.ConnectAsync(IPAddress.Loopback,endpoint.Port,timeout.Token);
        var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var payload=JsonSerializer.Serialize(request);
        await Frame.Write(client.GetStream(),new SessionPacket(config.CredentialId,nonce,payload,SessionAuthentication.Sign(endpoint.Capability,config.CredentialId,nonce,payload,"request")),timeout.Token);
        var packet=await Frame.Read<SessionPacket>(client.GetStream(),timeout.Token);
        if(packet.Identity!=config.CredentialId || packet.Nonce!=nonce || !SessionAuthentication.Verify(endpoint.Capability,config.CredentialId,nonce,packet.Payload,"response",packet.Mac))
            throw new SetupException("session_locked");
        return JsonSerializer.Deserialize<ApiReply>(packet.Payload)??throw new SetupException("session_locked");
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();listener.Stop();if(loop is not null)await loop;
        CryptographicOperations.ZeroMemory(secret);
        try
        {
            if(File.Exists(config.SessionPath) && JsonSerializer.Deserialize<SessionEndpoint>(File.ReadAllText(config.SessionPath))?.Capability==capability)
                File.Delete(config.SessionPath);
        }
        catch(IOException){}
        stop.Dispose();
    }
}

internal static class Frame
{
    public static async Task<T> Read<T>(Stream stream,CancellationToken ct)
    {
        var header=new byte[4];await stream.ReadExactlyAsync(header,ct);var length=BinaryPrimitives.ReadInt32BigEndian(header);
        if(length is <1 or >1_048_576)throw new SetupException("invalid_request");
        var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,ct);
        return JsonSerializer.Deserialize<T>(bytes)??throw new SetupException("invalid_request");
    }
    public static async Task Write<T>(Stream stream,T value,CancellationToken ct)
    {
        var data=JsonSerializer.SerializeToUtf8Bytes(value);if(data.Length>1_048_576)throw new SetupException("invalid_response");
        var header=new byte[4];BinaryPrimitives.WriteInt32BigEndian(header,data.Length);await stream.WriteAsync(header,ct);await stream.WriteAsync(data,ct);
    }
}
