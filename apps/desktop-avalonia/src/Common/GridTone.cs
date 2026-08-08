using System;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>DataGrid 行语义着色档位</summary>
public enum GridTone
{
    None,
    Warning,
    Danger,
}

/// <summary><see cref="Behaviors.DataGridRowTone"/> 默认行 tone 契约</summary>
public interface IRowTone
{
    GridTone RowTone { get; }
}

/// <summary>
/// 库存剩余着色口径：剩余为 0 用 Danger；0 &lt; 剩余 ≤ 阈值用 Warning
/// </summary>
public static class StockTone
{
    public static GridTone Resolve(decimal remain, decimal threshold)
    {
        if (remain <= 0)
        {
            return GridTone.Danger;
        }

        if (remain <= threshold)
        {
            return GridTone.Warning;
        }

        return GridTone.None;
    }
}

/// <summary>
/// 备注关键字着色：含「弃用」用 Danger；含「未拆零」用 Warning；弃用优先
/// </summary>
public static class NoteTone
{
    public static bool IsDeprecated(string? note)
        => (note ?? string.Empty).Contains("弃用", StringComparison.Ordinal);

    public static bool IsNoSplit(string? note)
        => (note ?? string.Empty).Contains("未拆零", StringComparison.Ordinal);

    public static GridTone Resolve(string? note)
    {
        if (IsDeprecated(note))
        {
            return GridTone.Danger;
        }

        if (IsNoSplit(note))
        {
            return GridTone.Warning;
        }

        return GridTone.None;
    }
}
