using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.UiTests;

public sealed class FormDialogSessionShowTests
{
    [AvaloniaFact]
    public void Custom_dialog_Show_registers_control_synchronously()
    {
        var manager = new DialogManager();
        manager.Register<StubDialogView, StubDialogViewModel>();
        var vm = new StubDialogViewModel();

        DialogSessionStack.RegisterCallbacks(
            manager,
            typeof(StubDialogViewModel),
            static () => { },
            static () => { });

        manager.CreateDialog(vm)
            .Dismissible()
            .WithMaxWidth(512)
            .WithSuccessCallback(static () => { })
            .WithCancelCallback(static () => { })
            .Show();

        Assert.Equal(1, DialogSessionStack.CountOpenControlsOfType(manager, typeof(StubDialogViewModel)));
    }

    [AvaloniaFact]
    public void Custom_dialog_Show_after_PrepareShow_registers_control_synchronously()
    {
        var manager = new DialogManager();
        manager.Register<StubDialogView, StubDialogViewModel>();
        var vm = new StubDialogViewModel();

        DialogSessionStack.PrepareShow(manager, typeof(StubDialogViewModel));
        DialogSessionStack.RegisterCallbacks(
            manager,
            typeof(StubDialogViewModel),
            static () => { },
            static () => { });

        manager.CreateDialog(vm)
            .Dismissible()
            .WithMaxWidth(512)
            .WithSuccessCallback(static () => { })
            .WithCancelCallback(static () => { })
            .Show();

        Assert.Equal(1, DialogSessionStack.CountOpenControlsOfType(manager, typeof(StubDialogViewModel)));
    }

    [AvaloniaFact]
    public void Custom_dialog_can_open_alongside_simple_dialog()
    {
        var manager = new DialogManager();
        manager.Register<StubDialogView, StubDialogViewModel>();
        var vm = new StubDialogViewModel();

        manager.CreateDialog("blocker", "message")
            .WithPrimaryButton("OK", static () => { })
            .Show();

        DialogSessionStack.RegisterCallbacks(
            manager,
            typeof(StubDialogViewModel),
            static () => { },
            static () => { });

        manager.CreateDialog(vm)
            .Dismissible()
            .WithSuccessCallback(static () => { })
            .WithCancelCallback(static () => { })
            .Show();

        Assert.Equal(1, DialogSessionStack.CountOpenControlsOfType(manager, typeof(StubDialogViewModel)));
        Assert.Equal(2, GetDialogControls(manager).Count);
    }

    private static List<object> GetDialogControls(DialogManager manager)
    {
        var field = typeof(DialogManager).GetField("Dialogs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var keys = new List<object>();
        foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)field.GetValue(manager)!)
        {
            keys.Add(entry.Key);
        }

        return keys;
    }

    private sealed class StubDialogView : Control;

    private sealed class StubDialogViewModel : FormBase
    {
        public StubDialogViewModel()
            : base(new DialogManager())
        {
        }
    }
}
