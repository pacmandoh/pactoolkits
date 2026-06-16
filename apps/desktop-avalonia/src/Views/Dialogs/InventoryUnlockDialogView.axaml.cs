using System;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public partial class InventoryUnlockDialogView : UserControl
{
    public static readonly StyledProperty<string?> HintMessageProperty =
        AvaloniaProperty.Register<InventoryUnlockDialogView, string?>(nameof(HintMessage), defaultValue: null);

    public string? HintMessage
    {
        get => GetValue(HintMessageProperty);
        set => SetValue(HintMessageProperty, value);
    }

    public string Password => PasswordBox.Text ?? string.Empty;
    public event Action? SubmitRequested;

    public InventoryUnlockDialogView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => PasswordBox.Focus(), DispatcherPriority.Input);
    }

    private void PasswordBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        SubmitRequested?.Invoke();
    }
}
