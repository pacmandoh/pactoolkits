using System;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 表单 Tab/Enter 循环（K）：隧道截获键，在可见可焦点输入间移动
/// RootName 限定枚举子树；IncludeComboBox 是否纳入 ComboBox
/// </summary>
public class TabScope
{
    private static readonly Type[] FieldTypes =
    [
        typeof(TextBox),
        typeof(NumericUpDown)
    ];

    private static readonly Type[] FieldTypesWithCombo =
    [
        ..FieldTypes,
        typeof(ComboBox)
    ];

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<TabScope, Control, bool>("Enable");

    public static readonly AttachedProperty<string?> RootNameProperty =
        AvaloniaProperty.RegisterAttached<TabScope, Control, string?>("RootName");

    public static readonly AttachedProperty<bool> IncludeComboBoxProperty =
        AvaloniaProperty.RegisterAttached<TabScope, Control, bool>("IncludeComboBox");

    static TabScope()
    {
        EnableProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.GetNewValue<bool>())
            {
                c.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: false);
            }
            else
            {
                c.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            }
        });
    }

    public static void SetEnable(AvaloniaObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(AvaloniaObject element) =>
        element.GetValue(EnableProperty);

    public static void SetRootName(AvaloniaObject element, string? value) =>
        element.SetValue(RootNameProperty, value);

    public static string? GetRootName(AvaloniaObject element) =>
        element.GetValue(RootNameProperty);

    public static void SetIncludeComboBox(AvaloniaObject element, bool value) =>
        element.SetValue(IncludeComboBoxProperty, value);

    public static bool GetIncludeComboBox(AvaloniaObject element) =>
        element.GetValue(IncludeComboBoxProperty);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control host)
        {
            return;
        }

        if (e.Key is not (Key.Tab or Key.Enter))
        {
            return;
        }

        var root = ResolveRoot(host);
        if (root is null)
        {
            return;
        }

        var types = GetIncludeComboBox(host) ? FieldTypesWithCombo : FieldTypes;
        var inputs = InputFocusHelper.EnumerateInputs(root, types);
        InputFocusHelper.TryHandleTabCycle(host, e, inputs);
    }

    private static Control? ResolveRoot(Control host)
    {
        var name = GetRootName(host);
        if (string.IsNullOrEmpty(name))
        {
            return host;
        }

        return host.FindControl<Control>(name);
    }
}
