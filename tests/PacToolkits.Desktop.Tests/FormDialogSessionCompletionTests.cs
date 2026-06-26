using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class FormDialogSessionCompletionTests
{
    [Fact]
    public void CloseDialog_completes_session_when_shad_callbacks_were_cleared()
    {
        var manager = new DialogManager();
        var vm = new TestFormDialogViewModel(manager);
        var completed = false;

        vm.BindSessionCompletion(success =>
        {
            Assert.True(success);
            completed = true;
        });

        DialogSessionStack.PrepareShow(manager, typeof(TestFormDialogViewModel));
        vm.Submit();

        Assert.True(completed);
    }

    [Fact]
    public void CloseDialog_completes_session_only_once_when_shad_callback_also_runs()
    {
        var manager = new DialogManager();
        var vm = new TestFormDialogViewModel(manager);
        var completedCount = 0;

        Action<bool> complete = _ => Interlocked.Increment(ref completedCount);
        vm.BindSessionCompletion(complete);

        DialogSessionStack.RegisterCallbacks(
            manager,
            typeof(TestFormDialogViewModel),
            () => complete(true),
            () => complete(false));

        vm.Submit();

        Assert.Equal(1, completedCount);
    }

    private sealed class TestFormDialogViewModel(DialogManager dialogManager) : FormDialogViewModelBase(dialogManager)
    {
        public void Submit()
            => CloseDialog(success: true);
    }
}
