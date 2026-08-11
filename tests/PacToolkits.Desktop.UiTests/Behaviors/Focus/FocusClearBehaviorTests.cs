using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Controls;
using Point = Avalonia.Point;

namespace PacToolkits.Desktop.UiTests;

/// <summary>FocusClear：空白抬 K、表外原生清选、Suppress 保留、表间 peer 互斥</summary>
public sealed class FocusClearBehaviorTests
{
    private static readonly Point TitleClick = new(40, 16);
    private static readonly Point BelowGridClick = new(40, 140);

    [AvaloniaFact]
    public void Blank_title_click_moves_focus_off_textbox()
    {
        var title = new TextBlock { Text = "追溯码录入", Height = 32, Width = 200 };
        var editor = new TextBox { Text = "8901", Height = 120, Width = 400, AcceptsReturn = true };
        var root = new StackPanel { Spacing = 8, Children = { title, editor } };

        var window = ShowFocusHost(root);
        try
        {
            FocusAndFlush(editor, window);
            Assert.True(editor.IsKeyboardFocusWithin);

            Click(window, TitleClick);

            Assert.False(editor.IsKeyboardFocusWithin, "点击区块标题应抬走 TextBox 焦点");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TraceCodeEditor_section_title_click_clears_editor_focus()
    {
        var title = new TextBlock { Text = "追溯码录入", Height = 28, Width = 240 };
        var editor = new TraceCodeEditor
        {
            Height = 160,
            Width = 400,
            Text = "8901\n8902",
            PlaceholderText = "paste",
        };
        var body = new StackPanel { Spacing = 12, Children = { title, editor } };

        var window = ShowFocusHost(body);
        try
        {
            var inner = editor.GetVisualDescendants().OfType<TextBox>().First();
            FocusAndFlush(inner, window);
            Assert.True(inner.IsKeyboardFocusWithin);

            Click(window, TitleClick);

            Assert.False(inner.IsKeyboardFocusWithin, "TraceCodeEditor：点标题必须清掉内层 TextBox 焦点");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Outside_grid_click_clears_selection()
    {
        var grid = new DataGrid
        {
            Height = 80,
            Width = 300,
            ItemsSource = new[] { "a", "b" },
        };
        var blank = new Border
        {
            Height = 120,
            Width = 300,
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
        };
        var body = new StackPanel { Children = { grid, blank } };

        var window = ShowFocusHost(body);
        try
        {
            Flush(window);
            grid.SelectedIndex = 0;
            Assert.NotNull(grid.SelectedItem);

            // Window 自身可 Focus 时仍须清表选：只有可编辑输入算“点在自身内”
            Click(window, BelowGridClick);

            Assert.Null(grid.SelectedItem);
            Assert.Equal(-1, grid.SelectedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SuppressGridClear_keeps_selection_on_blank_click()
    {
        var grid = new DataGrid
        {
            Height = 80,
            Width = 300,
            ItemsSource = new[] { "a", "b" },
        };
        var blank = new Border
        {
            Height = 120,
            Width = 300,
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
        };
        var page = new StackPanel { Children = { grid, blank } };
        FocusClear.SetSuppressGridClear(page, true);

        var window = ShowFocusHost(page);
        try
        {
            Flush(window);
            grid.SelectedIndex = 1;
            Assert.Equal("b", grid.SelectedItem);

            Click(window, BelowGridClick);

            Assert.Equal("b", grid.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_one_grid_clears_peer_grid_selection()
    {
        // 表间仅保留一张原生选中；headless 命中不稳时直调 peer 清选
        var gridA = new DataGrid
        {
            Height = 100,
            Width = 200,
            ItemsSource = new[] { "a1", "a2" },
        };
        var gridB = new DataGrid
        {
            Height = 100,
            Width = 200,
            ItemsSource = new[] { "b1", "b2" },
        };
        var body = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children = { gridA, gridB },
        };

        var window = ShowFocusHost(body);
        try
        {
            Flush(window);
            gridA.SelectedIndex = 0;
            gridB.SelectedIndex = 1;
            Assert.Equal("a1", gridA.SelectedItem);
            Assert.Equal("b2", gridB.SelectedItem);

            FocusClear.ClearPeerSelectionsForTests(window, gridB);

            Assert.Null(gridA.SelectedItem);
            Assert.Equal("b2", gridB.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window ShowFocusHost(Control content)
    {
        var window = new Window
        {
            Width = 640,
            Height = 480,
            Focusable = true,
            Content = content,
        };
        FocusClear.SetEnable(window, true);
        window.Show();
        Flush(window);
        return window;
    }

    private static void FocusAndFlush(Control target, Window window)
    {
        target.Focus();
        Flush(window);
    }

    private static void Click(Window window, Point point)
    {
        Flush(window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
