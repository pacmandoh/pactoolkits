using System;
using System.Globalization;
using global::Avalonia.Data.Converters;
using global::Avalonia.Media;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;

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

