using System;
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
        if (Application.Current?.TryFindResource(key, ThemeVariant.Default, out var v) == true && v is IBrush b)
            return b;
        if (Application.Current?.TryFindResource(key, ThemeVariant.Light, out var v2) == true && v2 is IBrush b2)
            return b2;
        if (Application.Current?.TryFindResource(key, ThemeVariant.Dark, out var v3) == true && v3 is IBrush b3)
            return b3;
        return fallback;
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

        var level = 10;
        if (parameter is int i)
            level = i;
        else if (parameter is string s && int.TryParse(s, out var j))
            level = j;

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

        var level = 15;
        if (parameter is int i)
            level = i;
        else if (parameter is string s && int.TryParse(s, out var j))
            level = j;


        var key = state switch
        {
            "deprecated" => $"BrushDangerBg{level}",
            "nosplit" => $"BrushWarningBg{level}",
            "both" => $"BrushPurpleBg{level}",
            _ => null,
        };

        if (key != null)
            return ConverterHelpers.FindAppBrush(key, Brushes.Transparent);

        if (level <= 15)
            return Brushes.Transparent;

        var opacity = level switch
        {
            <= 25 => 0.18,
            <= 35 => 0.26,
            _ => 0.35,
        };

        if (Application.Current?.TryFindResource("SukiPrimaryColor", ThemeVariant.Default, out var v) == true &&
            v is Color c)
        {
            return new SolidColorBrush(c, opacity);
        }

        return Brushes.Transparent;
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
        var level = 15;
        if (parameter is int i)
            level = i;
        else if (parameter is string s && int.TryParse(s, out var j))
            level = j;

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

        if (level <= 15)
            return Brushes.Transparent;

        var opacity = level switch
        {
            <= 25 => 0.18,
            <= 35 => 0.26,
            _ => 0.35,
        };

        if (Application.Current?.TryFindResource("SukiPrimaryColor", ThemeVariant.Default, out var v) == true &&
            v is Color c)
        {
            return new SolidColorBrush(c, opacity);
        }

        return Brushes.Transparent;
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
