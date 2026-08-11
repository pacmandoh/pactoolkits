using System.Reflection;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Tests;

public sealed class DialogSessionStackTests
{
    [Fact]
    public void PrepareShow_clears_callback_slots_for_type()
    {
        var manager = new DialogManager();
        var callbacks = GetCallbackDictionary(manager, "OnSuccessCallbacks");
        callbacks[typeof(object)] = static () => { };

        DialogSessionStack.PrepareShow(manager, typeof(object));

        Assert.False(callbacks.ContainsKey(typeof(object)));
    }

    [Fact]
    public void PrepareShow_allows_fresh_success_callback_registration()
    {
        var manager = new DialogManager();
        var firstCalled = false;
        var secondCalled = false;

        GetCallbackDictionary(manager, "OnSuccessCallbacks")[typeof(object)] = () => firstCalled = true;
        DialogSessionStack.PrepareShow(manager, typeof(object));

        var added = GetCallbackDictionary(manager, "OnSuccessCallbacks")
            .TryAdd(typeof(object), () => secondCalled = true);
        Assert.True(added);

        manager.Close(new object(), new CloseDialogOptions { Success = true });

        Assert.False(firstCalled);
        Assert.True(secondCalled);
    }

    [Fact]
    public void PrepareShow_clears_cancel_callback_slots()
    {
        var manager = new DialogManager();
        var callbacks = GetCallbackDictionary(manager, "OnCancelCallbacks");
        callbacks[typeof(object)] = static () => { };

        DialogSessionStack.PrepareShow(manager, typeof(object));

        Assert.False(callbacks.ContainsKey(typeof(object)));
    }

    [Fact]
    public void RegisterCallbacks_replaces_stale_success_handler()
    {
        var manager = new DialogManager();
        var staleCalled = false;
        var freshCalled = false;

        GetCallbackDictionary(manager, "OnSuccessCallbacks")[typeof(object)] = () => staleCalled = true;
        DialogSessionStack.RegisterCallbacks(
            manager,
            typeof(object),
            () => freshCalled = true,
            static () => { });

        manager.Close(new object(), new CloseDialogOptions { Success = true });

        Assert.False(staleCalled);
        Assert.True(freshCalled);
    }

    [Fact]
    public void Close_skips_when_tryadd_failed_to_replace_stale_callback()
    {
        var manager = new DialogManager();
        var completed = false;

        GetCallbackDictionary(manager, "OnSuccessCallbacks")[typeof(object)] = static () => { };
        var added = GetCallbackDictionary(manager, "OnSuccessCallbacks")
            .TryAdd(typeof(object), () => completed = true);

        Assert.False(added);
        manager.Close(new object(), new CloseDialogOptions { Success = true });
        Assert.False(completed);
    }

    private static Dictionary<Type, Action> GetCallbackDictionary(DialogManager manager, string fieldName)
    {
        var field = typeof(DialogManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (Dictionary<Type, Action>)field!.GetValue(manager)!;
    }
}
