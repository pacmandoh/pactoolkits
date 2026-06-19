using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeView : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };
    private bool _syncingSelection;
    private bool _syncingScroll;
    private ScrollViewer? _traceCodeInputScroll;
    private ScrollViewer? _traceCodePreviewScroll;

    public ScanCodeView()
    {
        InitializeComponent();
        AttachDrugFilter();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => AttachTraceCodeScrollSync();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => DetachTraceCodeScrollSync();

    private void AttachTraceCodeScrollSync()
    {
        DetachTraceCodeScrollSync();

        if (this.FindControl<TextBox>("TraceCodeInput") is not { } input)
        {
            return;
        }

        if (this.FindControl<ScrollViewer>("TraceCodePreviewScroll") is not { } preview)
        {
            return;
        }

        _traceCodePreviewScroll = preview;
        preview.ScrollChanged += OnTraceCodePreviewScrollChanged;

        void AttachInputScroll()
        {
            if (_traceCodeInputScroll is not null)
            {
                return;
            }

            var inputScroll = input.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (inputScroll is null)
            {
                return;
            }

            _traceCodeInputScroll = inputScroll;
            inputScroll.ScrollChanged += OnTraceCodeInputScrollChanged;
        }

        input.TemplateApplied += (_, _) => AttachInputScroll();
        AttachInputScroll();
    }

    private void DetachTraceCodeScrollSync()
    {
        if (_traceCodeInputScroll is { } inputScroll)
        {
            inputScroll.ScrollChanged -= OnTraceCodeInputScrollChanged;
            _traceCodeInputScroll = null;
        }

        if (_traceCodePreviewScroll is { } previewScroll)
        {
            previewScroll.ScrollChanged -= OnTraceCodePreviewScrollChanged;
            _traceCodePreviewScroll = null;
        }
    }

    private void OnTraceCodeInputScrollChanged(object? sender, ScrollChangedEventArgs e)
        => SyncTraceCodeScroll(fromInput: true);

    private void OnTraceCodePreviewScrollChanged(object? sender, ScrollChangedEventArgs e)
        => SyncTraceCodeScroll(fromInput: false);

    private void SyncTraceCodeScroll(bool fromInput)
    {
        if (_syncingScroll || _traceCodeInputScroll is null || _traceCodePreviewScroll is null)
        {
            return;
        }

        var source = fromInput ? _traceCodeInputScroll : _traceCodePreviewScroll;
        var target = fromInput ? _traceCodePreviewScroll : _traceCodeInputScroll;

        _syncingScroll = true;
        try
        {
            target.Offset = new Vector(source.Offset.X, source.Offset.Y);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteHelper.HandleEnterCommitAndApply(
            this,
            sender,
            e,
            "SpecBox",
            () =>
            {
                if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.ScanCodeViewModel vm)
                {
                    return;
                }

                var cmd = vm.ApplyDrugFilterCommand;
                if (cmd?.CanExecute(null) == true)
                {
                    cmd.Execute(null);
                }
            });
    }

    private void CodeEditor_OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.ScanCodeViewModel vm)
        {
            return;
        }

        var cmd = vm.EnsureEditorContextCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    private void TraceCodeInputBlocked_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.ScanCodeViewModel vm)
        {
            return;
        }

        vm.EnsureEditorContextCommand.Execute(null);
        e.Handled = true;
    }

    private void OnBrowsingGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (sender is not DataGrid activeGrid)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            foreach (var grid in GetBrowsingGrids())
            {
                if (!ReferenceEquals(grid, activeGrid))
                {
                    grid.SelectedItem = null;
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private IEnumerable<DataGrid> GetBrowsingGrids()
    {
        foreach (var name in BrowsingGridNames)
        {
            if (this.FindControl<DataGrid>(name) is { } grid)
            {
                yield return grid;
            }
        }
    }

    private void AttachDrugFilter()
    {
        if (this.FindControl<PlainAutoCompleteBox>("DrugBox") is not { } box)
        {
            return;
        }

        AutoCompleteHelper.AttachDrugOptionFilter(box);
    }
}
