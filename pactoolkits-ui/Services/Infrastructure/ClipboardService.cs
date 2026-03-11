using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace pactoolkits_ui.Services.Infrastructure;

public interface IClipboardService
{
    Task SetTextAsync(string text);
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
}
