using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Events;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

public sealed class NullAgentEventSink : IAgentEventSink
{
    public void Publish(AgentExecutionEvent executionEvent)
    {
    }
}
