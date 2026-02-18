using CommunityToolkit.Mvvm.ComponentModel;

namespace pactoolkits_ui.ViewModels.Pages;

public sealed partial class ClientAliasRow : ObservableObject
{
    public ClientAliasRow(string machine, string alias)
    {
        _machine = machine;
        _alias = alias;
    }

    [ObservableProperty] private string _machine;
    [ObservableProperty] private string _alias;
}