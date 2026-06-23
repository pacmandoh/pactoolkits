using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PacToolkits.Desktop.Avalonia.Common.Diagnostics;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// Keeps workspace page views alive in the visual tree and toggles visibility.
/// Cached page views stay attached; sidebar switches only change IsVisible.
/// </summary>
public class PageNavigationHost : Grid
{
    public static readonly StyledProperty<IReadOnlyList<AppPageBase>?> PagesProperty =
        AvaloniaProperty.Register<PageNavigationHost, IReadOnlyList<AppPageBase>?>(nameof(Pages));

    public static readonly StyledProperty<AppPageBase?> PageProperty =
        AvaloniaProperty.Register<PageNavigationHost, AppPageBase?>(nameof(Page));

    private readonly Dictionary<AppPageBase, Control> _views = new();

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
        EnsureKnownViews();
        UpdateActiveVisibility();
    }

    private void OnPagesChanged()
    {
        if (Page is not null)
        {
            EnsureView(Page);
        }

        UpdateActiveVisibility();
    }

    private void EnsureKnownViews()
    {
        if (Page is not null)
        {
            EnsureView(Page);
        }
    }

    private void EnsureView(AppPageBase page)
    {
        if (_views.ContainsKey(page))
        {
            return;
        }

        var token = NavigationPerformanceDiagnostics.Begin($"nav:{page.GetType().Name}");

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
        NavigationPerformanceDiagnostics.End(pending.Token, view, pending.PageName);
    }

    private void UpdateActiveVisibility()
    {
        if (Page is not null && (Pages is null || !_views.ContainsKey(Page)))
        {
            EnsureView(Page);
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
}
