using System;
using System.Threading.Tasks;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public partial class MsfxMappingBatchDialogView : UserControl
{
    private MsfxMappingBatchDialogViewModel? _attachedVm;

    public MsfxMappingBatchDialogView()
    {
        InitializeComponent();
        AttachDrugAutoComplete();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _attachedVm = null;
        _ = TryInitializeAsync();
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => await TryInitializeAsync();

    private async Task TryInitializeAsync()
    {
        if (DataContext is not MsfxMappingBatchDialogViewModel vm || ReferenceEquals(_attachedVm, vm))
        {
            return;
        }

        _attachedVm = vm;
        await vm.InitializeViewAsync().ConfigureAwait(true);
    }

    private void AttachDrugAutoComplete()
    {
        AutoCompleteFilter.AttachDrugOptionFilter(DrugIdBox);
        AutoCompleteCommit.AttachCandidateCommitApplyAsync(DrugIdBox, this, "SpecBox", ApplyDrugCommitFromBoxAsync);
    }

    private Task ApplyDrugCommitFromBoxAsync(AutoCompleteBox box)
    {
        if (DataContext is MsfxMappingBatchDialogViewModel vm && vm.ApplyDrugCommitCommand.CanExecute(null))
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
        if (e.Key != Key.Enter || DataContext is not MsfxMappingBatchDialogViewModel vm)
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
