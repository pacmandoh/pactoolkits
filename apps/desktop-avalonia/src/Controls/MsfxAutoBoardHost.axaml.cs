using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common.Diagnostics;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using PacToolkits.Desktop.Avalonia.Views.Pages;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// Stagger-mounts the four MSFX auto-board panels one Dispatcher frame at a time after first layout.
/// Skips panels whose queues are still empty; mounts them when data arrives or the panel expands.
/// Pauses reveal while the host is hidden.
/// </summary>
public partial class MsfxAutoBoardHost : Grid
{
    private static readonly (Func<UserControl> Factory, string Key, int Row, int Col)[] PanelDefinitions =
    [
        (() => new MsfxAutoPullPanelView(), "PULL", 0, 0),
        (() => new MsfxAutoMapPanelView(), "MAP", 0, 1),
        (() => new MsfxAutoTaskPanelView(), "TASK", 1, 0),
        (() => new MsfxAutoLogPanelView(), "LOG", 1, 1),
    ];

    private readonly Dictionary<string, UserControl> _panelViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mountedPanelKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _skippedPanelKeys = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _revealCts;
    private bool _revealPaused;
    private bool _revealStarted;
    private bool _revealLoopActive;
    private MsfxLinkViewModel? _vm;

    public MsfxAutoBoardHost()
    {
        InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        MinHeight = 0;
        PropertyChanged += OnHostPropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _revealPaused = !IsVisible;

        if (!_revealStarted)
        {
            _revealStarted = true;
            LayoutUpdated += OnHostFirstLayoutUpdated;
            if (Bounds.Width > 0 || Bounds.Height > 0)
            {
                BeginRevealAfterFirstLayout();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnHostFirstLayoutUpdated;
        PauseReveal();
        UnhookViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnhookViewModel();
        _vm = DataContext as MsfxLinkViewModel;
        _vm?.PropertyChanged += OnViewModelPropertyChanged;

        foreach (var view in _panelViews.Values)
        {
            view.DataContext = DataContext;
        }
    }

    private void UnhookViewModel()
    {
        _vm?.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = null;
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            ResumeReveal();
        }
        else
        {
            PauseReveal();
        }
    }

    private void PauseReveal()
    {
        if (_revealPaused)
        {
            return;
        }

        _revealPaused = true;
        _revealCts?.Cancel();
    }

    private void ResumeReveal()
    {
        if (!_revealPaused)
        {
            return;
        }

        _revealPaused = false;
        if (_mountedPanelKeys.Count == 0 && _skippedPanelKeys.Count > 0)
        {
            _ = TryMountSkippedPanelsAsync();
            return;
        }

        if (_mountedPanelKeys.Count == 0)
        {
            BeginRevealAfterFirstLayout();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MsfxLinkViewModel.AutoExpandedPanel))
        {
            ApplyPanelLayout();
        }

        if (e.PropertyName is nameof(MsfxLinkViewModel.IsAutoPullBatchEmpty)
            or nameof(MsfxLinkViewModel.IsAutoMapQueueEmpty)
            or nameof(MsfxLinkViewModel.IsAutoTaskQueueEmpty)
            or nameof(MsfxLinkViewModel.IsAutoLogsEmpty)
            or nameof(MsfxLinkViewModel.AutoExpandedPanel))
        {
            _ = TryMountSkippedPanelsAsync();
        }
    }

    private void OnHostFirstLayoutUpdated(object? sender, EventArgs e)
        => BeginRevealAfterFirstLayout();

