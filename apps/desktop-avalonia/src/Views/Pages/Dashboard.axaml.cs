using Avalonia.Controls;
using Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class Dashboard : UserControl
{
    public Dashboard()
    {
        InitializeComponent();
        AttachDrugFilter();
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteCommit.CommitOnEnter(this, sender, e, "SpecBox", ApplyDrugFilterFromBox);
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

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not DashboardViewModel vm || !vm.ApplyDrugFilterCommand.CanExecute(null))
        {
            return;
        }

        vm.ApplyDrugFilterCommand.Execute(null);
    }
}
