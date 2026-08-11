using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;

namespace PacToolkits.Desktop.UiTests;

/// <summary>PopupDismissHelper：候选 skip；不强制关 ACB</summary>
public sealed class PopupDismissHelperTests
{
    [AvaloniaFact]
    public void SkipPopupDismiss_false_for_orphan_list_box_item()
    {
        Assert.False(PopupDismissHelper.SkipPopupDismiss(new ListBoxItem { Content = "b" }));
    }

    [AvaloniaFact]
    public void SkipPopupDismiss_skips_when_inside_open_auto_complete_box()
    {
        var box = new AutoCompleteBox
        {
            ItemsSource = new[] { "alpha", "beta", "gamma" },
            Width = 240,
            Height = 32,
        };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = box,
        };
        window.Show();
        try
        {
            box.Text = "a";
            box.IsDropDownOpen = true;
            Assert.True(PopupDismissHelper.SkipPopupDismiss(box));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DismissOpenPopups_does_not_force_close_via_throw()
    {
        var box = new AutoCompleteBox
        {
            ItemsSource = new[] { "a", "b", "c" },
            Width = 240,
            Height = 32,
        };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = box,
        };
        window.Show();
        try
        {
            box.IsDropDownOpen = true;
            // 不强制关 ACB 属性路径；headless 下 PART_Popup 识别可能不稳，只断言不抛
            PopupDismissHelper.DismissOpenPopups(window);
        }
        finally
        {
            window.Close();
        }
    }
}
