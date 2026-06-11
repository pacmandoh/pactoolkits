using System.Windows.Input;

namespace pactoolkits_ui.Contracts;

public interface ITopBarActions
{
    ICommand? RefreshCommand { get; }
    ICommand? ImportCommand { get; }
    ICommand? ExportCommand { get; }

    string? RefreshTip { get; }
    string? ImportTip { get; }
    string? ExportTip { get; }
}
