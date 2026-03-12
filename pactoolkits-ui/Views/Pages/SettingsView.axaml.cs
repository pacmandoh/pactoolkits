using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;
using pactoolkits_ui.ViewModels.Pages;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.Views.Pages;

public partial class SettingsView : UserControl
{
    public static readonly StyledProperty<bool> IsClientAliasEditableProperty =
        AvaloniaProperty.Register<SettingsView, bool>(nameof(IsClientAliasEditable), false);

    public static readonly StyledProperty<bool> IsClientAliasReadOnlyProperty =
        AvaloniaProperty.Register<SettingsView, bool>(nameof(IsClientAliasReadOnly), true);

    public bool IsClientAliasEditable
    {
        get => GetValue(IsClientAliasEditableProperty);
        set => SetValue(IsClientAliasEditableProperty, value);
    }

    public bool IsClientAliasReadOnly
    {
        get => GetValue(IsClientAliasReadOnlyProperty);
        set => SetValue(IsClientAliasReadOnlyProperty, value);
    }

    private SettingsViewModel? _vm;
    private ScrollViewer? _contentScrollViewer;
    private Grid? _settingsSectionsGrid;
    private StackPanel? _navItemsHost;
    private readonly List<SectionLink> _sectionLinks = new();
    private int _activeSectionIndex = -1;
    private bool _isAnimatingScroll;
    private CancellationTokenSource? _scrollAnimationCts;
    private static readonly CubicEaseInOut ScrollEasing = new();
    private static readonly IBrush ActiveBackgroundFallback = new SolidColorBrush(Color.FromRgb(59, 130, 246));
    private static readonly IBrush ActiveForegroundFallback = Brushes.White;
    private static readonly IBrush BorderFallback = new SolidColorBrush(Color.FromRgb(120, 120, 120));
    private static readonly IBrush NormalBackgroundFallback = Brushes.Transparent;
    private static readonly IBrush NormalForegroundFallback = new SolidColorBrush(Color.FromRgb(225, 225, 225));

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnSettingsKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TryAttach(DataContext as SettingsViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryAttach(DataContext as SettingsViewModel);
    }

    private void TryAttach(SettingsViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
            return;

        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = vm;

        if (_vm is not null)
        {
            IsClientAliasReadOnly = _vm.IsClientAliasReadOnly;
            IsClientAliasEditable = !_vm.IsClientAliasReadOnly;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        else
        {
            IsClientAliasReadOnly = true;
            IsClientAliasEditable = false;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsClientAliasReadOnly))
        {
            IsClientAliasReadOnly = _vm?.IsClientAliasReadOnly ?? true;
            IsClientAliasEditable = !IsClientAliasReadOnly;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_contentScrollViewer is not null)
            _contentScrollViewer.ScrollChanged -= OnContentScrollChanged;
        foreach (var link in _sectionLinks)
            link.NavButton.Click -= OnNavButtonClick;

        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _sectionLinks.Clear();
        _contentScrollViewer = null;
        _settingsSectionsGrid = null;
        _navItemsHost = null;
        _activeSectionIndex = -1;
        _scrollAnimationCts?.Cancel();
        _scrollAnimationCts?.Dispose();
        _scrollAnimationCts = null;
        _vm = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryAttach(DataContext as SettingsViewModel);
        InitializeSectionNavigation();
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && e.Key != Key.Enter)
            return;