    private void BeginRevealAfterFirstLayout()
    {
        LayoutUpdated -= OnHostFirstLayoutUpdated;
        if (_revealPaused || !IsVisible)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = RevealPanelsAsync(), DispatcherPriority.Background);
    }

    private async Task RevealPanelsAsync()
    {
        if (_revealLoopActive || _revealPaused || !IsVisible)
        {
            return;
        }

        _revealLoopActive = true;
        _revealCts?.Cancel();
        _revealCts?.Dispose();
        _revealCts = new CancellationTokenSource();
        var token = _revealCts.Token;
        var perfToken = NavPerfDiagnostics.Begin("MsfxAutoBoard.Reveal");

        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            var panels = BuildPanelMountOrder();
            for (var i = 0; i < panels.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (_revealPaused || !IsVisible)
                {
                    return;
                }

                var panel = panels[i];
                if (_mountedPanelKeys.Contains(panel.Key))
                {
                    continue;
                }

                if (_vm is not null && !MountPanel(panel.Key, _vm))
                {
                    _skippedPanelKeys.Add(panel.Key);
                    continue;
                }

                await MountPanelAsync(panel, token);
            }

            if (_mountedPanelKeys.Count == 0)
            {
                foreach (var panel in panels)
                {
                    token.ThrowIfCancellationRequested();
                    if (_revealPaused || !IsVisible || _mountedPanelKeys.Contains(panel.Key))
                    {
                        continue;
                    }

                    await MountPanelAsync(panel, token);
                }
            }

            UpdateSkeletonVisibility();
            NavPerfDiagnostics.RecordLayout(this, "MsfxAutoBoard");
            NavPerfDiagnostics.End(perfToken, this, "MsfxAutoBoard.Reveal");
        }
        catch (OperationCanceledException)
        {
            // Host hidden before reveal finished.
        }
        finally
        {
            _revealLoopActive = false;
        }
    }

    private async Task TryMountSkippedPanelsAsync()
    {
        if (_vm is null || _skippedPanelKeys.Count == 0 || _revealPaused || !IsVisible)
        {
            return;
        }

        _revealCts?.Cancel();
        _revealCts?.Dispose();
        _revealCts = new CancellationTokenSource();
        var token = _revealCts.Token;

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        foreach (var key in _skippedPanelKeys.ToArray())
        {
            token.ThrowIfCancellationRequested();
            if (_vm is null || !MountPanel(key, _vm))
            {
                continue;
            }

            var panel = GetPanelDefinition(key);
            if (panel is null || _mountedPanelKeys.Contains(key))
            {
                continue;
            }

            await MountPanelAsync(panel.Value, token);
        }

        UpdateSkeletonVisibility();
    }

    private async Task MountPanelAsync(
        (Func<UserControl> Factory, string Key, int Row, int Col) panel,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var view = panel.Factory();
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Stretch;
        view.MinHeight = 0;
        view.DataContext = DataContext;

        Grid.SetRow(view, panel.Row);
        Grid.SetColumn(view, panel.Col);
        Grid.SetRowSpan(view, 1);
        Grid.SetColumnSpan(view, 1);

        Children.Add(view);
        _panelViews[panel.Key] = view;
        _mountedPanelKeys.Add(panel.Key);
        _skippedPanelKeys.Remove(panel.Key);
        ApplyPanelLayout();

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private void ApplyPanelLayout()
    {
        var expanded = _vm?.AutoExpandedPanel ?? string.Empty;
        var hasExpanded = !string.IsNullOrWhiteSpace(expanded);

        foreach (var (_, key, row, col) in PanelDefinitions)
        {
            if (!_panelViews.TryGetValue(key, out var view))
            {
                continue;
            }

            var isThisExpanded = hasExpanded
                && string.Equals(expanded, key, StringComparison.OrdinalIgnoreCase);

            if (isThisExpanded)
            {
                Grid.SetRow(view, 0);
                Grid.SetColumn(view, 0);
                Grid.SetRowSpan(view, 2);
                Grid.SetColumnSpan(view, 2);
                view.IsVisible = true;
                continue;
            }

            Grid.SetRow(view, row);
            Grid.SetColumn(view, col);
            Grid.SetRowSpan(view, 1);
            Grid.SetColumnSpan(view, 1);
            view.IsVisible = !hasExpanded;
        }
    }

    private void UpdateSkeletonVisibility()
        => SkeletonOverlay.IsVisible = _mountedPanelKeys.Count == 0;

    private static bool MountPanel(string key, MsfxLinkViewModel vm)
    {
        if (string.Equals(vm.AutoExpandedPanel, key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return key switch
        {
            "PULL" => !vm.IsAutoPullBatchEmpty,
            "MAP" => !vm.IsAutoMapQueueEmpty,
            "TASK" => !vm.IsAutoTaskQueueEmpty,
            "LOG" => !vm.IsAutoLogsEmpty,
            _ => true
        };
    }

    private (Func<UserControl> Factory, string Key, int Row, int Col)[]
        BuildPanelMountOrder()
    {
        var panels = PanelDefinitions;
        var expanded = _vm?.AutoExpandedPanel;
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return panels;
        }

        return panels
            .OrderBy(panel => string.Equals(panel.Key, expanded, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToArray();
    }

    private static (Func<UserControl> Factory, string Key, int Row, int Col)?
        GetPanelDefinition(string key)
        => PanelDefinitions.FirstOrDefault(panel => string.Equals(panel.Key, key, StringComparison.OrdinalIgnoreCase));
}
