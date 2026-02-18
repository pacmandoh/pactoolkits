using Avalonia;
using Avalonia.Controls;

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

    public InventoryUnlockDialogView()
    {
        InitializeComponent();
    }
}
