using System;
using Avalonia.Threading;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public abstract class FormDialogViewModelBase(DialogManager dialogManager) : ViewModelBase
{
    private Action<bool>? _sessionComplete;

    internal void BindSessionCompletion(Action<bool>? complete)
        => _sessionComplete = complete;

    protected void CloseDialog(bool success = false)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseDialogCore(success);
            return;
        }

        Dispatcher.UIThread.Invoke(() => CloseDialogCore(success));
    }

    private void CloseDialogCore(bool success)
    {
        if (success)
        {
            dialogManager.Close(this, new CloseDialogOptions { Success = true });
        }
        else
        {
            dialogManager.Close(this);
        }

        _sessionComplete?.Invoke(success);
    }
}
