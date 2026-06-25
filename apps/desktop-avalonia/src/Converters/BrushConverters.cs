using System;
using System.Collections.Generic;
using System.Globalization;
using global::Avalonia.Data.Converters;
using global::Avalonia.Media;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Converters;

internal enum StatusTone
{
    Done,
    Warning,
    Danger,
    Purple,
    Info,
}

internal static class ConverterHelpers
{
    private static readonly Dictionary<(Color color, double opacity), IBrush> PrimaryTintBrushCache = new();

    public static TxnBadge NormalizeTxnBadge(object? value)
    {
        if (value is TxnBadge b)
        {
            return b;
        }

        if (value is TxnStatus s)
        {
            return s switch
            {
                TxnStatus.Commit => TxnBadge.Done,
                TxnStatus.Rollback => TxnBadge.Warning,
                TxnStatus.Pending => TxnBadge.Danger,
                _ => TxnBadge.Unknown,
            };
        }

        return TxnBadge.Unknown;
    }

    public static IBrush FindAppBrush(string key, IBrush fallback)
        => ThemeBrushResolver.GetBrush(key, fallback);

    public static int MapPacTintLevel(int level)
        => level switch
        {
            <= 10 => 10,
            <= 35 => 20,
            _ => 60,
        };

    public static string NotificationFamily(StatusTone tone)
        => tone switch
        {
            StatusTone.Done => "Success",
            StatusTone.Warning => "Warning",
            StatusTone.Danger => "Error",
            StatusTone.Purple => "Purple",
            _ => "Info",
        };

    public static string NotificationTintKey(StatusTone tone, int pacLevel)
        => $"{NotificationFamily(tone)}Color{MapPacTintLevel(pacLevel)}";

    public static Color FindAppColor(string key, Color fallback)
        => ThemeBrushResolver.TryGetColor(key, out var color) ? color : fallback;

    public static int ParseLevel(object? parameter, int defaultLevel)
    {
        if (parameter is int i)
        {
            return i;
        }

        if (parameter is string s && int.TryParse(s, out var j))
        {
            return j;
        }

        return defaultLevel;
    }

    public static StatusTone BackgroundToneFromTxnBadge(TxnBadge badge)
        => badge switch
        {
            TxnBadge.Done => StatusTone.Done,
            TxnBadge.Warning => StatusTone.Warning,
            TxnBadge.Danger => StatusTone.Danger,
            _ => StatusTone.Purple,
        };

    public static StatusTone ToneFromTraceEntry(TraceEntryState state)
        => state switch
        {
            TraceEntryState.Success => StatusTone.Done,
            TraceEntryState.Warning => StatusTone.Warning,
            TraceEntryState.Failed => StatusTone.Danger,
            TraceEntryState.Discarded => StatusTone.Purple,
            _ => StatusTone.Info,
        };

    public static string ForegroundBrushKey(StatusTone tone)
        => tone switch
        {
            StatusTone.Done => "SuccessColor",
            StatusTone.Warning => "WarningColor",
            StatusTone.Danger => "ErrorColor",
            StatusTone.Purple => "PurpleColor",
            _ => "InfoColor",
        };

    public static string BackgroundBrushKey(StatusTone tone, int level)
        => NotificationTintKey(tone, level);

    public static string IconKindFromTxnBadge(TxnBadge badge)
        => badge switch
        {
            TxnBadge.Done => "CircleCheck",
            TxnBadge.Warning => "Undo",
            TxnBadge.Danger => "CircleAlert",
            _ => "Info",
        };

    public static string IconKindFromTraceEntry(TraceEntryState state)
        => state switch
        {
            TraceEntryState.Success => "CircleCheck",
            TraceEntryState.Warning => "CircleAlert",
            TraceEntryState.Failed => "CircleX",
            TraceEntryState.Discarded => "Ban",
            TraceEntryState.ManualReview => "Bookmark",
            TraceEntryState.Info => "Info",
            _ => "Info",
        };

    public static IBrush GetPrimaryTintBrush(int level)
    {
        if (level <= 15)
        {
            return Brushes.Transparent;
        }

        var opacity = level switch
        {
            <= 25 => 0.18,
            <= 35 => 0.26,
            _ => 0.35,
        };

        var color = FindAppColor("PrimaryColor", Colors.Transparent);
        if (color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0)
        {
            return Brushes.Transparent;
        }

        var key = (color, opacity);
        if (PrimaryTintBrushCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brush = new SolidColorBrush(color, opacity);
        PrimaryTintBrushCache[key] = brush;
        return brush;
    }
}

public sealed class BadgeToIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConverterHelpers.IconKindFromTxnBadge(ConverterHelpers.NormalizeTxnBadge(value));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BadgeToFgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var badge = ConverterHelpers.NormalizeTxnBadge(value);
        var key = badge == TxnBadge.Unknown
            ? "WarningColor"
            : ConverterHelpers.ForegroundBrushKey(ConverterHelpers.BackgroundToneFromTxnBadge(badge));

