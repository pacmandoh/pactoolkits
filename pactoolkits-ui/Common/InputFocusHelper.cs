using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace pactoolkits_ui.Common;

public static class InputFocusHelper
{
    public static T? FindAncestor<T>(object? source) where T : class
    {
        var current = source;
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = (current as StyledElement)?.Parent;
        }

        return null;
    }

    public static T? FindDescendant<T>(Control root) where T : class
    {
        var stack = new Stack<Control>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is T hit)
                return hit;

            foreach (var child in current.GetVisualChildren())
            {
                if (child is Control controlChild)
                    stack.Push(controlChild);
            }
        }

        return null;
    }

    public static void CommitAutoCompleteInput(AutoCompleteBox box)
    {
        box.IsDropDownOpen = false;

        var tb = FindDescendant<TextBox>(box);
        if (tb is null)
            return;

        CommitTextInput(tb);
    }

    public static void CommitTextInput(TextBox textBox)
    {
        var caret = textBox.CaretIndex;
        textBox.SelectionStart = caret;
        textBox.SelectionEnd = caret;
    }

    public static void FocusControlByName(Control host, string controlName)
    {
        FocusControlByName(host, controlName, DispatcherPriority.Input);
    }

    public static void FocusControlByName(Control host, string controlName, DispatcherPriority priority)
    {
        Dispatcher.UIThread.Post(() =>
        {
            host.FindControl<Control>(controlName)?.Focus();
        }, priority);
    }

    public static bool TryHandleTabCycle(Control host, KeyEventArgs e, IReadOnlyList<Control> inputs)
    {
        if ((e.Key != Key.Tab && e.Key != Key.Enter) || inputs.Count == 0)
            return false;

        if (TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() is not Control focused)
            return false;

        var idx = -1;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (ReferenceEquals(inputs[i], focused))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
            return false;

        var backward = e.Key == Key.Tab &&
                       (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
        var next = backward ? (idx - 1 + inputs.Count) % inputs.Count : (idx + 1) % inputs.Count;

        e.Handled = true;
        FocusAndMoveCaretToEnd(inputs[next]);
        return true;
    }

    public static IReadOnlyList<Control> EnumerateInputs(Control root, params System.Type[] allowedTypes)
    {
        if (allowedTypes.Length == 0)
            return [];

        return root.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.IsVisible && c.IsEnabled && c.Focusable)
            .Where(c => IsAllowedType(c, allowedTypes))
            .ToList();
    }

    private static bool IsAllowedType(Control control, IReadOnlyList<System.Type> allowedTypes)
    {
        for (var i = 0; i < allowedTypes.Count; i++)
        {
            if (allowedTypes[i].IsInstanceOfType(control))
                return true;
        }

        return false;
    }

    private static TextBox? ResolveTextBox(Control control)
    {
        if (control is TextBox tb)
            return tb;

        var current = control as StyledElement;
        while (current is not null)
        {
            if (current is TextBox owner)
                return owner;

            current = current.Parent;
        }

        return null;
    }

    private static void FocusAndMoveCaretToEnd(Control target)
    {
        target.Focus();

        Dispatcher.UIThread.Post(() =>
        {
            var top = TopLevel.GetTopLevel(target);
            if (top?.FocusManager?.GetFocusedElement() is not Control focused)
                return;

            var tb = ResolveTextBox(focused) ?? focused.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (tb is null)
                return;

            var end = tb.Text?.Length ?? 0;
            tb.CaretIndex = end;
            tb.SelectionStart = end;
            tb.SelectionEnd = end;
        }, DispatcherPriority.Input);
    }
}
