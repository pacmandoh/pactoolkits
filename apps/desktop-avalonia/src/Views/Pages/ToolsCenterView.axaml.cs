using System;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ToolsCenterView : UserControl
{
    public ToolsCenterView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnToolsAreaKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnToolsAreaKeyDown(object? sender, KeyEventArgs e)
    {
        var editor = this.FindControl<Control>("ToolEditorCard");
        if (editor is null)
        {
            return;
        }

        var inputs = InputFocusHelper.EnumerateInputs(editor, typeof(TextBox));
        InputFocusHelper.TryHandleTabCycle(this, e, inputs);
    }

    private void OnAddLineClicked(object? sender, RoutedEventArgs e)
    {
        var editor = this.FindControl<Control>("ToolEditorCard");
        var scroller = editor?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var shouldAutoFollow = false;
        if (scroller is not null)
        {
            // Only auto-follow when user is already at the bottom area.
            var remain = scroller.Extent.Height - (scroller.Offset.Y + scroller.Viewport.Height);
            shouldAutoFollow = remain <= 28;
        }

        var targetName = (sender as Button)?.Tag as string;

        Dispatcher.UIThread.Post(() =>
        {
            if (shouldAutoFollow && scroller is not null)
            {
                var maxY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
                scroller.Offset = new Vector(scroller.Offset.X, maxY);
            }

            if (string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            var itemsControl = this.FindControl<ItemsControl>(targetName);
            if (itemsControl is null)
            {
                return;
            }

            var targetBox = itemsControl.GetVisualDescendants().OfType<TextBox>().LastOrDefault();
            if (targetBox is null)
            {
                return;
            }

            targetBox.Focus();
            targetBox.CaretIndex = targetBox.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }
}
