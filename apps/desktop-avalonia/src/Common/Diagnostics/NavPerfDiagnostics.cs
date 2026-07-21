using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PacToolkits.Desktop.Avalonia.Common.Diagnostics;

/// <summary>
/// 导航、首次 layout、已实现 DataGrid 行的 UI 性能计数
/// 附加调试器或设 PACTOOLKITS_UI_PERF=1 启用（环境变量开启时打 stderr）
/// </summary>
public static class NavPerfDiagnostics
{
    private static readonly ConcurrentDictionary<string, long> Active = new(StringComparer.Ordinal);

    private static readonly bool LogToConsole = string.Equals(
        Environment.GetEnvironmentVariable("PACTOOLKITS_UI_PERF"),
        "1",
        StringComparison.Ordinal);

    private static bool IsEnabled { get; } = Debugger.IsAttached || LogToConsole;

    public static string Begin(string operation)
    {
        if (!IsEnabled)
        {
            return operation;
        }

        var token = $"{operation}:{Guid.NewGuid():N}";
        Active[token] = Stopwatch.GetTimestamp();
        return token;
    }

    public static void End(string token, Control? root, string label)
    {
        if (!IsEnabled || !Active.TryRemove(token, out var started))
        {
            return;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var rows = CountMaterializedDataGridRows(root);
        Log($"[ui-perf] {label}: {elapsedMs:F1} ms, DataGridRows={rows}");
    }

    public static void RecordLayout(Control? root, string label)
    {
        if (!IsEnabled)
        {
            return;
        }

        var rows = CountMaterializedDataGridRows(root);
        Log($"[ui-perf] layout:{label}: DataGridRows={rows}");
    }

    private static void Log(string message)
    {
        Trace.WriteLine(message);
        if (LogToConsole)
        {
            Console.Error.WriteLine(message);
        }
    }

    private static int CountMaterializedDataGridRows(Control? root)
    {
        if (root is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var grid in root.GetVisualDescendants())
        {
            if (grid is not DataGrid dataGrid)
            {
                continue;
            }

            foreach (var row in dataGrid.GetVisualDescendants())
            {
                if (row is DataGridRow { IsVisible: true })
                {
                    count++;
                }
            }
        }

        return count;
    }
}
