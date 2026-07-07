using System.Collections;
using System.Reflection;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;
using AvaloniaApplication = global::Avalonia.Application;

namespace PacToolkits.Desktop.Tests;

[Collection("Avalonia")]
public sealed class AlertSessionTests
{
    [Fact]
    public async Task ShowAsync_without_Close_throws()
    {
        var manager = new DialogManager();
        var alert = AlertBuilder<bool>.Create("title", "message").Affirm("OK", true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AlertSession.ShowAsync(manager, alert));

        Assert.Contains("Close(...)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dismiss_callback_completes_with_CloseValue()
    {
        var manager = new DialogManager();
        var alert = AlertBuilder<bool>.Create("title", "message")
            .Close(false)
            .Affirm("OK", true);

        var task = AlertSession.ShowAsync(manager, alert);
        await CompleteOnUiThreadAsync(() => InvokeSimpleDialogDismiss(manager));

        Assert.False(await task);
    }

    [Fact]
    public async Task Dismiss_callback_completes_only_once()
    {
        var manager = new DialogManager();
        var alert = AlertBuilder<int>.Create("title", "message")
            .Close(7)
            .Affirm("OK", 99);

        var task = AlertSession.ShowAsync(manager, alert);
        await CompleteOnUiThreadAsync(() => InvokeSimpleDialogDismiss(manager));
        await CompleteOnUiThreadAsync(() => InvokeSimpleDialogDismiss(manager, assertRegistered: false));

        Assert.Equal(7, await task);
    }

    [Fact]
    public async Task Affirm_button_completes_with_button_value()
    {
        var manager = new DialogManager();
        var alert = AlertBuilder<bool>.Create("title", "message")
            .Close(false)
            .Affirm("确认", true);

        var task = AlertSession.ShowAsync(manager, alert);
        await CompleteOnUiThreadAsync(() => InvokeSimpleDialogPrimary(manager));

        Assert.True(await task);
    }

    [Fact]
    public async Task Cancel_button_completes_with_cancel_value()
    {
        var manager = new DialogManager();
        var alert = AlertBuilder<bool>.Create("title", "message")
            .Close(true)
            .Cancel("取消", false)
            .Affirm("确认", true);

        var task = AlertSession.ShowAsync(manager, alert);
        await CompleteOnUiThreadAsync(() => InvokeSimpleDialogCancel(manager));

        Assert.False(await task);
    }

    private static Type SimpleDialogType =>
        typeof(DialogManager).Assembly.GetType("ShadUI.SimpleDialog")
        ?? throw new InvalidOperationException("Missing ShadUI.SimpleDialog.");

    private static void InvokeSimpleDialogDismiss(DialogManager manager, bool assertRegistered = true)
    {
        var callbacks = GetCallbackDictionary(manager, "OnCancelCallbacks");
        if (assertRegistered)
        {
            Assert.True(callbacks.Contains(SimpleDialogType));
        }
        else if (!callbacks.Contains(SimpleDialogType))
        {
            return;
        }

        var action = (Action)callbacks[SimpleDialogType]!;
        action();
    }

    private static void InvokeSimpleDialogPrimary(DialogManager manager)
        => InvokeSimpleDialogButton(manager, "OnPrimaryButtonClick");

    private static void InvokeSimpleDialogCancel(DialogManager manager)
        => InvokeSimpleDialogButton(manager, "OnCancelButtonClick");

    private static void InvokeSimpleDialogButton(DialogManager manager, string methodName)
    {
        foreach (var control in GetDialogControls(manager))
        {
            if (control.GetType() != SimpleDialogType)
            {
                continue;
            }

            var method = control.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(control, [null, new RoutedEventArgs()]);
            return;
        }

        throw new InvalidOperationException("SimpleDialog control not found.");
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

    private static IDictionary GetCallbackDictionary(DialogManager manager, string fieldName)
    {
        var field = typeof(DialogManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (IDictionary)field!.GetValue(manager)!;
    }

    private static async Task CompleteOnUiThreadAsync(Action action)
    {
        if (AvaloniaApplication.Current is null
            || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