        return ConverterHelpers.FindAppBrush(key, Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BadgeToBgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var badge = ConverterHelpers.NormalizeTxnBadge(value);
        var level = ConverterHelpers.ParseLevel(parameter, 10);
        var key = ConverterHelpers.BackgroundBrushKey(ConverterHelpers.BackgroundToneFromTxnBadge(badge), level);
        return ConverterHelpers.FindAppBrush(key, Brushes.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class RowStateToBgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = (value as string)?.Trim().ToLowerInvariant();

        var level = ConverterHelpers.ParseLevel(parameter, 15);


        var key = state switch
        {
            "deprecated" => ConverterHelpers.NotificationTintKey(StatusTone.Danger, level),
            "nosplit" => ConverterHelpers.NotificationTintKey(StatusTone.Warning, level),
            "both" => ConverterHelpers.NotificationTintKey(StatusTone.Purple, level),
            _ => null,
        };

        if (key != null)
        {
            return ConverterHelpers.FindAppBrush(key, Brushes.Transparent);
        }

        return ConverterHelpers.GetPrimaryTintBrush(level);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TraceEntryStateToIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TraceEntryState s ? s : TraceEntryState.Unknown;
        return ConverterHelpers.IconKindFromTraceEntry(state);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TraceEntryStateToFgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TraceEntryState s ? s : TraceEntryState.Unknown;
        var key = ConverterHelpers.ForegroundBrushKey(ConverterHelpers.ToneFromTraceEntry(state));
        return ConverterHelpers.FindAppBrush(key, Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TraceEntryStateToBgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TraceEntryState s ? s : TraceEntryState.Unknown;
        var level = ConverterHelpers.ParseLevel(parameter, 10);
        var key = ConverterHelpers.BackgroundBrushKey(ConverterHelpers.ToneFromTraceEntry(state), level);
        return ConverterHelpers.FindAppBrush(key, Brushes.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class LowStockToBgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = ConverterHelpers.ParseLevel(parameter, 15);

        var isDeprecated = value switch
        {
            StockRowItem s => s.IsDeprecated,
            DrugSpecAggRowItem a => a.IsDeprecated,
            _ => false
        };

        if (isDeprecated)
        {
            return ConverterHelpers.FindAppBrush(
                ConverterHelpers.NotificationTintKey(StatusTone.Purple, level),
                Brushes.Transparent);
        }

        var isLow = value switch
        {
            // Inventory detail: highlight rows with zero remaining stock.
            StockRowItem s => s.Remain <= 0 || s.IsLow,
            // Drug-spec aggregate: highlight when remaining is not enough for weekly usage.
            DrugSpecAggRowItem a => a.RemainSum <= a.WeekUsed || a.IsLow,
            // Low-stock tab: every row in this list is low stock by definition.
            LowStockRowItem => true,
            _ => false
        };

        if (isLow)
        {
            return ConverterHelpers.FindAppBrush(
                ConverterHelpers.NotificationTintKey(StatusTone.Danger, level),
                Brushes.Transparent);
        }

        return ConverterHelpers.GetPrimaryTintBrush(level);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ContextStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value switch
        {
            int i => i,
            string s when int.TryParse(s, out var p) => p,
            _ => 0
        };

        var key = level switch
        {
            1 => "SuccessColor",
            2 => "ErrorColor",
            _ => "WarningColor",
        };

        return ConverterHelpers.FindAppBrush(key, Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class KpiPctToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pct = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0d,
        };

        var mode = parameter?.ToString() ?? "Alert";
        var tone = mode.Equals("Remain", StringComparison.OrdinalIgnoreCase)
            ? ToneForRemain(pct)
            : ToneForAlert(pct);

        return ConverterHelpers.FindAppBrush(ToneToBrushKey(tone), Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static StatusTone ToneForRemain(double pct) =>
        pct >= 60 ? StatusTone.Done :
        pct >= 35 ? StatusTone.Info :
        pct >= 15 ? StatusTone.Warning :
        StatusTone.Danger;

    private static StatusTone ToneForAlert(double pct) =>
        pct <= 8 ? StatusTone.Done :
        pct <= 25 ? StatusTone.Warning :
        StatusTone.Danger;

    private static string ToneToBrushKey(StatusTone tone)
        => ConverterHelpers.ForegroundBrushKey(tone);
}

public sealed class BoolToDoneDangerBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOn = value is true;
        var key = isOn ? "SuccessColor" : "ErrorColor";
        return ConverterHelpers.FindAppBrush(key, Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class CellCurrentBorderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ISolidColorBrush solid)
        {
            var c = solid.Color;
            if (c.A == 0)
            {
                return ConverterHelpers.FindAppBrush("PrimaryColor", Brushes.White);
            }

            static byte Mix(byte baseCh, byte to, double factor)
                => (byte)Math.Clamp((int)Math.Round(baseCh + ((to - baseCh) * factor)), 0, 255);

            var toward = (c.R + c.G + c.B) < 380 ? (byte)255 : (byte)32;
            var mixed = Color.FromArgb(
                (byte)Math.Clamp(c.A + 70, 120, 255),
                Mix(c.R, toward, 0.38),
                Mix(c.G, toward, 0.38),
                Mix(c.B, toward, 0.38));

            return new SolidColorBrush(mixed);
        }

        return ConverterHelpers.FindAppBrush("PrimaryColor", Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
