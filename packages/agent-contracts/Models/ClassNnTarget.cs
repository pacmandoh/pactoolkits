namespace PacToolkits.Agent.Contracts.Models;

/// <summary>
/// Win32 ClassNN selector used by the AHK agent to focus grids and inputs.
/// </summary>
public sealed record ClassNnTarget(string Value)
{
    public override string ToString() => Value;
}
