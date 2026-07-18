using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;
using MsfxLinkViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.MsfxLink;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class MsfxLinkQueue : UserControl
{
    public MsfxLinkQueue()
    {
        InitializeComponent();
        DataGridRowSelection.AddSelectionChangedHandler(
            AutoTaskQueueGrid,
            OnTaskQueueSelectionChanged);
        DataGridRowSelection.AddSelectionChangedHandler(
            MappingGroupGrid,
            OnMappingGroupSelectionChanged);
        AutoCompleteFilter.AttachDrugOptionFilter(MappingDrugBox);
        AutoCompleteCommit.AttachCandidateCommitApplyAsync(
            MappingDrugBox,
            this,
            "MappingSpecBox",
            ApplyMappingDrugCommitFromBoxAsync);
        MsfxAutoPanelGridInteraction.AttachDetailRowClick(
            AutoTaskQueueGrid,
            allowRowBodyClick: false,
            (vm, row) => vm.ShowTaskQueueDetailCommand.Execute(row),
            vm => vm.IsTaskQueueBatchModeActive);
    }

    private void OnTaskQueueSelectionChanged(
        object? sender,
        DataGridRowSelectionChangedEventArgs e)
    {
        if (DataContext is not MsfxLinkViewModel vm)
        {
            return;
        }

        vm.SyncAutoTaskQueueSelection();
    }

    private void OnMappingGroupSelectionChanged(
        object? sender,
        DataGridRowSelectionChangedEventArgs e)
    {
        if (DataContext is MsfxLinkViewModel vm)
        {
            vm.SyncMappingGroupSelection();
        }
    }

    private Task ApplyMappingDrugCommitFromBoxAsync(AutoCompleteBox box)
    {
        if (DataContext is MsfxLinkViewModel vm
            && vm.ApplyMappingDrugCommitCommand.CanExecute(null))
        {
            vm.ApplyMappingDrugCommitCommand.Execute(null);
        }

        return Task.CompletedTask;
    }

    private void MappingDrugBox_OnKeyDown(object? sender, KeyEventArgs e)
        => _ = AutoCompleteCommit.CommitOnEnterAsync(
            this,
            sender,
            e,
            "MappingSpecBox",
            ApplyMappingDrugCommitFromBoxAsync);

}
