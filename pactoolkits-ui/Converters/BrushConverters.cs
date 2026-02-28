using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Material.Icons;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.ViewModels.Pages;

namespace pactoolkits_ui.Converters;

internal static class ConverterHelpers
{
    private static readonly Dictionary<(Color color, double opacity), IBrush> PrimaryTintBrushCache = new();

    public static TxnBadge NormalizeTxnBadge(object? value)
    {
        if (value is TxnBadge b)
            return b;

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
    {
        var app = Application.Current;
        if (app == null)
            return fallback;

        var variant = app.ActualThemeVariant;
        if (app.TryFindResource(key, variant, out var v) && v is IBrush b)
            return b;
        if (variant != ThemeVariant.Default && app.TryFindResource(key, ThemeVariant.Default, out var v2) && v2 is IBrush b2)
            return b2;

        return fallback;
    }

    public static Color FindAppColor(string key, Color fallback)
    {
        var app = Application.Current;
        if (app == null)
            return fallback;

        var variant = app.ActualThemeVariant;
        if (TryReadColor(app, key, variant, out var color))
            return color;
        if (variant != ThemeVariant.Default && TryReadColor(app, key, ThemeVariant.Default, out color))
            return color;

        return fallback;
    }

    public static int ParseLevel(object? parameter, int defaultLevel)
    {
        if (parameter is int i)
            return i;
        if (parameter is string s && int.TryParse(s, out var j))
            return j;
        return defaultLevel;
    }

    public static IBrush GetPrimaryTintBrush(int level)
    {
        if (level <= 15)
            return Brushes.Transparent;

        var opacity = level switch
        {
            <= 25 => 0.18,
            <= 35 => 0.26,
            _ => 0.35,
        };

        var color = FindAppColor("SukiPrimaryColor", Colors.Transparent);
        if (color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0)
            return Brushes.Transparent;

        var key = (color, opacity);
        if (PrimaryTintBrushCache.TryGetValue(key, out var cached))
            return cached;

        var brush = new SolidColorBrush(color, opacity);
        PrimaryTintBrushCache[key] = brush;
        return brush;
    }

    private static bool TryReadColor(Application app, string key, ThemeVariant variant, out Color color)
    {
        if (app.TryFindResource(key, variant, out var value))
        {
            if (value is Color c)
            {
                color = c;
                return true;
            }

            if (value is ISolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }
        }

        color = default;
        return false;
    }
}

public sealed class BadgeToIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var badge = ConverterHelpers.NormalizeTxnBadge(value);
        return badge switch
        {
            TxnBadge.Done => MaterialIconKind.CheckCircleOutline,
            TxnBadge.Warning => MaterialIconKind.UndoVariant,
            TxnBadge.Danger => MaterialIconKind.AlertCircleOutline,
            _ => MaterialIconKind.InformationOutline,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BadgeToFgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var badge = ConverterHelpers.NormalizeTxnBadge(value);
        var key = badge switch
        {
            TxnBadge.Done => "BrushDone",
            TxnBadge.Warning => "BrushWarning",
            TxnBadge.Danger => "BrushDanger",
            _ => "BrushWarning",
        };

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

        var key = badge switch
        {
            TxnBadge.Done => $"BrushDoneBg{level}",
            TxnBadge.Warning => $"BrushWarningBg{level}",
            TxnBadge.Danger => $"BrushDangerBg{level}",
            _ => $"BrushPurpleBg{level}",
        };

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
            "deprecated" => $"BrushDangerBg{level}",
            "nosplit" => $"BrushWarningBg{level}",
            "both" => $"BrushPurpleBg{level}",
            _ => null,
        };

        if (key != null)
            return ConverterHelpers.FindAppBrush(key, Brushes.Transparent);

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
        return state switch
        {
            TraceEntryState.Success => MaterialIconKind.CheckCircleOutline,
            TraceEntryState.Warning => MaterialIconKind.AlertCircleOutline,
            TraceEntryState.Failed => MaterialIconKind.CloseCircleOutline,
            _ => MaterialIconKind.InformationOutline,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TraceEntryStateToFgBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = value is TraceEntryState s ? s : TraceEntryState.Unknown;

        var key = state switch
        {
            TraceEntryState.Success => "BrushDone",
            TraceEntryState.Warning => "BrushWarning",
            TraceEntryState.Failed => "BrushDanger",
            _ => "BrushWarning",
        };

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

        var key = state switch
        {
            TraceEntryState.Success => "BrushDoneBg10",
            TraceEntryState.Warning => "BrushWarningBg10",
            TraceEntryState.Failed => "BrushDangerBg10",
            _ => "BrushWarningBg10",
        };

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
            return ConverterHelpers.FindAppBrush($"BrushPurpleBg{level}", Brushes.Transparent);

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
            return ConverterHelpers.FindAppBrush($"BrushDangerBg{level}", Brushes.Transparent);

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
            1 => "BrushDone",
            2 => "BrushDanger",
            _ => "BrushWarning",
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
        var key = isOn ? "BrushDone" : "BrushDanger";
        return ConverterHelpers.FindAppBrush(key, Brushes.White);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
