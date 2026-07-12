using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Controls;

public static class SettingsScroll
{
    public static readonly AttachedProperty<Panel?> StickyHostProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Panel?>("StickyHost", typeof(SettingsScroll));

    private static readonly AttachedProperty<bool> IsHookedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsHooked", typeof(SettingsScroll));

    private static readonly AttachedProperty<string?> LastStickyKeyProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, string?>("LastStickyKey", typeof(SettingsScroll));

    private static readonly AttachedProperty<bool> RefreshQueuedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("RefreshQueued", typeof(SettingsScroll));

    static SettingsScroll()
    {
        StickyHostProperty.Changed.AddClassHandler<ScrollViewer>(OnStickyHostChanged);
    }

    public static Panel? GetStickyHost(ScrollViewer scrollViewer) => scrollViewer.GetValue(StickyHostProperty);

    public static void SetStickyHost(ScrollViewer scrollViewer, Panel? value)
        => scrollViewer.SetValue(StickyHostProperty, value);

    private static void OnStickyHostChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (scrollViewer.GetValue(IsHookedProperty))
        {
            scrollViewer.ScrollChanged -= OnScrollChanged;
            scrollViewer.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            scrollViewer.SetValue(IsHookedProperty, false);
        }

        if (e.NewValue is not Panel)
        {
            return;
        }

