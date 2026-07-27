using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Input.Platform;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>定义 Desktop 剪贴板文本访问契约</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
}

/// <summary>通过 Avalonia TopLevel 访问系统剪贴板</summary>
public sealed class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string? text)
        => Dispatcher.UIThread.InvokeAsync((Func<Task>)(async () =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not TopLevel top ||
                top.Clipboard is null)
            {
                return;
            }

            await top.Clipboard.SetTextAsync(text ?? string.Empty);
        }));

    public Task<string?> GetTextAsync()
        => Dispatcher.UIThread.InvokeAsync((Func<Task<string?>>)(async () =>
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not TopLevel top ||
                top.Clipboard is null)
            {
                return null;
            }

            return await ClipboardExtensions.TryGetTextAsync(top.Clipboard);
        }));
}
