using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Tests;

public sealed class SettingsStickyTests
{
    private static SettingsScroll.HeaderPosition H1(double top, double height = 52)
        => new(1, top, height);

    private static SettingsScroll.HeaderPosition H2(double top, double height = 44)
        => new(2, top, height);

    private static SettingsScroll.HeaderPosition H3(double top, double height = 36)
        => new(3, top, height);

    [Fact]
    public void H1_is_inactive_at_zero_offset()
    {
        var active = SettingsScroll.SelectActive([H1(0)], [52, 44, 36]);

        Assert.Empty(active);
    }

    [Fact]
    public void H1_activates_after_crossing_viewport_edge()
    {
        var active = SettingsScroll.SelectActive([H1(-1)], [52, 44, 36]);

        Assert.Equal([0], active);
    }

    [Fact]
    public void H2_waits_until_sticky_bottom_reaches_content_mid()
    {
        var active = SettingsScroll.SelectActive([
            H1(-200),
            H2(-100),
            H2(75)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void H2_replaces_peer_after_sticky_bottom_reaches_content_mid()
    {
        var active = SettingsScroll.SelectActive([
            H1(-200),
            H2(-100),
            H2(74)
        ], [52, 44, 36]);

        Assert.Equal([0, 2], active);
    }

    [Fact]
    public void H2_activates_below_sticky_h1()
    {
        var active = SettingsScroll.SelectActive([
            H1(-80),
            H2(51)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void H3_activates_below_sticky_h1_and_h2()
    {
        var active = SettingsScroll.SelectActive([
            H1(-200),
            H2(-100),
            H3(95)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 2], active);
    }

    [Fact]
    public void Next_h1_clears_children_before_reaching_its_slot()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H1(52)
        ], [52, 44, 36]);

        Assert.Equal([0], active);
    }

    [Fact]
    public void Next_h1_replaces_the_active_hierarchy_after_crossing_viewport_edge()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H1(-1)
        ], [52, 44, 36]);

        Assert.Equal([3], active);
    }

    [Fact]
    public void Next_h2_waits_until_reaching_its_slot()
    {
        var active = SettingsScroll.SelectActive([
            H1(-200),
            H2(-100),
            H2(96)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void Next_h2_waits_before_half_obscuring_sticky_h3()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H2(120)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 2], active);
    }

    [Fact]
    public void Next_h2_clears_sticky_h3_after_half_obscured()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H2(114)
        ], [52, 44, 36]);

        Assert.Equal([0, 1], active);
    }

    [Fact]
    public void Next_h2_replaces_peer_after_half_h2_slot()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H2(74)
        ], [52, 44, 36]);

        Assert.Equal([0, 3], active);
    }

    [Fact]
    public void Next_h3_waits_until_reaching_its_slot()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H3(132)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 2], active);
    }

    [Fact]
    public void Next_h3_waits_above_content_mid()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H3(115)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 2], active);
    }

    [Fact]
    public void Next_h3_replaces_peer_after_sticky_bottom_reaches_content_mid()
    {
        var active = SettingsScroll.SelectActive([
            H1(-300),
            H2(-200),
            H3(-100),
            H3(114)
        ], [52, 44, 36]);

        Assert.Equal([0, 1, 3], active);
    }

    [Fact]
    public void Select_active_rejects_unknown_header_level()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(4, 0, 36)
        ], [52, 44, 36]));
    }

    [Fact]
    public void Merge_slot_heights_keeps_temporarily_missing_level()
    {
        var merged = SettingsScroll.MergeSlotHeights([52, 44, 0], [52, 44, 36]);

        Assert.Equal([52, 44, 36], merged);
    }

    [Fact]
    public void Merge_slot_heights_does_not_shrink()
    {
        var merged = SettingsScroll.MergeSlotHeights([52, 44, 36], [60, 44, 36]);

        Assert.Equal([60, 44, 36], merged);
    }
}
