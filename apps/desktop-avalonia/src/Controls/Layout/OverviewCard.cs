using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace PacToolkits.Desktop.Avalonia.Controls;

public sealed class OverviewCard : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<OverviewCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<OverviewCard, string?>(nameof(Value));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<OverviewCard, string?>(nameof(Hint));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<OverviewCard, object?>(nameof(Icon));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<OverviewCard, bool>(nameof(IsActive));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<OverviewCard, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<OverviewCard, object?>(nameof(CommandParameter));

    public static readonly DirectProperty<OverviewCard, bool> ShowHintProperty =
        AvaloniaProperty.RegisterDirect<OverviewCard, bool>(
            nameof(ShowHint),
            o => o.ShowHint);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool ShowHint => !string.IsNullOrWhiteSpace(Hint);

    static OverviewCard()
    {
        HintProperty.Changed.AddClassHandler<OverviewCard>((card, _) => card.RaiseHintVisibility());
        FocusableProperty.OverrideDefaultValue<OverviewCard>(true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Handled || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height)
        {
            return;
        }

        ExecuteCommand();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        ExecuteCommand();
        e.Handled = true;
    }

    private void ExecuteCommand()
    {
        var command = Command;
        if (command?.CanExecute(CommandParameter) == true)
        {
            command.Execute(CommandParameter);
        }
    }

    private void RaiseHintVisibility() => RaisePropertyChanged(ShowHintProperty, false, ShowHint);
}
