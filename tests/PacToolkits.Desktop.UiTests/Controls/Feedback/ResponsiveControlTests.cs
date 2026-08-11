using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.UiTests;

public sealed class ResponsiveControlTests
{
    [AvaloniaFact]
    public void Data_grid_pager_progressively_hides_low_priority_content()
    {
        var pager = new DataGridPager
        {
            SelectedCount = 3,
            TotalCount = 57,
        };

        Arrange(pager, 700);
        Assert.True(pager.ShowResponsivePageSizeSection);
        Assert.True(pager.ShowResponsivePageSummary);

        Arrange(pager, 500);
        Assert.False(pager.ShowResponsivePageSizeSection);
        Assert.True(pager.ShowResponsivePageSummary);

        Arrange(pager, 320);
        Assert.False(pager.ShowResponsivePageSizeSection);
        Assert.False(pager.ShowResponsivePageSummary);
        Assert.True(pager.ShowSelectionSummary);
        Assert.Equal("已选择 3 / 57 行", pager.SelectionSummaryText);

        Arrange(pager, 700);
        Assert.True(pager.ShowResponsivePageSizeSection);
        Assert.True(pager.ShowResponsivePageSummary);
    }

    [AvaloniaFact]
    public void Responsive_card_panel_tracks_compact_and_minimal_widths()
    {
        var card = new CardPanel { IsResponsive = true };

        Arrange(card, 800);
        Assert.DoesNotContain("ResponsiveCompact", card.Classes);
        Assert.DoesNotContain("ResponsiveMinimal", card.Classes);

        Arrange(card, 600);
        Assert.Contains("ResponsiveCompact", card.Classes);
        Assert.DoesNotContain("ResponsiveMinimal", card.Classes);

        Arrange(card, 380);
        Assert.Contains("ResponsiveCompact", card.Classes);
        Assert.Contains("ResponsiveMinimal", card.Classes);

        Arrange(card, 800);
        Assert.DoesNotContain("ResponsiveCompact", card.Classes);
        Assert.DoesNotContain("ResponsiveMinimal", card.Classes);
    }

    [AvaloniaFact]
    public void Status_pill_can_collapse_to_icon_without_losing_its_text_value()
    {
        var pill = new StatusPill { Text = "待执行 37", ShowText = false };

        Assert.False(pill.ShowTextContent);
        Assert.Equal("待执行 37", pill.Text);
    }

    [AvaloniaFact]
    public void Runtime_state_icon_exposes_one_visual_state_at_a_time()
    {
        var icon = new RuntimeStateIcon();

        Assert.False(icon.IsActive);
        Assert.False(icon.IsTransitioning);
        Assert.True(icon.IsInactive);

        icon.State = RuntimeVisualState.Transitioning;
        Assert.False(icon.IsActive);
        Assert.True(icon.IsTransitioning);
        Assert.False(icon.IsInactive);

        icon.State = RuntimeVisualState.Active;
        Assert.True(icon.IsActive);
        Assert.False(icon.IsTransitioning);
        Assert.False(icon.IsInactive);
    }

    [AvaloniaFact]
    public void Sticky_module_tabs_share_selection_with_source_tabs()
    {
        var items = new object[] { new(), new() };
        var source = new TabControl
        {
            ItemsSource = items,
            SelectedItem = items[0]
        };
        var sticky = SettingsScroll.CloneHeaderTabs(source);

        sticky.SelectedItem = items[1];
        Assert.Same(items[1], source.SelectedItem);

        source.SelectedItem = items[0];
        Assert.Same(items[0], sticky.SelectedItem);
    }

    [AvaloniaFact]
    public void Removed_sticky_row_releases_tab_synchronization()
    {
        var items = new object[] { new(), new() };
        var sourceTabs = new TabControl
        {
            ItemsSource = items,
            SelectedItem = items[0]
        };
        var sourceHeader = new Border { Child = sourceTabs };
        var rows = new StackPanel();

        SettingsScroll.SyncStickyRows(rows,
        [
            new SettingsScroll.Header(1, 0, sourceHeader)
        ]);

        var stickyTabs = Assert.IsType<Border>(rows.Children[0])
            .GetVisualDescendants()
            .OfType<TabControl>()
            .Single();
        SettingsScroll.SyncStickyRows(rows, []);

        sourceTabs.SelectedItem = items[1];
        Assert.Null(stickyTabs.SelectedItem);

        sourceTabs.SelectedItem = items[0];
        stickyTabs.SelectedItem = items[1];
        Assert.Same(items[0], sourceTabs.SelectedItem);
    }

    [AvaloniaFact]
    public void Sticky_parent_row_is_reused_when_child_header_changes()
    {
        var h1 = new Border();
        var firstH2 = new Border();
        var secondH2 = new Border();
        var rows = new StackPanel();

        SettingsScroll.SyncStickyRows(rows,
        [
            new SettingsScroll.Header(1, 0, h1),
            new SettingsScroll.Header(2, 0, firstH2)
        ]);
        var h1Row = rows.Children[0];

        SettingsScroll.SyncStickyRows(rows,
        [
            new SettingsScroll.Header(1, 0, h1),
            new SettingsScroll.Header(2, 0, secondH2)
        ]);

        Assert.Same(h1Row, rows.Children[0]);
        Assert.Equal(2, rows.Children.Count);
    }

    private static void Arrange(Control control, double width)
    {
        var size = new Size(width, 80);
        control.Measure(size);
        control.Arrange(new Rect(size));
    }

}
