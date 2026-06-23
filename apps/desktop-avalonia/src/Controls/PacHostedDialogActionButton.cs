using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Controls;

public sealed class PacHostedDialogActionButton : Button
{
    public static readonly StyledProperty<PacHostedDialogAction?> ActionProperty =
        AvaloniaProperty.Register<PacHostedDialogActionButton, PacHostedDialogAction?>(nameof(Action));

    public PacHostedDialogAction? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public PacHostedDialogActionButton()
    {
        Click += OnClick;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ActionProperty)
        {
            ApplyAction(Action);
        }
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (Action is null)
        {
            return;
        }

        var context = this.FindAncestorOfType<PacHostedDialogView>()?.DataContext as PacHostedDialogContext;
        context?.InvokeAction(Action);
    }

    private void ApplyAction(PacHostedDialogAction? action)
    {
        if (action is null)
        {
            return;
        }

        Content = action.Text;
        var className = DialogButtonStyleConverters.ToClass.Convert(action.Style, typeof(string), null, null!) as string
            ?? "Secondary";

        Classes.Clear();
        Classes.Add(className);
    }
}
