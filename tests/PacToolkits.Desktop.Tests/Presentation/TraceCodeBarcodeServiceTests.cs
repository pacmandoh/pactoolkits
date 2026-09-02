using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Barcode;
using SkiaSharp;

namespace PacToolkits.Desktop.Tests.Presentation;

public sealed class TraceCodeBarcodeServiceTests
{
    [Fact]
    public void FitTextToWidth_ellipsis_when_text_exceeds_width()
    {
        using var font = new SKFont(SKTypeface.Default, 16f);
        using var paint = new SKPaint { IsAntialias = true };

        var fitted = TraceCodeBarcodeService.FitTextToWidth(
            "abcdefghijklmnopqrstuvwxyz",
            font,
            paint,
            maxWidth: 80f);

        Assert.EndsWith("…", fitted);
        Assert.True(font.MeasureText(fitted, paint) <= 80f);
    }

    [Fact]
    public void BuildFileName_preserves_code_within_file_name_limit()
    {
        var service = new TraceCodeBarcodeService();
        var code = new string('8', 128);

        var fileName = service.BuildFileName(
            new TraceBarcodeLabelInput(code, new string('d', 128), new string('s', 128), null, null, null),
            new BarcodeGenExportOptions { FileNamePrefix = new string('p', 64) });

        Assert.True(fileName.Length <= TraceCodeBarcodeService.MaxFileNameLength);
        Assert.EndsWith($"_{code}.png", fileName);
    }

    [Fact]
    public void BuildFileName_strips_parent_directory_segments()
    {
        var service = new TraceCodeBarcodeService();
        var fileName = service.BuildFileName(
            new TraceBarcodeLabelInput("..\\evil", "d", "s", null, null, null),
            new BarcodeGenExportOptions());

        Assert.DoesNotContain("..", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderPng_returns_non_empty_png()
    {
        var service = new TraceCodeBarcodeService();
        var png = service.RenderPng(
            new TraceBarcodeLabelInput("12345678901234567890", "药品A", "10mg", 10, 5, DateTimeOffset.Now),
            new BarcodeGenOptions(),
            DateTimeOffset.Now);

        Assert.NotEmpty(png);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
    }
}
