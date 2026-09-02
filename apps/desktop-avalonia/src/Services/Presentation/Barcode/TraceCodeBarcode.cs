using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.SkiaSharp;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Barcode;

/// <summary>追溯码 Code 128 标签 PNG 渲染</summary>
public interface ITraceCodeBarcodeService
{
    byte[] RenderPng(TraceBarcodeLabelInput input, BarcodeGenOptions options, DateTimeOffset generatedAt);
    string BuildFileName(TraceBarcodeLabelInput input, BarcodeGenExportOptions export);
}

/// <summary>Code 128 条码与可配置标签合成</summary>
public sealed class TraceCodeBarcodeService : ITraceCodeBarcodeService
{
    internal const int MaxFileNameLength = 240;
    private const float BodyPadding = 16f;
    private const float SectionGap = 12f;
    private const float LineGap = 6f;
    private const float UnitDividerGap = 8f;
    private const float UnitDividerStroke = 1f;
    private const float UnitSize = 28f;
    private const float TitleSize = 22f;
    private const float InfoSize = 16f;

    private static readonly string[] LabelFontFamilies =
    [
        "PingFang SC",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "Hiragino Sans GB",
        "Source Han Sans SC",
        "Arial Unicode MS"
    ];

    private static readonly Lazy<SKTypeface> LabelTypeface = new(ResolveLabelTypeface);
    private static readonly Lazy<SKTypeface> BoldLabelTypeface = new(() =>
        SKFontManager.Default.MatchFamily(LabelTypeface.Value.FamilyName, SKFontStyle.Bold)
        ?? LabelTypeface.Value);

