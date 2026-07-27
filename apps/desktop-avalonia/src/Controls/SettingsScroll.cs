using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 管理设置页 H2/H3 粘性标题替换，并抑制标题高度变化造成的布局抖动
/// </summary>
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

    // 保留最近测量的标题槽位高度，避免三级标题跨越二级标题时发生布局跳动
    private static readonly AttachedProperty<double[]> LastSlotHeightsProperty =
        AvaloniaProperty.RegisterAttached<Panel, double[]>("LastSlotHeights", typeof(SettingsScroll));

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
            if (GetStickyHost(scrollViewer) is Panel stickyHost)
            {
                stickyHost.ClearValue(LastSlotHeightsProperty);
            }
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
            stickyHost.ClearValue(LastSlotHeightsProperty);
            stickyHost.Children.Clear();
            SetStickyChromeVisible(stickyHost, false, false);
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

        var headers = CollectHeaders(contentRoot, scrollViewer)
            .OrderBy(static header => header.Top)
            .ToList();
        var positions = headers
            .Select(static header => new HeaderPosition(header.Level, header.Top))
            .ToList();
        var active = SelectActive(positions, GetStickyHeights(stickyHost))
            .Select(index => headers[index])
            .ToList();
        var interactive = active.Any(static header => header.HasActions);
        ApplyStickyState(scrollViewer, stickyHost, active, active.Count > 0, interactive);
    }

    internal static IReadOnlyList<int> SelectActive(
        IReadOnlyList<HeaderPosition> headers,
        IReadOnlyList<double> stickyHeights)
    {
        const double edgeTolerance = 0.5;

        if (stickyHeights.Count != 3)
        {
            throw new ArgumentException("Sticky heights must contain H1, H2, and H3 slots.", nameof(stickyHeights));
        }

        var active = new int?[3];

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var level = header.Level - 1;
            if ((uint)level >= active.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(headers), header.Level, "Header level must be between 1 and 3.");
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
                    parentEdge += stickyHeights[slot];
                }
            }

            var peerEdge = parentEdge;
            if (active[level] is not null)
            {
                peerEdge += stickyHeights[level];
            }

            var fullEdge = peerEdge;
            var hasStickyChildren = false;
            for (var child = level + 1; child < active.Length; child++)
            {
                if (active[child] is null)
                {
                    continue;
                }

                hasStickyChildren = true;
                fullEdge += stickyHeights[child];
            }

            // 下一 peer 碰到 sticky 子级（H3）时：
            // - 仍在 peer slot 底之上 → 只收起 H3，保留当前 H2
            // - 已到/超过 peer slot 底 → 正常 peer 顶替（换 H2、清 H3）
            if (active[level] is not null && hasStickyChildren)
            {
                if (header.Top > fullEdge + edgeTolerance)
                {
                    continue;
                }

                for (var child = level + 1; child < active.Length; child++)
                {
                    active[child] = null;
                }

                if (header.Top > peerEdge + edgeTolerance)
                {
                    continue;
                }

                active[level] = i;
                continue;
            }

            var edge = active[level] is not null ? peerEdge : parentEdge;
            if (header.Top > edge + edgeTolerance)
            {
                continue;
            }

            active[level] = i;
            for (var child = level + 1; child < active.Length; child++)
            {
                active[child] = null;
            }
        }

        return active.Where(static index => index is not null).Select(static index => index!.Value).ToList();
    }

    private static IReadOnlyList<double> GetStickyHeights(Panel stickyHost)
    {
        var measured = new double[3];
        foreach (var row in stickyHost.Children.OfType<Border>())
        {
            var level = ResolveHeaderLevel(row);
            if (level is not { } value || row.Bounds.Height <= 0)
            {
                continue;
            }

            measured[value - 1] = row.Bounds.Height;
        }

        var last = stickyHost.GetValue(LastSlotHeightsProperty) ?? new double[3];
        var merged = MergeSlotHeights(measured, last);
        stickyHost.SetValue(LastSlotHeightsProperty, merged);
        return merged;
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
        for (var i = 0; i < 3; i++)
        {
            // 禁止缩小：带 StatusPill 的 H2/H3 行高于纯标题。活动头切换时若缩小高度，
            // 边界附近反复切换活动标题会持续触发布局计算并阻塞界面线程
            merged[i] = measured[i] > 0
                ? Math.Max(measured[i], lastKnown[i])
                : lastKnown[i];
        }

        return merged;
    }

    private static void ApplyStickyState(
        ScrollViewer scrollViewer,
        Panel stickyHost,
        IReadOnlyList<HeaderSnapshot> active,
        bool showSticky,
        bool interactive)
    {
        var stickyKey = showSticky
            ? string.Join('|', active.Select(static header =>
                $"{header.Level}:{header.Title}:{header.HasActions}:{string.Join(',', header.TitleClasses)}"))
            : string.Empty;

        if (string.Equals(scrollViewer.GetValue(LastStickyKeyProperty), stickyKey, StringComparison.Ordinal)
            && stickyHost.IsVisible == showSticky
            && stickyHost.Children.Count == active.Count
            && GetStickyHostInteractive(stickyHost) == (showSticky && interactive))
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

            SetStickyChromeVisible(stickyHost, false, false);
            return;
        }

        SetStickyChromeVisible(stickyHost, true, interactive);
        SyncStickyChildren(stickyHost, active);

        // 新/空行需要一次 layout；高度已知时跳过再 post，避免 stickyKey 交替
        // 将同一渲染周期内的多次请求合并为一次粘性标题刷新
        if (stickyHost.Children.OfType<Border>().Any(static row => row.Bounds.Height <= 0))
        {
            Dispatcher.UIThread.Post(() => RefreshSticky(scrollViewer), DispatcherPriority.Render);
        }
    }

    private static bool GetStickyHostInteractive(Panel stickyHost)
        => stickyHost.Parent is Border chrome
           && chrome.Classes.Contains("StickyHost")
           && chrome.IsHitTestVisible;

    private static void SetStickyChromeVisible(Panel stickyHost, bool visible, bool interactive)
    {
        if (stickyHost.Parent is Border chrome && chrome.Classes.Contains("StickyHost"))
        {
            chrome.IsVisible = visible;
            chrome.IsHitTestVisible = visible && interactive;
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
        var title = CreateStickyTitle(header);

        if (!header.HasActions)
        {
            return title;
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

        title.VerticalAlignment = VerticalAlignment.Center;
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

    private static Control CreateStickyTitle(HeaderSnapshot header)
    {
        var title = new TextBlock
        {
            Text = header.Title,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var @class in header.TitleClasses)
        {
            title.Classes.Add(@class);
        }

        var pills = GetHeaderStatusPills(header.Source)
            .Select(CloneStatusPill)
            .ToList();
        if (header.Level is not (2 or 3) && pills.Count == 0)
        {
            return title;
        }

        var row = new StackPanel
        {
            Classes = { header.Level == 2 ? "H2Title" : "H3Title" }
        };
        if (header.Level == 2)
        {
            row.Children.Add(CreateH2HashIcon());
        }
        else if (header.Level == 3)
        {
            row.Children.Add(CreateH3HashIcon());
        }

        row.Children.Add(title);
        foreach (var pill in pills)
        {
            row.Children.Add(pill);
        }

        return row;
    }

    private static AppIcon CreateH2HashIcon()
        => new()
        {
            Kind = "Hash",
            Classes = { "H2Hash" },
            VerticalAlignment = VerticalAlignment.Center
        };

    private static AppIcon CreateH3HashIcon()
        => new()
        {
            Kind = "Hash",
            Classes = { "H3Hash" },
            VerticalAlignment = VerticalAlignment.Center
        };

    private static IEnumerable<StatusPill> GetHeaderStatusPills(Control header)
        => header.GetVisualDescendants().OfType<StatusPill>();

    private static StatusPill CloneStatusPill(StatusPill source)
    {
        var pill = new StatusPill
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        foreach (var @class in source.Classes)
        {
            if (@class.Length > 0 && @class[0] == ':')
            {
                continue;
            }

            pill.Classes.Add(@class);
        }

        CopyBind(pill, source, StatusPill.IconProperty);
        CopyBind(pill, source, StatusPill.TextProperty);
        CopyBind(pill, source, Visual.IsVisibleProperty);
        return pill;
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

    private static IEnumerable<HeaderSnapshot> CollectHeaders(Control root, ScrollViewer scrollViewer)
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

            var title = FindHeaderTitle(control);
            if (string.IsNullOrWhiteSpace(title?.Text))
            {
                continue;
            }

            var topLeft = title.TranslatePoint(new Point(0, 0), scrollViewer);
            if (topLeft is null)
            {
                continue;
            }

            var titleClasses = SelectTitleClasses(title.Classes);

            yield return new HeaderSnapshot(
                level.Value,
                title.Text,
                titleClasses,
                topLeft.Value.Y,
                control,
                HasActions(control));
        }
    }

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

    private static TextBlock? FindHeaderTitle(Control header)
        => header.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(static block =>
                block.Classes.Contains("H1Text")
                || block.Classes.Contains("H2Text")
                || block.Classes.Contains("H3Text"));

    internal static IReadOnlyList<string> SelectTitleClasses(IEnumerable<string> classes)
        => classes
            .Where(static @class => @class.Length > 0 && @class[0] != ':')
            .ToArray();

    internal readonly record struct HeaderPosition(int Level, double Top);

    private sealed record HeaderSnapshot(
        int Level,
        string Title,
        IReadOnlyList<string> TitleClasses,
        double Top,
        Control Source,
        bool HasActions);
}
