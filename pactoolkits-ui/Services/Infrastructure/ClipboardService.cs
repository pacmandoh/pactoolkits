using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace pactoolkits_ui.Services.Infrastructure;

public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}

public sealed class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string? text)
        => Dispatcher.UIThread.InvokeAsync((Func<Task>)(async () =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not TopLevel top ||
                top.Clipboard is null)
                return;

            await top.Clipboard.SetTextAsync(text ?? string.Empty);
        }));

    public Task<string?> GetTextAsync()
        => Dispatcher.UIThread.InvokeAsync((Func<Task<string?>>)(async () =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not TopLevel top ||
                top.Clipboard is null)
                return null;

            return await ClipboardExtensions.TryGetTextAsync(top.Clipboard);
        }));
}
