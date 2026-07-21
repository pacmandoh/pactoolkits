using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>将 <see cref="DataValidationErrors"/> 映射到控件 ToolTip</summary>
public static class ValidationToolTip
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(ValidationToolTip));

    static ValidationToolTip()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
        DataValidationErrors.ErrorsProperty.Changed.AddClassHandler<Control>(OnErrorsChanged);
    }

    public static void SetIsEnabled(Control control, bool value)
        => control.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(Control control)
        => control.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Sync(control);
        }
        else
        {
            ToolTip.SetTip(control, null);
        }
    }

    private static void OnErrorsChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (GetIsEnabled(control))
        {
            Sync(control);
        }
    }

    private static void Sync(Control control)
    {
        var message = DataValidationErrors.GetErrors(control)?
            .Cast<object>()
            .Select(ExtractMessage)
            .FirstOrDefault(static m => !string.IsNullOrWhiteSpace(m));
        ToolTip.SetTip(control, message);
    }

    private static string? ExtractMessage(object? error)
        => error switch
        {
            null => null,
            string text => text,
            Exception ex => ex.Message,
            _ => error.ToString()
        };
}
