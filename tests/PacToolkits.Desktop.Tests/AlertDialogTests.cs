using System.Collections;
using System.Reflection;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class AlertDialogTests
{
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
