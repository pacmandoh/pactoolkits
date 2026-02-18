using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace pactoolkits_ui.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}

public sealed class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string? text)
        => Dispatcher.UIThread.InvokeAsync((Func<Task>)(async () =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is TopLevel top)
            {
                if (top.Clipboard != null) await top.Clipboard.SetTextAsync(text ?? string.Empty);
            }
        }));
}