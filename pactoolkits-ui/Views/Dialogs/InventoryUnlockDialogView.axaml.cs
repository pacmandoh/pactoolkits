using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace pactoolkits_ui.Views.Dialogs;

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

    private void PasswordBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        SubmitRequested?.Invoke();
    }
}
