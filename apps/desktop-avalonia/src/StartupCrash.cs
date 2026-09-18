using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PacToolkits.Desktop.Avalonia;

/// <summary>启动期不可恢复异常：不依赖 DI / IAppLogger，写独立日志并弹原生框</summary>
internal static class StartupCrash
{
    internal const int KeepCount = 10;
    internal const string FilePrefix = "startup-crash-";

    internal static void Report(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        string? logPath = null;
        try
        {
            logPath = Write(ex);
        }
        catch
        {
            // 写日志失败时仍尝试提示用户
        }

        try
        {
            Show(logPath);
        }
        catch
        {
            // 弹窗失败时不得再抛，以免盖住原异常路径
        }
    }

    /// <summary>写入时间戳日志并裁剪旧文件，返回路径</summary>
    internal static string Write(Exception ex, string? directory = null, DateTimeOffset? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacToolkits");
        Directory.CreateDirectory(dir);

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(dir, FilePrefix + stamp + ".log");
        if (File.Exists(path))
        {
            path = Path.Combine(dir, FilePrefix + stamp + "-" + Guid.NewGuid().ToString("N")[..8] + ".log");
        }

        var body = new StringBuilder()
            .AppendLine("PacToolkits Desktop startup failure")
            .AppendLine("UTC: " + now.ToString("O", CultureInfo.InvariantCulture))
            .AppendLine()
            .AppendLine(ex.ToString())
            .ToString();

        File.WriteAllText(path, body, Encoding.UTF8);
        Prune(dir);
        return path;
    }

    private static void Prune(string directory, int keep = KeepCount)
    {
        if (keep < 1 || !Directory.Exists(directory))
        {
            return;
        }

        // 文件名含 UTC 时间戳，按名序裁剪，避免 LastWriteTime 粒度相同导致去留不定
        var stale = Directory.EnumerateFiles(directory, FilePrefix + "*.log")
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(keep)
            .ToArray();

        foreach (var path in stale)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 单个旧文件删不掉不阻断本次启动报告
            }
        }
    }

    private static void Show(string? logPath)
    {
        var pathText = string.IsNullOrWhiteSpace(logPath) ? "（未能写入日志文件）" : logPath;
        var text =
            "PacToolkits Desktop 启动失败。" + Environment.NewLine
            + Environment.NewLine
            + "详情已写入：" + Environment.NewLine
            + pathText;
        _ = MessageBoxW(IntPtr.Zero, text, "PacToolkits", 0x00000010u); // MB_ICONERROR
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