        scrollViewer.ScrollChanged += OnScrollChanged;
        scrollViewer.DetachedFromVisualTree += OnDetachedFromVisualTree;
        scrollViewer.SetValue(IsHookedProperty, true);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.SetValue(LastStickyKeyProperty, null);
            scrollViewer.SetValue(RefreshQueuedProperty, false);
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            QueueRefresh(scrollViewer);
        }
    }

    public static void ResetSticky(ScrollViewer scrollViewer)
    {
        scrollViewer.SetValue(LastStickyKeyProperty, null);
        scrollViewer.SetValue(RefreshQueuedProperty, false);

        if (GetStickyHost(scrollViewer) is Panel stickyHost)
        {
            stickyHost.Children.Clear();
            SetStickyChromeVisible(stickyHost, false);
        }
    }

    public static void QueueRefresh(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(RefreshQueuedProperty))
        {
            return;
        }

        scrollViewer.SetValue(RefreshQueuedProperty, true);
        Dispatcher.UIThread.Post(() =>
        {
            scrollViewer.SetValue(RefreshQueuedProperty, false);
            RefreshSticky(scrollViewer);
        }, DispatcherPriority.Background);
    }

    public static void ScheduleRefresh(ScrollViewer scrollViewer)
    {
        QueueRefresh(scrollViewer);

        Dispatcher.UIThread.Post(() => RefreshSticky(scrollViewer), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(() => RefreshSticky(scrollViewer), DispatcherPriority.Render);

        if (FindTabPage(scrollViewer) is not Control page)
        {
            return;
        }

        EventHandler? layoutHandler = null;
        layoutHandler = (_, _) =>
        {
            if (scrollViewer.Content is not Control contentRoot || !IsLayoutReady(scrollViewer, contentRoot))
            {
                return;
            }

            page.LayoutUpdated -= layoutHandler!;
            RefreshSticky(scrollViewer);
        };
        page.LayoutUpdated += layoutHandler;
    }

    private static void RefreshSticky(ScrollViewer scrollViewer)
    {
        if (!IsTabPageVisible(scrollViewer))
        {
            return;
        }

        if (scrollViewer.GetValue(StickyHostProperty) is not Panel stickyHost)
        {
            return;
        }

        var contentRoot = scrollViewer.Content as Control;
        if (contentRoot is null || !IsLayoutReady(scrollViewer, contentRoot))
        {
            return;
        }

        var offset = scrollViewer.Offset.Y;
        var headers = CollectHeaders(contentRoot)
            .OrderBy(static header => header.Y)
            .ToList();

        HeaderSnapshot? h1 = null;
        HeaderSnapshot? h2 = null;
        HeaderSnapshot? h3 = null;

        foreach (var header in headers)
        {
            if (header.Y > offset + 0.5)
            {
                break;
            }

            if (header.Level == 1 && !IsHeaderFullyScrolledPast(header, offset))
            {
                continue;
            }

            switch (header.Level)
            {
                case 1:
                    h1 = header;
                    break;
                case 2:
                    h2 = header;
                    break;
                case 3:
                    h3 = header;
                    break;
            }
        }

        var active = new[] { h1, h2, h3 }.Where(static header => header is not null).Cast<HeaderSnapshot>().ToList();
        ApplyStickyState(scrollViewer, stickyHost, active, active.Count > 0);
    }

    private static bool IsHeaderFullyScrolledPast(HeaderSnapshot header, double offset)
        => header.Y + header.Height <= offset + 0.5;

    private static void ApplyStickyState(
        ScrollViewer scrollViewer,
        Panel stickyHost,
        IReadOnlyList<HeaderSnapshot> active,
        bool showSticky)
    {
        var stickyKey = showSticky
            ? string.Join('|', active.Select(static header => $"{header.Level}:{header.Title}"))
            : string.Empty;

        if (string.Equals(scrollViewer.GetValue(LastStickyKeyProperty), stickyKey, StringComparison.Ordinal)
            && stickyHost.IsVisible == showSticky
            && stickyHost.Children.Count == active.Count)
        {
            return;
        }

        scrollViewer.SetValue(LastStickyKeyProperty, stickyKey);

        if (!showSticky)
        {
            if (stickyHost.Children.Count > 0)
            {
                stickyHost.Children.Clear();
            }

            SetStickyChromeVisible(stickyHost, false);
            return;
        }

        SetStickyChromeVisible(stickyHost, true);
        SyncStickyChildren(stickyHost, active);
    }

    private static void SetStickyChromeVisible(Panel stickyHost, bool visible)
    {
        if (stickyHost.Parent is Border chrome && chrome.Classes.Contains("StickyHost"))
        {
            chrome.IsVisible = visible;
        }

        stickyHost.IsVisible = visible;
    }

    private static void SyncStickyChildren(Panel stickyHost, IReadOnlyList<HeaderSnapshot> active)
    {
        while (stickyHost.Children.Count > active.Count)
        {
            stickyHost.Children.RemoveAt(stickyHost.Children.Count - 1);
        }

        while (stickyHost.Children.Count < active.Count)
        {
            stickyHost.Children.Add(CreateStickyRow());
        }

        for (var i = 0; i < active.Count; i++)
        {
            if (stickyHost.Children[i] is not Border row)
            {
                continue;
            }

            var levelClass = active[i].Level switch
            {
                1 => "H1",
                2 => "H2",
                _ => "H3"
            };

            if (!row.Classes.Contains(levelClass) || !row.Classes.Contains("StickyRow"))
            {
                row.Classes.Clear();
                row.Classes.Add(levelClass);
                row.Classes.Add("StickyRow");
            }

            row.Child = BuildStickyContent(active[i]);
        }
    }

    private static Control BuildStickyContent(HeaderSnapshot header)
    {
        var titleClass = header.Level switch
        {
            1 => "H1Text",
            2 => "H2Text",
            _ => "H3Text"
        };

        if (!header.HasActions)
        {
            return new TextBlock
            {
                Text = header.Title,
                Classes = { titleClass }
            };
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

        var title = new TextBlock
        {
            Text = header.Title,
            Classes = { titleClass },
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);

        var actions = new StackPanel
        {
            Classes = { "H2Actions" },
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(actions, 1);

        foreach (var sourceButton in GetActionButtons(header.Source))
        {
            actions.Children.Add(CloneActionButton(sourceButton));
        }

        grid.Children.Add(actions);
        return grid;
    }

    private static Button CloneActionButton(Button source)
    {
        var button = new Button
        {
            Content = source.Content,
            Command = source.Command,
            CommandParameter = source.CommandParameter
        };

        foreach (var @class in source.Classes)
        {
            if (@class.Length > 0 && @class[0] == ':')
            {
                continue;
            }

            button.Classes.Add(@class);
        }

        CopyBind(button, source, Button.IsEnabledProperty);
        CopyBind(button, source, ButtonAssist.ShowProgressProperty);
        return button;
    }

    private static void CopyBind<T>(AvaloniaObject target, AvaloniaObject source, AvaloniaProperty<T> property)
    {
        if (!source.IsSet(property))
        {
            return;
        }

        if (source.GetBindingObservable(property) is IObservable<T> observable)
        {
            target.Bind(property, observable);
            return;
        }

        target.SetValue(property, source.GetValue(property));
    }

    private static IEnumerable<Button> GetActionButtons(Control header)
        => header.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(panel => panel.Classes.Contains("H2Actions"))
            .SelectMany(panel => panel.Children.OfType<Button>());

    private static bool HasActions(Control header)
        => GetActionButtons(header).Any();

    private static Border CreateStickyRow()
        => new()
        {
            Child = new TextBlock()
        };

    private static Control? FindTabPage(ScrollViewer scrollViewer)
        => scrollViewer.GetVisualAncestors()
            .OfType<Control>()
            .FirstOrDefault(static control => control.Classes.Contains("TabPage"));

    private static bool IsTabPageVisible(ScrollViewer scrollViewer)
    {
        if (FindTabPage(scrollViewer) is Control page)
        {
            return page.IsVisible;
        }

        return scrollViewer.IsVisible;
    }

    private static bool IsLayoutReady(ScrollViewer scrollViewer, Control contentRoot)
        => scrollViewer.IsVisible
           && scrollViewer.Bounds.Height > 0
           && contentRoot.Bounds.Height > 0;

    private static IEnumerable<HeaderSnapshot> CollectHeaders(Control root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (control.Classes.Contains("StickyRow"))
            {
                continue;
            }

            var level = ResolveHeaderLevel(control);
            if (level is null)
            {
                continue;
            }

            var title = ResolveHeaderTitle(control);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var topLeft = control.TranslatePoint(new Point(0, 0), root);
            if (topLeft is null)
            {
                continue;
            }

            var height = control.Bounds.Height;
            if (height <= 0)
            {
                height = EstimateHeaderHeight(level.Value);
            }

            yield return new HeaderSnapshot(level.Value, title, topLeft.Value.Y, height, control, HasActions(control));
        }
    }

    private static double EstimateHeaderHeight(int level)
        => level switch
        {
            1 => 52,
            2 => 44,
            _ => 36
        };

    private static int? ResolveHeaderLevel(Control control)
    {
        if (control.Classes.Contains("H1"))
        {
            return 1;
        }

        if (control.Classes.Contains("H2"))
        {
            return 2;
        }

        if (control.Classes.Contains("H3"))
        {
            return 3;
        }

        return null;
    }

    private static string? ResolveHeaderTitle(Control header)
    {
        var title = header.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(static block =>
                block.Classes.Contains("H1Text")
                || block.Classes.Contains("H2Text")
                || block.Classes.Contains("H3Text"));

        return title?.Text;
    }

    private sealed record HeaderSnapshot(
        int Level,
        string Title,
        double Y,
        double Height,
        Control Source,
        bool HasActions);
}
