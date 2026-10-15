using Deleguard.Application.Audit;

namespace Deleguard.UnitTests;

public sealed class AuditPayloadTests
{
    [Fact]
    public void Playground_authorize_event_requires_attested_source()
    {
        Assert.True(AuditPayload.IsPlaygroundEvent("authorization.decided", """{"source":"playground","decision":"ALLOW"}"""));
        Assert.False(AuditPayload.IsPlaygroundEvent("authorization.decided", """{"decision":"ALLOW"}"""));
        Assert.False(AuditPayload.IsPlaygroundEvent("authorization.decided", """{"source":"agent"}"""));
    }

    [Fact]
    public void Execution_reported_is_playground_only_with_source()
    {
        Assert.True(AuditPayload.IsPlaygroundEvent("execution.reported", """{"source":"playground","decision_id":"dec_1"}"""));
        Assert.False(AuditPayload.IsPlaygroundEvent("execution.reported", """{"decision_id":"dec_1"}"""));
        Assert.False(AuditPayload.IsPlaygroundEvent("approval.resolved", """{"source":"playground"}"""));
    }
}