    public byte[] RenderPng(TraceBarcodeLabelInput input, BarcodeGenOptions options, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        var normalized = BarcodeGenSettingsService.Normalize(options);
        var image = normalized.Image;
        var canvasWidth = image.WidthPx;
        var contentWidth = canvasWidth - BodyPadding * 2;
        var unit = image.Unit;
        var title = BuildTitle(input);
        var infoLines = BuildInfoLines(input, generatedAt);

        using var unitFont = CreateUnitFont(UnitSize);
        using var titleFont = CreateFont(TitleSize);
        using var infoFont = CreateFont(InfoSize);
        using var titlePaint = CreateTextPaint();
        using var infoPaint = CreateTextPaint();

        var fittedUnit = FitTextToWidth(unit, unitFont, titlePaint, contentWidth);
        var fittedTitle = FitTextToWidth(title, titleFont, titlePaint, contentWidth);
        var fittedTraceCode = FitTextToWidth(input.TraceCode, infoFont, infoPaint, contentWidth);
        var fittedInfoLines = infoLines
            .Select(line => FitTextToWidth(line, infoFont, infoPaint, contentWidth))
            .ToArray();

        var unitHeight = string.IsNullOrWhiteSpace(fittedUnit)
            ? 0f
            : MeasureTextHeight(fittedUnit, unitFont, titlePaint) + UnitDividerGap + UnitDividerStroke + SectionGap;
        var titleHeight = string.IsNullOrWhiteSpace(fittedTitle)
            ? 0f
            : MeasureTextHeight(fittedTitle, titleFont, titlePaint);
        var traceCodeHeight = MeasureTextHeight(fittedTraceCode, infoFont, infoPaint);
        var infoHeight = MeasureInfoHeight(fittedInfoLines, infoFont, infoPaint);

        var barcodeHeight = Math.Max(96, (int)(contentWidth / 3f));
        var writer = new BarcodeWriter
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width = (int)contentWidth,
                Height = barcodeHeight,
                Margin = image.QuietZoneModules,
                PureBarcode = true
            }
        };

        using var barcodeBitmap = writer.Write(input.TraceCode);
        var totalHeight = BodyPadding
                          + unitHeight
                          + (string.IsNullOrWhiteSpace(title) ? 0f : titleHeight + SectionGap)
                          + barcodeBitmap.Height
                          + SectionGap
                          + traceCodeHeight
                          + SectionGap
                          + infoHeight
                          + BodyPadding;

        using var surface = SKSurface.Create(new SKImageInfo(canvasWidth, (int)Math.Ceiling(totalHeight)));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var y = BodyPadding;
        if (!string.IsNullOrWhiteSpace(fittedUnit))
        {
            y += DrawCenteredLine(canvas, fittedUnit, unitFont, titlePaint, BodyPadding, y, contentWidth);
            y += UnitDividerGap;
            DrawHorizontalRule(canvas, BodyPadding, y, contentWidth);
            y += UnitDividerStroke + SectionGap;
        }

        if (!string.IsNullOrWhiteSpace(fittedTitle))
        {
            y += DrawCenteredLine(canvas, fittedTitle, titleFont, titlePaint, BodyPadding, y, contentWidth);
            y += SectionGap;
        }

        var barcodeX = BodyPadding + (contentWidth - barcodeBitmap.Width) / 2f;
        canvas.DrawBitmap(barcodeBitmap, barcodeX, y);
        y += barcodeBitmap.Height + SectionGap;

        y += DrawCenteredLine(canvas, fittedTraceCode, infoFont, infoPaint, BodyPadding, y, contentWidth);
        y += SectionGap;

        foreach (var line in fittedInfoLines)
        {
            y += DrawCenteredLine(canvas, line, infoFont, infoPaint, BodyPadding, y, contentWidth);
            y += LineGap;
        }

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string BuildTitle(TraceBarcodeLabelInput input)
        => string.IsNullOrWhiteSpace(input.DrugId) ? string.Empty : input.DrugId;

    private static IReadOnlyList<string> BuildInfoLines(TraceBarcodeLabelInput input, DateTimeOffset generatedAt)
    {
        var lines = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(input.Spec))
        {
            lines.Add($"规格：{input.Spec}");
        }

        lines.Add($"生成时间：{generatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        return lines;
    }

    private static SKTypeface ResolveLabelTypeface()
    {
        var manager = SKFontManager.Default;
        foreach (var family in LabelFontFamilies)
        {
            var typeface = manager.MatchFamily(family);
            if (typeface is not null)
            {
                return typeface;
            }
        }

        return manager.MatchCharacter('药') ?? SKTypeface.Default;
    }

    private static SKFont CreateFont(float size)
        => new(LabelTypeface.Value, size);

    private static SKFont CreateUnitFont(float size)
    {
        var font = new SKFont(BoldLabelTypeface.Value, size);
        if (ReferenceEquals(BoldLabelTypeface.Value, LabelTypeface.Value))
        {
            font.Embolden = true;
        }

        return font;
    }

    private static SKPaint CreateTextPaint()
        => new()
        {
            Color = SKColors.Black,
            IsAntialias = true
        };

    private static float MeasureTextHeight(string text, SKFont font, SKPaint paint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0f;
        }

        _ = font.MeasureText(text, paint);
        var metrics = font.Metrics;
        return metrics.Descent - metrics.Ascent;
    }

    private static float MeasureInfoHeight(
        IReadOnlyList<string> lines,
        SKFont font,
        SKPaint paint)
    {
        if (lines.Count == 0)
        {
            return 0f;
        }

        var height = 0f;
        foreach (var line in lines)
        {
            height += MeasureTextHeight(line, font, paint) + LineGap;
        }

        return Math.Max(0f, height - LineGap);
    }

    private static float DrawCenteredLine(
        SKCanvas canvas,
        string text,
        SKFont font,
        SKPaint paint,
        float x,
        float y,
        float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0f;
        }

        var fitted = FitTextToWidth(text, font, paint, maxWidth);
        var textWidth = font.MeasureText(fitted, paint);
        var drawX = x + Math.Max(0f, (maxWidth - textWidth) / 2f);
        var metrics = font.Metrics;
        canvas.DrawText(fitted, drawX, y - metrics.Ascent, font, paint);
        return metrics.Descent - metrics.Ascent;
    }

    internal static string FitTextToWidth(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0f)
        {
            return text;
        }

        if (font.MeasureText(text, paint) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        var ellipsisWidth = font.MeasureText(ellipsis, paint);
        if (ellipsisWidth >= maxWidth)
        {
            return ellipsis;
        }

        var end = text.Length;
        while (end > 0)
        {
            var candidate = string.Concat(text.AsSpan(0, end), ellipsis);
            if (font.MeasureText(candidate, paint) <= maxWidth)
            {
                return candidate;
            }

            end--;
        }

        return ellipsis;
    }

    private static void DrawHorizontalRule(SKCanvas canvas, float x, float y, float width)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = UnitDividerStroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawLine(x, y, x + width, y, paint);
    }

    public string BuildFileName(TraceBarcodeLabelInput input, BarcodeGenExportOptions export)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(export);
        var drug = SanitizeFilePart(input.DrugId ?? "码");
        var spec = SanitizeFilePart(input.Spec ?? "规格");
        var code = SanitizeFilePart(input.TraceCode);
        var rawPrefix = string.IsNullOrWhiteSpace(export.FileNamePrefix)
            ? BarcodeGenExportOptions.DefaultPrefix
            : export.FileNamePrefix.Trim();
        var prefix = SanitizeFilePart(rawPrefix);
        var suffix = $"_{code}.png";
        var head = $"{prefix}_{drug}_{spec}";
        var maxHeadLength = MaxFileNameLength - suffix.Length;
        if (maxHeadLength <= 0)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.TraceCode)))
                .ToLowerInvariant()[..12];
            var hashSuffix = $"_{hash}.png";
            var codeLength = MaxFileNameLength - hashSuffix.Length;
            return $"{code[..Math.Min(code.Length, codeLength)]}{hashSuffix}";
        }

        if (head.Length > maxHeadLength)
        {
            head = head[..maxHeadLength].TrimEnd(' ', '_');
        }

        return $"{head}{suffix}";
    }

    private static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (ch is '.' && builder.Length > 0 && builder[^1] == '.')
            {
                builder.Append('_');
                continue;
            }

            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}
