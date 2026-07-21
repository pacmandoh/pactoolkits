using CommunityToolkit.Mvvm.ComponentModel;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class InjectorLineItem : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;

    public InjectorLineItem()
    {
    }

    public InjectorLineItem(string value)
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
