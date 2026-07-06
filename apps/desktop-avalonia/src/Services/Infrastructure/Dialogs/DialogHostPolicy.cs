using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

internal static class DialogHostPolicy
{
    private static readonly StyledProperty<bool> CanDismissWithBackgroundClick =
        ResolveBackgroundDismissProperty();

    internal static void DisableBackgroundDismiss(Window window)
    {
        foreach (var host in window.Hosts.OfType<DialogHost>())
        {
            host.SetValue(CanDismissWithBackgroundClick, false);
        }
    }

    private static StyledProperty<bool> ResolveBackgroundDismissProperty()
    {
        var field = typeof(DialogHost).GetField(
            "CanDismissWithBackgroundClickProperty",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ShadUI DialogHost background dismiss property missing.");

        return (StyledProperty<bool>)field.GetValue(null)!;
    }
}
