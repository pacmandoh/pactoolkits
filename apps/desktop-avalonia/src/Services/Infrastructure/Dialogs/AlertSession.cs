using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

internal static class AlertSession
{
    private const double DefaultMaxWidth = 512;

    internal static Task<T> ShowAsync<T>(
        DialogManager manager,
        AlertBuilder<T> alert,
        double maxWidth = DefaultMaxWidth)
    {
        if (!alert.HasCloseValue)
        {
            throw new InvalidOperationException("Alert requires Close(...) so dismiss (X) matches cancel semantics.");
        }

        return DialogAwaiter.RunAlertAsync<T>(manager, tcs =>
        {
            var completed = 0;
            void Finish(T result)
            {
                if (Interlocked.CompareExchange(ref completed, 1, 0) != 0)
                {
                    return;
                }

                DialogSessionStack.UnbindSimpleDialogDismiss(manager);
                tcs.TrySetResult(result);
            }

            try
            {
                var dialog = manager.CreateDialog(alert.Title, alert.Message);

                foreach (var button in alert.Buttons.OrderBy(b => AlertLayout.Slot(b.Role)))
                {
                    Action onClick = () => Finish(button.Value);
                    var style = AlertLayout.Style(button.Role);
                    dialog = AlertLayout.Slot(button.Role) switch
                    {
                        AlertSlot.Left => dialog.WithCancelButton(button.Text, onClick, style),
                        AlertSlot.Right => dialog.WithPrimaryButton(button.Text, onClick, style),
                        _ => dialog
                    };
                }

                dialog.WithMaxWidth(maxWidth).Dismissible().Show();
                DialogSessionStack.BindSimpleDialogDismiss(manager, () => Finish(alert.CloseValue));
            }
            catch
            {
                DialogSessionStack.UnbindSimpleDialogDismiss(manager);
                throw;
            }
        });
    }
}
