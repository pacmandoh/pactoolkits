using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 使用独立覆盖层呈现设置页 H1/H2/H3 分级吸顶标题
/// </summary>
public static class SettingsScroll
{
    private const double EdgeTolerance = 0.5;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SettingsScroll));

    private static readonly AttachedProperty<bool> IsHookedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsHooked", typeof(SettingsScroll));

    private static readonly AttachedProperty<bool> LayoutRefreshPendingProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("LayoutRefreshPending", typeof(SettingsScroll));

    private static readonly AttachedProperty<Border?> OverlayHostProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Border?>("OverlayHost", typeof(SettingsScroll));

    private static readonly AttachedProperty<IReadOnlyList<Control>?> ActiveHeadersProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, IReadOnlyList<Control>?>("ActiveHeaders", typeof(SettingsScroll));

    private static readonly AttachedProperty<Control?> StickyRowSourceProperty =
        AvaloniaProperty.RegisterAttached<Border, Control?>("StickyRowSource", typeof(SettingsScroll));

    private static readonly AttachedProperty<CloneLifetime?> CloneLifetimeProperty =
        AvaloniaProperty.RegisterAttached<Control, CloneLifetime?>("CloneLifetime", typeof(SettingsScroll));

    private static readonly AttachedProperty<double[]> LastSlotHeightsProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double[]>("LastSlotHeights", typeof(SettingsScroll));

    static SettingsScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer scrollViewer) => scrollViewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer scrollViewer, bool value)
        => scrollViewer.SetValue(IsEnabledProperty, value);

    public static void ResetSticky(ScrollViewer scrollViewer) => ResetOverlay(scrollViewer, detach: false);

    public static void ScheduleRefresh(ScrollViewer scrollViewer)
    {
        if (scrollViewer.Content is Control contentRoot
            && scrollViewer.IsVisible
            && scrollViewer.Bounds.Height > 0
            && contentRoot.Bounds.Height > 0)
        {
            RefreshSticky(scrollViewer);
            return;
        }

        if (!scrollViewer.GetValue(LayoutRefreshPendingProperty))
        {
            scrollViewer.LayoutUpdated += OnLayoutUpdated;
            scrollViewer.SetValue(LayoutRefreshPendingProperty, true);
        }
    }

    internal static IReadOnlyList<int> SelectActive(
        IReadOnlyList<HeaderPosition> headers,
        IReadOnlyList<double> slotHeights)
    {
        if (slotHeights.Count != 3)
        {
            throw new ArgumentException("Slot heights must contain H1, H2, and H3.", nameof(slotHeights));
        }

        var active = new int?[3];

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var level = header.Level - 1;
            if ((uint)level >= active.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(headers), header.Level, "Header level must be between H1 and H3.");
            }

            if (level > 0 && active[level - 1] is null)
            {
                continue;
            }

            var parentEdge = 0d;
            for (var slot = 0; slot < level; slot++)
            {
                if (active[slot] is not null)
                {
                    parentEdge += slotHeights[slot];
                }
            }

            if (level == 0 && active[level] is not null)
            {
                var occupiedEdge = parentEdge;
                for (var slot = 0; slot < active.Length; slot++)
                {
                    if (active[slot] is not null)
                    {
                        occupiedEdge += slotHeights[slot];
                    }
                }

                if (header.Top <= occupiedEdge + EdgeTolerance)
                {
                    active[1] = active[2] = null;
                }
            }
            else if (active[level] is not null)
            {
                var childSlotTop = parentEdge + slotHeights[level];
                for (var child = level + 1; child < active.Length; child++)
                {
                    if (active[child] is null)
                    {
                        continue;
                    }

                    var childSlotBottom = childSlotTop + slotHeights[child];
                    if (header.Top <= childSlotBottom - slotHeights[child] / 2d + EdgeTolerance)
                    {
                        active[child] = null;
                    }

                    childSlotTop = childSlotBottom;
                }
            }

            var activates = active[level] is not null && level > 0
                ? header.Top + header.Height / 2d <= parentEdge + slotHeights[level] + EdgeTolerance
                : header.Top < parentEdge - EdgeTolerance;

            if (activates)
            {
                active[level] = i;
                for (var child = level + 1; child < active.Length; child++)
                {
                    active[child] = null;
                }
            }
        }

        return active.Where(static index => index is not null).Select(static index => index!.Value).ToList();
    }

    internal static double[] MergeSlotHeights(IReadOnlyList<double> measured, IReadOnlyList<double> lastKnown)
    {
        if (measured.Count != 3)
        {
            throw new ArgumentException("Measured heights must contain H1, H2, and H3 slots.", nameof(measured));
        }

        if (lastKnown.Count != 3)
        {
            throw new ArgumentException("Last-known heights must contain H1, H2, and H3 slots.", nameof(lastKnown));
        }

        var merged = new double[3];
        for (var i = 0; i < merged.Length; i++)
        {
            merged[i] = measured[i] > 0
                ? Math.Max(measured[i], lastKnown[i])
                : lastKnown[i];
        }

        return merged;
    }

    internal static void SyncStickyRows(StackPanel rows, IReadOnlyList<Header> active)
    {
        while (rows.Children.Count > active.Count)
        {
            DisposeRow(rows.Children[^1]);
            rows.Children.RemoveAt(rows.Children.Count - 1);
        }

        for (var index = 0; index < active.Count; index++)
        {
            var header = active[index];
            if (index < rows.Children.Count
                && rows.Children[index] is Border existing
                && ReferenceEquals(existing.GetValue(StickyRowSourceProperty), header.Source))
            {
                continue;
            }

            var row = new Border { Child = BuildRow(header.Source, header.Level) };
            row.SetValue(StickyRowSourceProperty, header.Source);
            row.Classes.Add("StickyRow");
            row.Classes.Add(header.Level switch
            {
                1 => "H1",
                2 => "H2",
                _ => "H3"
            });

            if (index < rows.Children.Count)
            {
                DisposeRow(rows.Children[index]);
                rows.Children[index] = row;
            }
            else
            {
                rows.Children.Add(row);
            }
        }
    }

    internal static TabControl CloneHeaderTabs(TabControl source)
    {
        var lifetime = new CloneLifetime();
        var tabs = new TabControl();
        tabs.SetValue(CloneLifetimeProperty, lifetime);
        foreach (var @class in source.Classes.Where(static c => c.Length > 0 && c[0] != ':'))
        {
            tabs.Classes.Add(@class);
        }

        Mirror(tabs, source, StyledElement.DataContextProperty, lifetime);
        Mirror(tabs, source, ItemsControl.ItemsSourceProperty, lifetime);
        Mirror(tabs, source, ItemsControl.ItemTemplateProperty, lifetime);
        Mirror(tabs, source, SelectingItemsControl.SelectedItemProperty, lifetime);
        EventHandler<SelectionChangedEventArgs> selectionChanged = (_, _) =>
        {
            if (!ReferenceEquals(source.SelectedItem, tabs.SelectedItem))
            {
                source.SelectedItem = tabs.SelectedItem;
            }
        };
        tabs.SelectionChanged += selectionChanged;
        lifetime.Add(() => tabs.SelectionChanged -= selectionChanged);
        Mirror(tabs, source, Visual.IsVisibleProperty, lifetime);
        Mirror(tabs, source, InputElement.IsEnabledProperty, lifetime);
        tabs.SetValue(TabControlBehaviors.UseSlidingPillProperty, false);
        return tabs;
    }

    internal readonly record struct HeaderPosition(int Level, double Top, double Height);

    internal readonly record struct Header(int Level, double Top, Control Source);

    private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        SetHooked(scrollViewer, e.NewValue is true);
        if (e.NewValue is not true)
        {
            ResetOverlay(scrollViewer, detach: true);
        }
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            SetHooked(scrollViewer, false);
            ResetOverlay(scrollViewer, detach: true);
        }
    }

    private static void SetHooked(ScrollViewer scrollViewer, bool hooked)
    {
        if (scrollViewer.GetValue(IsHookedProperty) == hooked)
        {
            return;
        }

        scrollViewer.ScrollChanged -= OnScrollChanged;
        scrollViewer.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        if (hooked)
        {
            scrollViewer.ScrollChanged += OnScrollChanged;
            scrollViewer.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        scrollViewer.SetValue(IsHookedProperty, hooked);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            RefreshSticky(scrollViewer);
        }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || scrollViewer.Content is not Control contentRoot
            || !scrollViewer.IsVisible
            || scrollViewer.Bounds.Height <= 0
            || contentRoot.Bounds.Height <= 0)
        {
            return;
        }

        scrollViewer.LayoutUpdated -= OnLayoutUpdated;
        scrollViewer.SetValue(LayoutRefreshPendingProperty, false);
        RefreshSticky(scrollViewer);
    }

    private static void RefreshSticky(ScrollViewer scrollViewer)
    {
        if (scrollViewer.Content is not Control contentRoot
            || !scrollViewer.IsVisible
            || scrollViewer.Bounds.Height <= 0
            || contentRoot.Bounds.Height <= 0)
        {
            return;
        }

        var tabPage = scrollViewer.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(static c => c.Classes.Contains("TabPage"));
        if (!(tabPage?.IsVisible ?? scrollViewer.IsVisible))
        {
            return;
        }

        var headers = CollectHeaders(contentRoot, scrollViewer)
            .OrderBy(static header => header.Top)
            .ToList();

        var measured = new double[3];
        foreach (var header in headers)
        {
            measured[header.Level - 1] = Math.Max(measured[header.Level - 1], header.Source.Bounds.Height);
        }

        var slotHeights = MergeSlotHeights(
            measured,
            scrollViewer.GetValue(LastSlotHeightsProperty) ?? new double[3]);
        scrollViewer.SetValue(LastSlotHeightsProperty, slotHeights);

        var activeIndexes = SelectActive(
            headers.Select(static h => new HeaderPosition(h.Level, h.Top, h.Source.Bounds.Height)).ToList(),
            slotHeights);
        var active = activeIndexes.Select(index => headers[index]).ToList();
        var previous = scrollViewer.GetValue(ActiveHeadersProperty) ?? [];

        var host = scrollViewer.GetValue(OverlayHostProperty);
        if (host is { Parent: null })
        {
            scrollViewer.ClearValue(OverlayHostProperty);
            host = null;
        }

        if (host is null)
        {
            if (tabPage is not Grid page)
            {
                return;
            }

            host = new Border
            {
                Child = new StackPanel(),
                IsVisible = false
            };
            host.Classes.Add("StickyHost");
            page.Children.Add(host);
            scrollViewer.SetValue(OverlayHostProperty, host);
        }

        if (host.Child is not StackPanel rows)
        {
            return;
        }

        var margin = contentRoot.Margin;
        host.Margin = new Thickness(margin.Left, 0, margin.Right, 0);
        host.Padding = new Thickness(0, margin.Top, 0, 0);

        if (active.Count == 0)
        {
            foreach (var row in rows.Children)
            {
                DisposeRow(row);
            }

            rows.Children.Clear();
            scrollViewer.SetValue(ActiveHeadersProperty, []);
            host.IsVisible = false;
            host.IsHitTestVisible = false;
            return;
        }

        var activeSources = active.Select(static h => h.Source).ToList();
        var allowHitTest = activeSources.Any(static source =>
            source.GetVisualDescendants().OfType<StackPanel>().Any(static p => p.Classes.Contains("H2Actions"))
            || source.GetVisualDescendants().OfType<TabControl>().Any());

        if (previous.SequenceEqual(activeSources))
        {
            host.IsVisible = true;
            host.IsHitTestVisible = allowHitTest;
            return;
        }

        scrollViewer.SetValue(ActiveHeadersProperty, activeSources);
        SyncStickyRows(rows, active);
        host.IsVisible = true;
        host.IsHitTestVisible = allowHitTest;
    }

    private static void ResetOverlay(ScrollViewer scrollViewer, bool detach)
    {
        if (scrollViewer.GetValue(LayoutRefreshPendingProperty))
        {
            scrollViewer.LayoutUpdated -= OnLayoutUpdated;
            scrollViewer.SetValue(LayoutRefreshPendingProperty, false);
        }

        scrollViewer.ClearValue(LastSlotHeightsProperty);
        scrollViewer.ClearValue(ActiveHeadersProperty);

        if (scrollViewer.GetValue(OverlayHostProperty) is not { } host)
        {
            return;
        }

        if (host.Child is StackPanel rows && rows.Children.Count > 0)
        {
            foreach (var row in rows.Children)
            {
                DisposeRow(row);
            }

            rows.Children.Clear();
        }

        if (detach)
        {
            if (host.Parent is Panel parent)
            {
                parent.Children.Remove(host);
            }

            scrollViewer.ClearValue(OverlayHostProperty);
            return;
        }

        host.IsVisible = false;
        host.IsHitTestVisible = false;
    }

    private static Control BuildRow(Control source, int level)
    {
        var nodes = source.GetVisualDescendants().ToList();
        var titleBlock = nodes
            .OfType<TextBlock>()
            .FirstOrDefault(static block =>
                block.Classes.Contains("H1Text")
                || block.Classes.Contains("H2Text")
                || block.Classes.Contains("H3Text"));
        var title = new TextBlock
        {
            Text = titleBlock?.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var @class in (titleBlock?.Classes ?? []).Where(static c => c.Length > 0 && c[0] != ':'))
        {
            title.Classes.Add(@class);
        }

        Control titleContent = title;
        var pills = nodes.OfType<StatusPill>().ToList();
        if (level is 2 or 3 || pills.Count > 0)
        {
            var titleRow = new StackPanel { Classes = { level == 2 ? "H2Title" : "H3Title" } };
            if (level is 2 or 3)
            {
                titleRow.Children.Add(new AppIcon
                {
                    Kind = "Hash",
                    Classes = { level == 2 ? "H2Hash" : "H3Hash" }
                });
            }

            titleRow.Children.Add(title);
            foreach (var pill in pills)
            {
                titleRow.Children.Add(ClonePill(pill));
            }

            titleContent = titleRow;
        }

        var sourceTabs = nodes.OfType<TabControl>().FirstOrDefault();
        if (sourceTabs is not null)
        {
            var rail = new StackPanel
            {
                Classes = { "HeaderTitleRail" },
                Orientation = Orientation.Horizontal
            };
            rail.Children.Add(titleContent);
            rail.Children.Add(new AppIcon { Classes = { "HeaderTitleChevron" }, Kind = "ChevronRight" });
            rail.Children.Add(CloneHeaderTabs(sourceTabs));
            titleContent = rail;
        }

        var buttons = nodes
            .OfType<StackPanel>()
            .Where(static panel => panel.Classes.Contains("H2Actions"))
            .SelectMany(static panel => panel.Children.OfType<Button>())
            .ToList();
        if (buttons.Count == 0)
        {
            return titleContent;
        }

        var grid = new Grid
        {
            Classes = { "H2Row" },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        Grid.SetColumn(titleContent, 0);
        grid.Children.Add(titleContent);

        var actions = new StackPanel { Classes = { "H2Actions" } };
        Grid.SetColumn(actions, 1);
        foreach (var button in buttons)
        {
            actions.Children.Add(CloneButton(button));
        }

        grid.Children.Add(actions);
        return grid;
    }

    private static StatusPill ClonePill(StatusPill source)
    {
        var lifetime = new CloneLifetime();
        var pill = new StatusPill { VerticalAlignment = VerticalAlignment.Center };
        pill.SetValue(CloneLifetimeProperty, lifetime);
        foreach (var @class in source.Classes.Where(static c => c.Length > 0 && c[0] != ':'))
        {
            pill.Classes.Add(@class);
        }

        Mirror(pill, source, StatusPill.IconProperty, lifetime);
        Mirror(pill, source, StatusPill.TextProperty, lifetime);
        Mirror(pill, source, Visual.IsVisibleProperty, lifetime);
        Mirror(pill, source, InputElement.IsEnabledProperty, lifetime);
        return pill;
    }

    private static Button CloneButton(Button source)
    {
        var lifetime = new CloneLifetime();
        var button = new Button();
        button.SetValue(CloneLifetimeProperty, lifetime);
        foreach (var @class in source.Classes.Where(static c => c.Length > 0 && c[0] != ':'))
        {
            button.Classes.Add(@class);
        }

        Mirror(button, source, ContentControl.ContentProperty, lifetime);
        Mirror(button, source, Button.CommandProperty, lifetime);
        Mirror(button, source, Button.CommandParameterProperty, lifetime);
        Mirror(button, source, Visual.IsVisibleProperty, lifetime);
        Mirror(button, source, InputElement.IsEnabledProperty, lifetime);
        Mirror(button, source, ButtonAssist.ShowProgressProperty, lifetime);
        return button;
    }

    private static void Mirror<T>(
        AvaloniaObject target,
        AvaloniaObject source,
        AvaloniaProperty<T> property,
        CloneLifetime lifetime)
    {
        lifetime.Add(target.Bind(property, source.GetObservable(property)));
    }

    private static void DisposeRow(Control row)
    {
        foreach (var control in row.GetVisualDescendants().OfType<Control>().Prepend(row))
        {
            if (control.GetValue(CloneLifetimeProperty) is not { } lifetime)
            {
                continue;
            }

            lifetime.Dispose();
            control.ClearValue(CloneLifetimeProperty);
        }
    }

    private static IEnumerable<Header> CollectHeaders(Control root, ScrollViewer scrollViewer)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            int? level = control.Classes.Contains("H1") ? 1
                : control.Classes.Contains("H2") ? 2
                : control.Classes.Contains("H3") ? 3
                : null;
            if (level is null || control.Bounds.Height <= 0)
            {
                continue;
            }

            var top = control.Bounds.Y;
            foreach (var ancestor in control.GetVisualAncestors())
            {
                if (ReferenceEquals(ancestor, root))
                {
                    yield return new Header(level.Value, top - scrollViewer.Offset.Y, control);
                    break;
                }

                top += ancestor.Bounds.Y;
            }
        }
    }

    private sealed class CloneLifetime : IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private bool _disposed;

        public void Add(IDisposable subscription)
        {
            if (_disposed)
            {
                subscription.Dispose();
                return;
            }

            _subscriptions.Add(subscription);
        }

        public void Add(Action dispose) => Add(new CallbackDisposable(dispose));

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var index = _subscriptions.Count - 1; index >= 0; index--)
            {
                _subscriptions[index].Dispose();
            }

            _subscriptions.Clear();
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _callback = callback;

        public void Dispose()
        {
            var callbackToRun = _callback;
            _callback = null;
            callbackToRun?.Invoke();
        }
    }
}