        var inputs = EnumerateTabInputs().ToList();
        InputFocusHelper.TryHandleTabCycle(this, e, inputs);
    }

    private IEnumerable<Control> EnumerateTabInputs() =>
        InputFocusHelper.EnumerateInputs(this, typeof(TextBox), typeof(NumericUpDown), typeof(ComboBox));

    private async void OnNavButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button navButton || navButton.Tag is null)
            return;

        var sectionIndex = navButton.Tag switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        };

        if (sectionIndex < 0)
            return;

        await SetActiveSectionAsync(sectionIndex, scrollToSection: true);
    }

    private void InitializeSectionNavigation()
    {
        if (_contentScrollViewer is not null && _settingsSectionsGrid is not null && _sectionLinks.Count > 0)
            return;

        _contentScrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
        _settingsSectionsGrid = this.FindControl<Grid>("SettingsSectionsGrid");
        _navItemsHost = this.FindControl<StackPanel>("NavItemsHost");

        if (_contentScrollViewer is null || _settingsSectionsGrid is null || _navItemsHost is null)
            return;

        _navItemsHost.Children.Clear();
        _sectionLinks.Clear();
        BuildSectionsFromAnchors();

        _contentScrollViewer.ScrollChanged -= OnContentScrollChanged;
        _contentScrollViewer.ScrollChanged += OnContentScrollChanged;
        _ = SetActiveSectionAsync(0, scrollToSection: false);
    }

    private void BuildSectionsFromAnchors()
    {
        if (_settingsSectionsGrid is null || _navItemsHost is null)
            return;

        var anchors = _settingsSectionsGrid.Children
            .OfType<Grid>()
            .Where(IsSectionAnchor)
            .OrderBy(Grid.GetRow)
            .ToList();

        var sectionIndex = 0;
        foreach (var anchor in anchors)
        {
            if (!TryCreateNavButtonForAnchor(anchor, sectionIndex, out var navButton))
                continue;

            _navItemsHost.Children.Add(navButton);
            _sectionLinks.Add(new SectionLink(navButton, anchor));
            sectionIndex++;
        }
    }

    private static bool IsSectionAnchor(Grid grid)
    {
        var name = grid.Name;
        return !string.IsNullOrWhiteSpace(name)
               && name.StartsWith("Section", StringComparison.Ordinal)
               && name.EndsWith("Anchor", StringComparison.Ordinal);
    }

    private bool TryCreateNavButtonForAnchor(Grid anchor, int sectionIndex, out Button navButton)
    {
        navButton = null!;

        var sourceHeader = anchor.Children.OfType<StackPanel>().FirstOrDefault();
        var sourceIcon = sourceHeader?.Children.OfType<MaterialIcon>().FirstOrDefault();
        var sourceTitle = sourceHeader?.Children.OfType<TextBlock>().FirstOrDefault(text =>
            text.Classes.Contains("SectionTitle") && !string.IsNullOrWhiteSpace(text.Text));

        if (sourceTitle?.Text is null)
            return false;

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        var icon = new MaterialIcon
        {
            Kind = sourceIcon?.Kind ?? MaterialIconKind.CogOutline,
            Width = 16,
            Height = 16,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);

        var text = new TextBlock
        {
            Text = sourceTitle.Text,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetColumn(text, 1);

        content.Children.Add(icon);
        content.Children.Add(text);

        navButton = new Button
        {
            Content = content,
            Tag = sectionIndex
        };
        navButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        navButton.Classes.Add("Rounded");
        navButton.Classes.Add("SettingsNavItem");
        navButton.Click += OnNavButtonClick;
        ApplyNavButtonVisual(navButton, isActive: false);
        return true;
    }

    private void OnContentScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isAnimatingScroll || _contentScrollViewer is null || _settingsSectionsGrid is null || _sectionLinks.Count == 0)
            return;

        var offsetY = _contentScrollViewer.Offset.Y;
        var extentHeight = Math.Max(0, _contentScrollViewer.Extent.Height);
        var viewportHeight = Math.Max(0, _contentScrollViewer.Viewport.Height);
        var maxOffset = Math.Max(0, extentHeight - viewportHeight);
        var isAtBottom = maxOffset > 0 && offsetY >= maxOffset - 2;

        var activeIndex = _sectionLinks.Count - 1;
        if (!isAtBottom)
        {
            var minDistance = double.MaxValue;
            for (var i = 0; i < _sectionLinks.Count; i++)
            {
                var distance = Math.Abs(GetAnchorTop(_sectionLinks[i].Anchor) - offsetY);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    activeIndex = i;
                }
            }
        }

        _ = SetActiveSectionAsync(activeIndex, scrollToSection: false);
    }

    private double GetAnchorTop(Control anchor)
    {
        if (_settingsSectionsGrid is null)
            return 0;

        var origin = anchor.TranslatePoint(default, _settingsSectionsGrid);
        return origin?.Y ?? 0;
    }

    private async Task SetActiveSectionAsync(int index, bool scrollToSection)
    {
        if (_contentScrollViewer is null || _sectionLinks.Count == 0)
            return;

        if (index < 0 || index >= _sectionLinks.Count)
            return;

        if (_activeSectionIndex != index)
        {
            for (var i = 0; i < _sectionLinks.Count; i++)
            {
                var navButton = _sectionLinks[i].NavButton;
                if (i == index)
                {
                    if (!navButton.Classes.Contains("Active"))
                        navButton.Classes.Add("Active");
                    ApplyNavButtonVisual(navButton, isActive: true);
                }
                else if (navButton.Classes.Contains("Active"))
                {
                    navButton.Classes.Remove("Active");
                    ApplyNavButtonVisual(navButton, isActive: false);
                }
                else
                {
                    ApplyNavButtonVisual(navButton, isActive: false);
                }
            }

            _activeSectionIndex = index;
        }

        if (!scrollToSection)
            return;

        var targetTop = GetAnchorTop(_sectionLinks[index].Anchor);
        await AnimateScroll(targetTop);
    }

    private async Task AnimateScroll(double desiredScroll)
    {
        if (_contentScrollViewer is null)
            return;

        _scrollAnimationCts?.Cancel();
        _scrollAnimationCts?.Dispose();
        _scrollAnimationCts = new CancellationTokenSource();
        var token = _scrollAnimationCts.Token;
        _isAnimatingScroll = true;

        try
        {
            var startOffset = _contentScrollViewer.Offset.Y;
            var targetOffset = Math.Max(0, desiredScroll - 30);
            var duration = TimeSpan.FromMilliseconds(800);
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < duration && !token.IsCancellationRequested)
            {
                var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
                var eased = ScrollEasing.Ease(progress);
                var y = startOffset + ((targetOffset - startOffset) * eased);
                _contentScrollViewer.Offset = new Vector(_contentScrollViewer.Offset.X, y);
                await Task.Delay(16, token);
            }

            if (!token.IsCancellationRequested)
                _contentScrollViewer.Offset = new Vector(_contentScrollViewer.Offset.X, targetOffset);
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            _isAnimatingScroll = false;
        }
    }

    private void ApplyNavButtonVisual(Button navButton, bool isActive)
    {
        var activeBackground = ResolveBrush("SukiPrimaryColor", ActiveBackgroundFallback);
        var activeForeground = ResolveBrush("SukiPrimaryForeground", ActiveForegroundFallback);
        var normalBackground = ResolveBrush("SukiBackground", NormalBackgroundFallback);
        var normalForeground = ResolveBrush("SukiText", NormalForegroundFallback);
        var borderBrush = ResolveBrush("SukiControlBorderBrush", BorderFallback);

        navButton.BorderBrush = borderBrush;
        navButton.BorderThickness = new Thickness(1.2);

        if (isActive)
        {
            navButton.Background = activeBackground;
            navButton.Foreground = activeForeground;
            return;
        }

        navButton.Background = normalBackground;
        navButton.Foreground = normalForeground;
    }

    private IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var resource) == true)
        {
            if (resource is IBrush brush)
                return brush;
            if (resource is Color color)
                return new SolidColorBrush(color);
        }

        return fallback;
    }

    private sealed record SectionLink(Button NavButton, Control Anchor);
}
