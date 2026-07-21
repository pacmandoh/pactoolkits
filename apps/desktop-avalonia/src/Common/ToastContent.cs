using Avalonia.Controls;
using Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>Toast 内容模型</summary>
public static class ToastContent
{
    public const double MaxWidth = 420;

    public static TextBlock ForMessage(string message)
        => new()
        {
            Text = message,
            MaxWidth = MaxWidth,
            TextWrapping = TextWrapping.Wrap,
        };
}
