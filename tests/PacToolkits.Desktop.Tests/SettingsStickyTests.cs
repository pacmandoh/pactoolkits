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
    public void New_h2_replaces_peer_and_clears_old_h3()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(2, 132)
        ], [52, 44, 36]);

        Assert.Equal([0, 3], active);
    }

    [Fact]
    public void New_h2_waits_below_old_h3_stack_bottom()
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
    public void New_h1_replaces_entire_previous_stack()
    {
        var active = SettingsScroll.SelectActive([
            new SettingsScroll.HeaderPosition(1, -300),
            new SettingsScroll.HeaderPosition(2, -200),
            new SettingsScroll.HeaderPosition(3, -100),
            new SettingsScroll.HeaderPosition(1, 132)
        ], [52, 44, 36]);

        Assert.Equal([3], active);
    }
}
