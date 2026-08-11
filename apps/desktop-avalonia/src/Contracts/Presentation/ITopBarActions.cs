using System.Windows.Input;

namespace PacToolkits.Desktop.Avalonia.Contracts.Presentation;

public interface ITopBarActions
{
    ICommand? RefreshCommand { get; }
    ICommand? ImportCommand { get; }
    ICommand? ExportCommand { get; }
}
