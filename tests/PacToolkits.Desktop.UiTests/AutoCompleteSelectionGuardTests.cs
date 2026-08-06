using System.Collections.ObjectModel;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.UiTests;

/// <summary>Avalonia #6128：动态 ItemsSource 后关 ACB 不得越界</summary>
public sealed class AutoCompleteSelectionGuardTests
{
    [AvaloniaFact]
    public void Align_and_close_after_items_shrunk_does_not_throw()
    {
        var items = new ObservableCollection<OptionItem>();
        for (var i = 0; i < 8; i++)
        {
            items.Add(new OptionItem($"raw-{i}", $"显示-{i}"));
        }

        var box = new AutoCompleteBox
        {
            ItemsSource = items,
            Width = 280,
            Height = 32,
        };
        AutoCompleteFilter.AttachDrugOptionFilter(box);

        var window = new Window
        {
            Width = 420,
            Height = 320,
            Content = box,
        };
        window.Show();
        try
        {
            box.Text = "显";
            box.IsDropDownOpen = true;

            if (AutoCompleteSelectionGuard.FindSelector(box) is { ItemCount: > 5 } selector)
            {
                selector.SelectedIndex = 5;
            }

            while (items.Count > 2)
            {
                items.RemoveAt(items.Count - 1);
            }

            AutoCompleteSelectionGuard.AlignForItemsSource(items);
            box.IsDropDownOpen = false;

            Assert.False(box.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RefreshVisibleOptions_then_close_does_not_throw()
    {
        var catalog = Enumerable.Range(0, 12)
            .Select(i => new OptionItem($"drug-{i}", $"药品{i}"))
            .ToList();
        var visible = new ObservableCollection<OptionItem>(catalog);

        var box = new AutoCompleteBox
        {
            ItemsSource = visible,
            Width = 280,
            Height = 32,
        };
        AutoCompleteFilter.AttachDrugOptionFilter(box);

        var window = new Window
        {
            Width = 420,
            Height = 320,
            Content = box,
        };
        window.Show();
        try
        {
            box.Text = "药";
            box.IsDropDownOpen = true;

            if (AutoCompleteSelectionGuard.FindSelector(box) is { ItemCount: > 3 } selector)
            {
                selector.SelectedIndex = 3;
            }

            AutoCompleteFilter.RefreshVisibleOptions(visible, catalog, "药品1");
            box.IsDropDownOpen = false;
            Assert.False(box.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }
}
