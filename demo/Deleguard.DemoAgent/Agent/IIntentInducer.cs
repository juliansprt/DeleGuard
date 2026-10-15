namespace Deleguard.DemoAgent.Agent;

public interface IIntentInducer
{
    Task<InducedIntent> InduceAsync(string userText, CancellationToken cancellationToken);
}
