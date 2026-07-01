using CommunityToolkit.Mvvm.ComponentModel;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class AgentLineItem : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;

    public AgentLineItem()
    {
    }

    public AgentLineItem(string value)
    {
        _value = value;
    }
}

public sealed class CodePickPolicyOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    public override string ToString() => Label;
}
