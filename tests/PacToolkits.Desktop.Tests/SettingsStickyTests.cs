using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Tests;

public sealed class SettingsStickyTests
{
    [Fact]
    public void Sticky_title_keeps_source_typography_classes()
    {
        var classes = SettingsScroll.SelectTitleClasses(["Large", "H2Text", ":pointerover"]);

        Assert.Equal(["Large", "H2Text"], classes);
    }

    [Fact]
    public void H1_activates_when_title_reaches_viewport_edge()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, 0)
        ], [52, 44, 36]);

        Assert.Equal([0], active);
    }

    [Fact]
    public void H1_waits_while_title_is_visible_below_viewport_edge()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, 1)
        ], [52, 44, 36]);

        Assert.Empty(active);
    }

    [Fact]
    public void H2_activates_when_title_reaches_h1_bottom()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -80),
            new SettingsScroll.HeaderPosition(2, 52)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void Next_h2_replaces_peer_when_title_reaches_current_stack_bottom()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -200),
            new SettingsScroll.HeaderPosition(2, -100),
            new SettingsScroll.HeaderPosition(2, 96)
        ], [52, 44, 36]);

        Assert.Equal([0, 2], active);
    }

    [Fact]
    public void Next_h2_waits_below_current_stack_bottom()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -200),
            new SettingsScroll.HeaderPosition(2, -100),
            new SettingsScroll.HeaderPosition(2, 97)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void H3_activates_when_title_reaches_parent_stack_bottom()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -200),
            new SettingsScroll.HeaderPosition(2, -100),
            new SettingsScroll.HeaderPosition(3, 96)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 2], active);
    }

    [Fact]
    public void Next_h3_replaces_peer_when_title_reaches_current_stack_bottom()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(3, 132)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 3], active);
    }

    [Fact]
    public void Next_h2_touching_sticky_h3_clears_h3_but_keeps_current_h2()
    {
        // 位于 H3 区间时只应取消 H3，不得切换 H2
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(2, 100)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void Next_h2_at_full_stack_bottom_still_keeps_current_h2_while_above_peer_slot()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(2, 132)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void Next_h2_peer_push_works_while_old_h3_header_is_still_in_document()
    {
        // 旧 H3 可能在同一轮重新激活，但相邻标题在 H1 与 H2 边界处仍应优先推出它
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(2, 96)
        ], [52, 44, 36]);

        Assert.Equal([0, 3], active);
    }

    [Fact]
    public void Next_h2_still_waits_above_h2_slot_after_h3_was_cleared()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(2, 97)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void Next_h2_replaces_current_h2_at_normal_peer_push_after_h3_cleared()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(2, 96)
        ], [52, 44, 36]);

        Assert.Equal([0, 2], active);
    }

    [Fact]
    public void Next_h2_above_full_stack_keeps_h2_and_h3()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(2, 133)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 2], active);
    }

    [Fact]
    public void New_h1_in_child_band_clears_children_but_keeps_current_h1()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(1, 80)
        ], [52, 44, 36]);

        Assert.Equal([0], active);
    }

    [Fact]
    public void New_h1_peer_push_works_while_old_children_headers_are_still_in_document()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(1, 52)
        ], [52, 44, 36]);

        Assert.Equal([3], active);
    }

    [Fact]
    public void New_h1_replaces_at_normal_peer_push_after_children_cleared()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(1, 52)
        ], [52, 44, 36]);

        Assert.Equal([1], active);
    }

    [Fact]
    public void Merge_slot_heights_keeps_last_h3_when_row_is_temporarily_gone()
    {
        var merged = SettingsScroll.MergeSlotHeights([52, 44, 0], [52, 44, 36]);

        Assert.Equal([52, 44, 36], merged);
    }

    [Fact]
    public void Merge_slot_heights_does_not_shrink_when_shorter_header_is_measured()
    {
        var merged = SettingsScroll.MergeSlotHeights([52, 44, 36], [52, 60, 36]);

        Assert.Equal([52, 60, 36], merged);
    }
}
