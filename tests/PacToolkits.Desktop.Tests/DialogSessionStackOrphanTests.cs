using System.Reflection;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class DialogSessionStackOrphanTests
{
    [Fact]
    public void PrepareShow_dismisses_registered_view_without_datacontext()
    {
        var manager = new DialogManager();
        manager.Register<StubDialogView, StubDialogViewModel>();

        var orphan = new StubDialogView { DataContext = null };
        GetDialogs(manager)[orphan] = new DialogOptions();

        DialogSessionStack.PrepareShow(manager, typeof(StubDialogViewModel));

        Assert.Empty(GetDialogs(manager));
    }

    private sealed class StubDialogView : Control;

    private sealed class StubDialogViewModel;

    private static Dictionary<Control, DialogOptions> GetDialogs(DialogManager manager)
    {
        var field = typeof(DialogManager).GetField("Dialogs", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (Dictionary<Control, DialogOptions>)field!.GetValue(manager)!;
    }
}
