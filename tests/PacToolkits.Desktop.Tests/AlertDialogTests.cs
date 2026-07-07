using System.Collections;
using System.Reflection;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class AlertDialogTests
{
    [Fact]
    public void Ack_uses_outline_style()
        => Assert.Equal(DialogButtonStyle.Outline, AlertLayout.Style(AlertRole.Ack));

    [Fact]
    public void Cancel_and_dismiss_use_ghost_style()
    {
        Assert.Equal(DialogButtonStyle.Ghost, AlertLayout.Style(AlertRole.Cancel));
        Assert.Equal(DialogButtonStyle.Ghost, AlertLayout.Style(AlertRole.Dismiss));
    }

    [Fact]
    public void Affirm_uses_primary_and_danger_uses_destructive()
    {
        Assert.Equal(DialogButtonStyle.Primary, AlertLayout.Style(AlertRole.Affirm));
        Assert.Equal(DialogButtonStyle.Destructive, AlertLayout.Style(AlertRole.Danger));
    }

    [Fact]
    public void Second_simple_dialog_show_is_blocked_while_first_is_open()
    {
        var manager = new DialogManager();
        var secondPrimaryCalled = false;

        manager.CreateDialog("first", "message")
            .WithPrimaryButton("OK", () => { })
            .Show();

        manager.CreateDialog("second", "message")
            .WithPrimaryButton("OK", () => secondPrimaryCalled = true)
            .Show();

        Assert.False(secondPrimaryCalled);
        Assert.Single(GetDialogControls(manager));
    }

    private static List<object> GetDialogControls(DialogManager manager)
    {
        var field = typeof(DialogManager).GetField("Dialogs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var keys = new List<object>();
        foreach (DictionaryEntry entry in (IDictionary)field.GetValue(manager)!)
        {
            keys.Add(entry.Key);
        }

        return keys;
    }
}
