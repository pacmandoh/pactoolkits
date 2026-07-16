using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

internal static class DialogAwaiter
{
    internal static Task<T> RunOnUiThread<T>(Action<TaskCompletionSource<T>> show)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Run()
        {
            try
            {
                show(tcs);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        if (Dispatcher.UIThread.CheckAccess() || global::Avalonia.Application.Current is null)
        {
            // Design-time / no Application: run on the caller thread.
            Run();
        }
        else
        {
            Dispatcher.UIThread.Invoke(Run);
        }

        return tcs.Task;
    }

    internal static Task<T> RunAlertAsync<T>(DialogManager manager, Action<TaskCompletionSource<T>> show)
        => RunOnUiThread<T>(tcs =>
        {
            DialogSessionStack.PrepareForAlert(manager);
            show(tcs);
        });
}

/// <summary>
/// ShadUI registers dialog callbacks by VM type with TryAdd (never overwrites). Singleton form VMs
/// need slot reassignment plus orphan control cleanup before each Show. <see cref="FormBase"/> session
/// completion completes awaiters even when ShadUI clears slots before Close.
/// </summary>
internal static class DialogSessionStack
{
    private static readonly FieldInfo DialogsField =
        typeof(DialogManager).GetField("Dialogs", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo CloseDialogMethod =
        typeof(DialogManager).GetMethod("CloseDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly string[] CallbackFields =
    [
        "OnSuccessCallbacks",
        "OnSuccessWithContextCallbacks",
        "OnSuccessAsyncCallbacks",
        "OnSuccessWithContextAsyncCallbacks",
        "OnCancelCallbacks",
        "OnCancelAsyncCallbacks"
    ];

    private static readonly FieldInfo CustomDialogsField =
        typeof(DialogManager).GetField("CustomDialogs", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static void PrepareShow(DialogManager manager, Type contextType)
    {
        ClearCallbackSlots(manager, contextType);
        DismissControlsOfType(manager, contextType);
    }

    internal static void PrepareForAlert(DialogManager manager)
    {
        DismissOpenControls(manager);
        ClearAllCallbackSlots(manager);
    }

    internal static void RegisterCallbacks(
        DialogManager manager,
        Type contextType,
        Action success,
        Action cancel)
    {
        GetCallbackDictionary(manager, "OnSuccessCallbacks")[contextType] = success;
        GetCallbackDictionary(manager, "OnCancelCallbacks")[contextType] = cancel;
    }

    internal static int CountOpenControlsOfType(DialogManager manager, Type contextType)
        => CollectControlsOfType(manager, contextType).Count;

    internal static void ClearCallbackSlots(DialogManager manager, Type contextType)
    {
        foreach (var fieldName in CallbackFields)
        {
            GetCallbackDictionary(manager, fieldName).Remove(contextType);
        }
    }

    internal static void BindSimpleDialogDismiss(DialogManager manager, Action onDismiss)
    {
        var simpleDialogType = manager.GetType().Assembly.GetType("ShadUI.SimpleDialog")
            ?? throw new InvalidOperationException("Missing ShadUI.SimpleDialog.");

        RegisterCallbacks(manager, simpleDialogType, static () => { }, onDismiss);

        foreach (var control in GetDialogControls(manager))
        {
            if (control.GetType() != simpleDialogType || control is not StyledElement element)
            {
                continue;
            }

            // SimpleDialog X/light-dismiss routes by VM type; self DataContext wires the callback.
            element.DataContext = element;
        }
    }

    internal static void UnbindSimpleDialogDismiss(DialogManager manager)
    {
        var simpleDialogType = manager.GetType().Assembly.GetType("ShadUI.SimpleDialog");
        if (simpleDialogType is null)
        {
            return;
        }

        ClearCallbackSlots(manager, simpleDialogType);
    }

    private static void ClearAllCallbackSlots(DialogManager manager)
    {
        foreach (var fieldName in CallbackFields)
        {
            GetCallbackDictionary(manager, fieldName).Clear();
        }
    }

    private static void DismissOpenControls(DialogManager manager)
    {
        foreach (var control in GetDialogControls(manager).ToList())
        {
            CloseDialogMethod.Invoke(manager, [control]);
        }
    }

    private static void DismissControlsOfType(DialogManager manager, Type contextType)
    {
        TryGetRegisteredViewType(manager, contextType, out var registeredViewType);

        foreach (var control in GetDialogControls(manager).ToList())
        {
            if (!MatchesContextType(control, contextType, registeredViewType))
            {
                continue;
            }

            CloseDialogMethod.Invoke(manager, [control]);
        }
    }

    private static List<Control> CollectControlsOfType(DialogManager manager, Type contextType)
    {
        TryGetRegisteredViewType(manager, contextType, out var registeredViewType);

        var controls = new List<Control>();
        foreach (var control in GetDialogControls(manager))
        {
            if (MatchesContextType(control, contextType, registeredViewType))
            {
                controls.Add(control);
            }
        }

        return controls;
    }

    private static bool TryGetRegisteredViewType(DialogManager manager, Type contextType, out Type? registeredViewType)
    {
        var dict = (IDictionary)CustomDialogsField.GetValue(manager)!;
        if (dict.Contains(contextType))
        {
            registeredViewType = (Type)dict[contextType]!;
            return true;
        }

        registeredViewType = null;
        return false;
    }

    private static bool MatchesContextType(Control control, Type contextType, Type? registeredViewType)
    {
        if (control is StyledElement { DataContext: { } dc } && dc.GetType() == contextType)
        {
            return true;
        }

        return registeredViewType is not null && control.GetType() == registeredViewType;
    }

    private static List<Control> GetDialogControls(DialogManager manager)
    {
        var dict = (IDictionary)DialogsField.GetValue(manager)!;
        var controls = new List<Control>();
        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Key is Control control)
            {
                controls.Add(control);
            }
        }

        return controls;
    }

    private static IDictionary GetCallbackDictionary(DialogManager manager, string fieldName)
        => (IDictionary)typeof(DialogManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
}

internal static class FormDialogSession
{
    internal static Task<TResult> ShowAsync<TContext, TResult>(
        DialogManager manager,
        TContext context,
        Action<TContext>? prepare,
        Func<TContext, TResult> onSuccess,
        Func<TResult> onCancel,
        double maxWidth = 512,
        bool dismissible = false)
        where TContext : class
    {
        var contextType = typeof(TContext);
        return DialogAwaiter.RunOnUiThread<TResult>(tcs =>
        {
            prepare?.Invoke(context);
            DialogSessionStack.PrepareShow(manager, contextType);

            var completed = 0;
            Action<bool> completeSession = success =>
            {
                // ShadUI can invoke success and cancel on the same dismiss path.
                if (Interlocked.CompareExchange(ref completed, 1, 0) != 0)
                {
                    return;
                }

                if (context is FormBase form)
                {
                    form.BindSessionCompletion(null);
                }

                tcs.TrySetResult(success ? onSuccess(context) : onCancel());
            };

            if (context is FormBase bind)
            {
                bind.BindSessionCompletion(completeSession);
            }

            Action successCallback = () => completeSession(true);
            Action cancelCallback = () => completeSession(false);

            ShowCustomDialog(
                manager,
                context,
                contextType,
                maxWidth,
                dismissible,
                successCallback,
                cancelCallback);
        });
    }

    private static void ShowCustomDialog<TContext>(
        DialogManager manager,
        TContext context,
        Type contextType,
        double maxWidth,
        bool dismissible,
        Action successCallback,
        Action cancelCallback)
        where TContext : class
    {
        DialogSessionStack.RegisterCallbacks(manager, contextType, successCallback, cancelCallback);

        var dialog = manager.CreateDialog(context)
            .WithMaxWidth(maxWidth)
            .WithSuccessCallback(successCallback)
            .WithCancelCallback(cancelCallback);

        if (dismissible)
        {
            dialog.Dismissible();
        }

        dialog.Show();

        EnsureOpenControlRegistered(manager, contextType);
    }

    internal static void EnsureOpenControlRegistered(DialogManager manager, Type contextType)
    {
        if (DialogSessionStack.CountOpenControlsOfType(manager, contextType) > 0)
        {
            return;
        }

        // Show() must register synchronously; otherwise the awaiter would hang forever.
        throw new InvalidOperationException(
            $"Custom dialog for {contextType.FullName} did not register an open control after Show.");
    }
}
