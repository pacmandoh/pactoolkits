using System.Windows.Input;

namespace PacToolkits.Desktop.Avalonia.Contracts;

public interface ITopBarActions
{
    ICommand? RefreshCommand { get; }
    ICommand? ImportCommand { get; }
    ICommand? ExportCommand { get; }
}
