using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;
using ScanCodeViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.ScanCode;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeManual : UserControl
{
    public ScanCodeManual()
    {
        InitializeComponent();
        AttachDrugFilter();
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteCommit.CommitOnEnter(this, sender, e, "SpecBox", ApplyDrugFilterFromBox);
    }

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not ScanCodeViewModel vm || vm.ApplyDrugFilterCommand?.CanExecute(null) != true)
        {
            return;
        }

        vm.ApplyDrugFilterCommand.Execute(null);
    }

    private void CodeEditor_OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScanCodeViewModel vm || vm.RequireDrugSpecCommand?.CanExecute(null) != true)
        {
            return;
        }

        vm.RequireDrugSpecCommand.Execute(null);
    }

    private void AttachDrugFilter()
    {
        if (this.FindControl<AutoCompleteBox>("DrugBox") is not { } box)
        {
            return;
        }

        AutoCompleteFilter.AttachDrugOptionFilter(box);
        AutoCompleteCommit.AttachCandidateCommitApply(box, this, "SpecBox", ApplyDrugFilterFromBox);
    }
}
