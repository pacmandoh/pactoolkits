using Avalonia;
using Avalonia.Controls;
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

    private static void Arrange(Control control, double width)
    {
        var size = new Size(width, 80);
        control.Measure(size);
        control.Arrange(new Rect(size));
    }
}
