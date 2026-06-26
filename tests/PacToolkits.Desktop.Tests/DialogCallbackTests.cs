using System.Reflection;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class DialogCallbackTests
{
    [Fact]
    public void Close_success_invokes_parameterless_success_callback()
    {
        var manager = new DialogManager();
        var called = false;

        var callbacks = GetCallbackDictionary(manager, "OnSuccessCallbacks");
        callbacks[typeof(object)] = () => called = true;

        manager.Close(new object(), new CloseDialogOptions { Success = true });

        Assert.True(called);
    }

    [Fact]
    public void Close_cancel_invokes_cancel_callback()
    {
        var manager = new DialogManager();
        var called = false;

        var callbacks = GetCallbackDictionary(manager, "OnCancelCallbacks");
        callbacks[typeof(object)] = () => called = true;

        manager.Close(new object());

        Assert.True(called);
    }

    [Fact]
    public void Close_clears_callback_slots_for_type()
    {
        var manager = new DialogManager();
        var callbacks = GetCallbackDictionary(manager, "OnSuccessCallbacks");
        callbacks[typeof(object)] = static () => { };

        manager.Close(new object());

        Assert.False(callbacks.ContainsKey(typeof(object)));
    }

    private static Dictionary<Type, Action> GetCallbackDictionary(DialogManager manager, string fieldName)
    {
        var field = typeof(DialogManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (Dictionary<Type, Action>)field!.GetValue(manager)!;
    }
}
