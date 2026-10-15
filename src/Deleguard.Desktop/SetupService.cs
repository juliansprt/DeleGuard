using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Deleguard.Desktop.Core;

namespace Deleguard.Desktop;

public sealed class SetupService
{
    public string Runtime {get;}=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public async Task<JsonElement> Run(Dictionary<string,object?> request,CancellationToken ct=default)
    {
        var executable=Path.Combine(Runtime,"worker",OperatingSystem.IsWindows()?"deleguard-adapter.exe":"deleguard-adapter");
        if(!File.Exists(executable))throw new SetupException("runtime_missing");
        request["runtime"]=Runtime;
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,WorkingDirectory=Runtime};
        start.ArgumentList.Add("portable");start.Environment["PYTHONDONTWRITEBYTECODE"]="1";
        using var process=Process.Start(start)??throw new SetupException("runtime_missing");
        var output=process.StandardOutput.ReadToEndAsync(ct);var errors=process.StandardError.ReadToEndAsync(ct);
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request));process.StandardInput.Close();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw new SetupException("installation_timeout");}
        _=await errors;
        var data=await output;
        if(data.Length>1_048_576)throw new SetupException("invalid_response");
        using var result=JsonDocument.Parse(data);
        if(result.RootElement.TryGetProperty("error",out var code))throw new SetupException(code.GetString()??"connection_failed");
        if(process.ExitCode!=0)throw new SetupException("connection_failed");
        return result.RootElement.Clone();
    }
    public static JsonElement ReadProfile(string path)
    {
        var data=File.ReadAllText(path);if(data.Length>65536)throw new SetupException("invalid_profile");
        using var document=JsonDocument.Parse(data);var p=document.RootElement;
        if(p.GetProperty("schema_version").GetInt32()!=1)throw new SetupException("invalid_profile");
        ProjectConnection.ValidateUrl(p.GetProperty("server_url").GetString()!);
        foreach(var field in new[]{"project_id","agent_id","agent_external_id","organization_id","application_id","environment","adapter"})
            if(!p.TryGetProperty(field,out var v)||v.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(v.GetString()))throw new SetupException("invalid_profile");
        return p.Clone();
    }
    public static JsonElement ProfileFor(ProjectConnection c)=>JsonSerializer.SerializeToElement(new
    {
        schema_version=1,server_url=c.Url,project_id=c.Project,agent_id=c.Scope.GetProperty("agent_id").GetString(),agent_external_id=c.Agent,
        organization_id=c.Scope.GetProperty("organization_id").GetString(),application_id=c.Scope.GetProperty("application_id").GetString(),environment=c.Environment,adapter=c.Adapter
    });
}
