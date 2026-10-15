using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Deleguard.McpGuard;

/// <summary>Durable, cross-process at-most-once forwarding. Stores no tool arguments or results.</summary>
public sealed class GuardJournal
{
    private readonly string _root;
    public PendingGuardStore Pending { get; }
    public GuardJournal(string? root=null)
    {
        _root=root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeleGuard","mcp-journal");
        Directory.CreateDirectory(_root);
        Pending = new PendingGuardStore(_root);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(_root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    }
    private string Name(string id,string suffix)=>Path.Combine(_root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))+suffix);
    public bool TryClaim(string decision)
    {
        try {using var file=new FileStream(Name(decision,".claimed"),FileMode.CreateNew,FileAccess.Write,FileShare.None);file.WriteByte(1);file.Flush(true);return true;}
        catch(IOException) when(File.Exists(Name(decision,".claimed"))) {return false;}
    }
    public void Queue(string decision,string status,string? reference)
    {
        var path=Name(decision,".evidence");var temporary=path+"."+Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary,JsonSerializer.Serialize(new PendingEvidence(decision,status,reference)));
        File.Move(temporary,path,true);
    }
    public async Task FlushAsync(IDeleguardPdp pdp,CancellationToken ct)
    {
        FileStream lease;
        try { lease=new FileStream(Path.Combine(_root,".flush.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException) { return; }
        using var flushLease=lease;
        foreach(var file in Directory.EnumerateFiles(_root,"*.evidence"))
        {
            try {var record=JsonSerializer.Deserialize<PendingEvidence>(await File.ReadAllTextAsync(file,ct));if(record is null)continue;await pdp.ReportExecutionAsync(record.Decision,record.Status,record.Reference,ct);File.Delete(file);}
            catch(OperationCanceledException) {throw;}
            catch { /* Preserve evidence for later diagnosis/retry. Never forward the operation again. */ }
        }
    }
    private sealed record PendingEvidence(string Decision,string Status,string? Reference);
}
