using PacToolkits.Agent.Contracts.Events;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentEventSink
{
    void Publish(AgentExecutionEvent executionEvent);
}
