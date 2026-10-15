using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Deleguard.Desktop.Core;

namespace Deleguard.Desktop;

/// <summary>A separate ephemeral process holds exactly one project's unlocked credential.</summary>
public sealed class ProjectSessionProcess(Process process) : IAsyncDisposable
{
    public static async Task<ProjectSessionProcess> Unlock(ProjectConnection config,string? password)
    {
        var executable=Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"Deleguard.Desktop.exe":"Deleguard.Desktop");
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
            StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,WorkingDirectory=config.Base};
        start.ArgumentList.Add("--session");start.ArgumentList.Add(config.ConfigPath);
        var child=Process.Start(start)??throw new SetupException("session_locked");
        try
        {
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(password));await child.StandardInput.FlushAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result=await child.StandardOutput.ReadLineAsync(timeout.Token);
            if(result!="ready")throw new SetupException(result is "vault_locked" or "invalid_key" or "account_mismatch"?result:"session_locked");
            return new(child);
        }
        catch{if(!child.HasExited)child.Kill(true);child.Dispose();throw;}
    }
    public async ValueTask DisposeAsync()
    {
        if(!process.HasExited)
        {
            process.StandardInput.Close();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();}
        }
        process.Dispose();
    }
}
