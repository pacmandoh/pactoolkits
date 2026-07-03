using System;
using System.Threading.Tasks;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public partial class MsfxMappingBatchView : UserControl
{
    private MsfxMappingBatch? _attached;

    public MsfxMappingBatchView()
    {
        InitializeComponent();
        AttachDrugAutoComplete();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _attached = null;
        TaskObserve.Observe(TryInitializeSafeAsync(), "MsfxMappingBatchView", "init.detached.fail");
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => await TryInitializeSafeAsync();

    private async Task TryInitializeSafeAsync()
    {
        try
        {
            await TryInitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                "MsfxMappingBatchView",
                "msfx.map.batch.attach_init.fail",
                "Dialog attach initialization failed",
                ex);
        }
    }

    private async Task TryInitializeAsync()
    {
        if (DataContext is not MsfxMappingBatch vm || ReferenceEquals(_attached, vm))
        {
            return;
        }

        _attached = vm;
        await vm.InitializeViewAsync().ConfigureAwait(true);
    }

    private void AttachDrugAutoComplete()
    {
        AutoCompleteFilter.AttachDrugOptionFilter(DrugIdBox);
        AutoCompleteCommit.AttachCandidateCommitApplyAsync(DrugIdBox, this, "SpecBox", ApplyDrugCommitFromBoxAsync);
    }

    private Task ApplyDrugCommitFromBoxAsync(AutoCompleteBox box)
    {
        if (DataContext is MsfxMappingBatch vm && vm.ApplyDrugCommitCommand.CanExecute(null))
        {
            vm.ApplyDrugCommitCommand.Execute(null);
        }

        return Task.CompletedTask;
    }

    private void DrugIdBox_OnKeyDown(object? sender, KeyEventArgs e)
        => _ = AutoCompleteCommit.CommitOnEnterAsync(
            this,
            sender,
            e,
            "SpecBox",
            ApplyDrugCommitFromBoxAsync);

    private void KeywordBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MsfxMappingBatch vm)
        {
            return;
        }

        if (vm.SearchCommand.CanExecute(null))
        {
            vm.SearchCommand.Execute(null);
        }
    }

    private void GroupGrid_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        DataGridInteractionHelper.TrySelectRowFromPointer(
            grid,
            e.Source,
            requireRowHeader: false,
            out _,
            out _);
    }
}
