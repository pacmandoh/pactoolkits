using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common.Diagnostics;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 工作区页面 View 保活在 visual tree 中，仅切换可见性
/// 缓存页保持 attach；侧栏切换只改 IsVisible
/// </summary>
public class PageNavigationHost : Grid
{
    public static readonly StyledProperty<IReadOnlyList<AppPageBase>?> PagesProperty =
        AvaloniaProperty.Register<PageNavigationHost, IReadOnlyList<AppPageBase>?>(nameof(Pages));

    public static readonly StyledProperty<AppPageBase?> PageProperty =
        AvaloniaProperty.Register<PageNavigationHost, AppPageBase?>(nameof(Page));

    private readonly Dictionary<AppPageBase, Control> _views = new();
    private bool _warmupScheduled;

    static PageNavigationHost()
    {
        PagesProperty.Changed.AddClassHandler<PageNavigationHost>((host, _) => host.OnPagesChanged());
        PageProperty.Changed.AddClassHandler<PageNavigationHost>((host, _) => host.UpdateActiveVisibility());
    }

    public PageNavigationHost()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
    }

    public IReadOnlyList<AppPageBase>? Pages
    {
        get => GetValue(PagesProperty);
        set => SetValue(PagesProperty, value);
    }

    public AppPageBase? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MountKnownViews();
        UpdateActiveVisibility();
        SchedulePageWarmup();
    }

    private void OnPagesChanged()
    {
        if (Page is not null)
        {
            MountPageView(Page);
        }

        UpdateActiveVisibility();
        SchedulePageWarmup();
    }

    private void MountKnownViews()
    {
        if (Page is not null)
        {
            MountPageView(Page);
        }
    }

    private void MountPageView(AppPageBase page)
    {
        if (_views.ContainsKey(page))
        {
            return;
        }

        var token = NavPerfDiagnostics.Begin($"nav:{page.GetType().Name}");

        var view = BuildPageView(page);
        view.IsVisible = false;
        view.IsHitTestVisible = false;
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(view, 0);
        Grid.SetColumn(view, 0);

        _views[page] = view;
        Children.Add(view);

        view.LayoutUpdated += OnPageViewLayoutUpdated;
        _pendingNavTokens[view] = (token, page.GetType().Name);
    }

    private readonly Dictionary<Control, (string Token, string PageName)> _pendingNavTokens = new();

    private void OnPageViewLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not Control view || !_pendingNavTokens.Remove(view, out var pending))
        {
            return;
        }

        view.LayoutUpdated -= OnPageViewLayoutUpdated;
        NavPerfDiagnostics.End(pending.Token, view, pending.PageName);
    }

    private void UpdateActiveVisibility()
    {
        if (Page is not null && (Pages is null || !_views.ContainsKey(Page)))
        {
            MountPageView(Page);
        }

        foreach (var (page, view) in _views)
        {
            var isActive = ReferenceEquals(page, Page);
            view.IsVisible = isActive;
            view.IsHitTestVisible = isActive;
            view.ZIndex = isActive ? 1 : 0;
        }
    }

    private static Control BuildPageView(AppPageBase page)
    {
        var app = global::Avalonia.Application.Current;
        if (app is not null)
        {
            foreach (var template in app.DataTemplates)
            {
                if (template.Match(page))
                {
                    return template.Build(page)!;
                }
            }
        }

        return new TextBlock { Text = $"No template for {page.GetType().Name}" };
    }

    /// <summary>
    /// 按帧物化缓存页 View，避免首次侧栏切换在 UI 线程一次性承担全部 AXAML 编译成本
    /// </summary>
    private void SchedulePageWarmup()
    {
        if (_warmupScheduled || Pages is null || Pages.Count == 0)
        {
            return;
        }

        _warmupScheduled = true;
        var pending = Pages.Where(page => !_views.ContainsKey(page)).ToList();
        WarmupPageAt(pending, 0);
    }

    private void WarmupPageAt(IReadOnlyList<AppPageBase> pending, int index)
    {
        if (index >= pending.Count)
        {
            return;
        }

        var page = pending[index];
        Dispatcher.UIThread.Post(() =>
        {
            if (!_views.ContainsKey(page))
            {
                MountPageView(page);
            }

            WarmupPageAt(pending, index + 1);
        }, DispatcherPriority.Background);
    }
}
