using System;
using global::Avalonia.Threading;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>
/// ShadUI 自定义对话框视图模型基类
///
/// 统一关闭对话框；当 ShadUI 提前清除回调槽位时，
/// 通过 <see cref="BindSessionCompletion"/> 完成 <c>FormDialogSession</c> 的等待任务
/// </summary>
public abstract class FormBase(DialogManager dialogManager) : ViewModelBase
{
    private Action<bool>? _sessionComplete;

    internal void BindSessionCompletion(Action<bool>? complete)
        => _sessionComplete = complete;

    protected void CloseDialog(bool success = false)
    {
        if (Dispatcher.UIThread.CheckAccess() || global::Avalonia.Application.Current is null)
        {
            CompleteClose(success);
            return;
        }

        Dispatcher.UIThread.Invoke(() => CompleteClose(success));
    }

    private void CompleteClose(bool success)
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
