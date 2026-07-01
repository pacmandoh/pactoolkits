using CommunityToolkit.Mvvm.Input;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>ShadUI Demo Login pattern — singleton VM, Initialize before Show, Close from Submit/Cancel.</summary>
public sealed partial class SensitiveUnlock(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    private string _title = string.Empty;
    private string _hintMessage = string.Empty;
    private string _password = string.Empty;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string HintMessage
    {
        get => _hintMessage;
        private set => SetProperty(ref _hintMessage, value);
    }

    public string Username { get; } = "pacmandoh";

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public void Initialize(string title, string hintMessage)
    {
        Title = title;
        HintMessage = hintMessage;
        Password = string.Empty;
    }

    [RelayCommand]
    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(Password))
        {
            return;
        }

        CloseDialog(success: true);
    }

    [RelayCommand]
    private void Cancel()
        => CloseDialog();
}
