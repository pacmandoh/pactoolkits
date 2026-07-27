using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Common;
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
    public void Sticky_module_tabs_share_selected_item_with_the_page_tabs()
    {
        var model = new ModuleTabsModel();
        var source = new TabControl { DataContext = model };
        var sticky = SettingsScroll.CloneHeaderTabs(source);

        sticky.SelectedItem = model.ModuleEditors[1];
        Assert.Same(model.ModuleEditors[1], model.SelectedModuleEditor);

        model.SelectedModuleEditor = model.ModuleEditors[0];
        Assert.Same(model.ModuleEditors[0], sticky.SelectedItem);
    }

    [AvaloniaFact]
    public void Sticky_h1_content_is_reused_when_a_lower_header_changes()
    {
        var model = new ModuleTabsModel();
        var tabs = new TabControl { DataContext = model };
        tabs.Classes.Add("SettingsHeaderTabs");
        var h1Source = new Border { Child = tabs };
        var firstH2Source = new Border();
        var secondH2Source = new Border();
        var stickyHost = new StackPanel();

        SettingsScroll.SyncStickyChildren(stickyHost,
        [
            new SettingsScroll.HeaderSnapshot(1, "模块配置", ["H1Text"], 0, h1Source, false),
            new SettingsScroll.HeaderSnapshot(2, "Injector", ["H2Text"], 0, firstH2Source, false)
        ]);
        var h1Content = Assert.IsType<Border>(stickyHost.Children[0]).Child;

        SettingsScroll.SyncStickyChildren(stickyHost,
        [
            new SettingsScroll.HeaderSnapshot(1, "模块配置", ["H1Text"], 0, h1Source, false),
            new SettingsScroll.HeaderSnapshot(2, "Scanner", ["H2Text"], 0, secondH2Source, false)
        ]);

        Assert.Same(h1Content, Assert.IsType<Border>(stickyHost.Children[0]).Child);
    }

    private static void Arrange(Control control, double width)
    {
        var size = new Size(width, 80);
        control.Measure(size);
        control.Arrange(new Rect(size));
    }

    private sealed class ModuleTabsModel : INotifyPropertyChanged
    {
        private object? _selectedModuleEditor;

        public ModuleTabsModel()
        {
            ModuleEditors = [new object(), new object()];
            _selectedModuleEditor = ModuleEditors[0];
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<object> ModuleEditors { get; }

        public object? SelectedModuleEditor
        {
            get => _selectedModuleEditor;
            set
            {
                if (ReferenceEquals(_selectedModuleEditor, value))
                {
                    return;
                }

                _selectedModuleEditor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedModuleEditor)));
            }
        }
    }
}
