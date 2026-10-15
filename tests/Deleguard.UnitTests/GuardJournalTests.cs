using Deleguard.McpGuard;
namespace Deleguard.UnitTests;
public sealed class GuardJournalTests
{
    [Fact]
    public async Task Claim_is_durable_and_exclusive_across_instances()
    {
        var root=Path.Combine(Path.GetTempPath(),"dg-journal-"+Guid.NewGuid().ToString("N"));
        try {
            var a=new GuardJournal(root);var b=new GuardJournal(root);
            var results=await Task.WhenAll(Task.Run(()=>a.TryClaim("decision")),Task.Run(()=>b.TryClaim("decision")));
            Assert.Single(results,x=>x);Assert.False(new GuardJournal(root).TryClaim("decision"));
            Assert.True(new GuardJournal(root).TryClaim("different"));
        } finally {Directory.Delete(root,true);}
    }
}
